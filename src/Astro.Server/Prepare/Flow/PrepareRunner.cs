using System.Runtime.CompilerServices;

namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 촬영 준비 진행자 (docs/implementation/PREPARE.md 2.3·2.4). 묶음 하나(장비 준비 또는 대상)를 맡고, 작업 내용은 모르고 다음만 맡는다:
/// 순서와 끝 버튼, 다시 하기 의존표, 끝 상태 약속 확인, 멈춤 확인(IMPL-01), 실행 번호로 늦은 갱신 차단·오래된 값 표시(IMPL-04),
/// 다시 하기 순서(중단 → 정지 확인 → 이전 결과 차단 → 무효화 → 재실행, IMPL-05). 상태는 서버가 갖고 화면은 그리기만 한다.
/// 묶음 사이의 일(다른 묶음 작업 다시 하기, 다른 묶음이 장비를 쓰기 전 멈추기)은 PrepareFlow가 BeforeRun·ForeignRedo·SuspendAsync로 맡는다.
/// </summary>
public sealed partial class PrepareRunner(IEnumerable<IPrepTask> tasks, ILogger<PrepareRunner> log, RunnerSetup? setup = null)
{
    /// <summary>작업 화면 기록용 (RunHandle이 씀)</summary>
    private ILogger Log => log;
    private readonly RunnerSetup _setup = setup ?? RunnerSetup.Single;

    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    private readonly IReadOnlyList<IPrepTask> _tasks = tasks.ToList();
    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _version;

    private PrepContext? _ctx;
    private readonly Dictionary<string, (PrepTaskStatus Status, string? Result)> _rows = [];
    private Current? _cur;
    private int _runId;
    private CancellationTokenSource? _runCts;
    private Task _runTask = Task.CompletedTask;
    private bool _ready;
    private string? _key;
    /// <summary>다른 묶음이 장비를 쓰는 동안 멈춰 둠 (Handoff = 화면이 갈 묶음). 같은 열쇠로 Start하면 이어서 한다</summary>
    private bool _suspended;
    private string? _handoff;
    /// <summary>정지를 확인하지 못한 작업과, 확인되면 이어서 할 일</summary>
    private (IPrepTask Task, Func<Task> Then)? _stopUnconfirmed;
    private Timer? _heartbeat;
    private Completed? _pendingFinish;

    private sealed class Current(IPrepTask task, int runId)
    {
        public IPrepTask Task { get; } = task;
        public int RunId { get; } = runId;
        public int SubIndex { get; set; }
        public bool Finished { get; set; }
        public GuideView Guide { get; set; } = new(task.Title, "");
        public ReadoutView? Readout { get; set; }
        public bool ReadoutLive { get; set; }
        public StatusLine? Status { get; set; }
        public IReadOnlyList<PrepAction> Actions { get; set; } = [];
        public LiveView? Live { get; set; }
        public TaskCompletionSource<string>? Ask { get; set; }
    }

    public PrepContext? Context { get { lock (_gate) return _ctx; } }

    /// <summary>모의 장비로 도는가 (화면에 알림)</summary>
    public bool Simulated { get; init; } = true;

    public string Group => _setup.Group;
    public IReadOnlyList<IPrepTask> Tasks => _tasks;

    /// <summary>작업 하나를 시작하기 직전 (PrepareFlow: 다른 묶음이 장비를 쓰고 있으면 먼저 멈춘다). false면 시작하지 않는다</summary>
    public Func<string, Task<bool>>? BeforeRun { get; set; }
    /// <summary>이 묶음에 없는 작업을 다시 하자는 요청 (예: 가이딩 → 캘리브레이션). PrepareFlow가 다른 묶음에 넘긴다</summary>
    public Func<string, Task>? ForeignRedo { get; set; }
    /// <summary>작업이 실제로 끝났을 때 (끝 상태 확인까지 통과 — PrepareFlow가 준비 시간을 기록). 건너뛰기·이어서 하기로 넘긴 작업은 부르지 않는다</summary>
    public Action<IPrepTask, object, PrepContext>? TaskFinished { get; set; }
    /// <summary>묻지 않고 넘어가는 작업(AutoNext)의 결과를 보여 주는 시간</summary>
    public TimeSpan AutoNextDelay { get; init; } = TimeSpan.FromSeconds(1.8);

    /// <summary>시작했고 모든 작업이 끝났거나 건너뜀</summary>
    public bool AllDone
    {
        get { lock (_gate) return _ctx is not null && _rows.Values.All(r => r.Status is PrepTaskStatus.Done or PrepTaskStatus.Skipped); }
    }

    // ── 상태 읽기 ─────────

    /// <summary>작업이 진행 중이면 그 작업 이름 (끝나서 다음 버튼을 기다리는 중이면 null) — 적도의 홈 버튼을 막을 때</summary>
    public string? ActiveTaskTitle
    {
        get
        {
            lock (_gate)
                return _cur is { Finished: false } c && _rows[c.Task.Id].Status is PrepTaskStatus.Running or PrepTaskStatus.Waiting ? c.Task.Title : null;
        }
    }

    public PrepView View()
    {
        lock (_gate) return Snapshot();
    }

    private PrepView Snapshot()
    {
        var rows = _tasks.Where(t => _rows.ContainsKey(t.Id))
            .Select(t => new TaskRowView(t.Id, t.Title, _rows[t.Id].Status, _rows[t.Id].Result)).ToList();
        CurrentView? cur = null;
        if (_cur is { } c)
        {
            var subs = c.Task.SubSteps.Select((s, i) => new SubStepView(s.Id, s.Label,
                c.Finished || i < c.SubIndex ? PrepTaskStatus.Done : i == c.SubIndex ? PrepTaskStatus.Running : PrepTaskStatus.Pending)).ToList();
            cur = new CurrentView(c.Task.Id, c.RunId, subs, c.Guide, new CenterView(c.Readout, c.Status, c.Actions), c.Live);
        }
        var skip = TempSkipTarget() is { } t ? new TempSkipView(t.Id, t.Title, !TempSkipSuccessOnly.Contains(t.Id)) : null; // [임시] 건너뛰기
        return new PrepView(_ctx is not null, rows, cur, _ready, _version, Simulated, _setup.Group, _suspended ? _handoff : null, skip);
    }

    /// <summary>상태가 바뀔 때마다 하나씩 (Server-Sent Events)</summary>
    public async IAsyncEnumerable<PrepView> WatchAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var last = -1;
        while (!ct.IsCancellationRequested)
        {
            PrepView view;
            Task changed;
            lock (_gate)
            {
                view = Snapshot();
                changed = _changed.Task;
            }
            if (view.Version != last)
            {
                last = view.Version;
                yield return view;
            }
            try { await changed.WaitAsync(ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>다음 변화까지 기다린다 (테스트용)</summary>
    public Task NextChangeAsync()
    {
        lock (_gate) return _changed.Task;
    }

    private void Changed()
    {
        // _gate 안에서 부른다
        _version++;
        var old = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
    }

    // ── 시작 ─────────

    /// <summary>확정 계획 하나로 시작 (열쇠 = 확정 시각)</summary>
    public void Start(PrepContext ctx) => Start(ctx, ctx.Plan.ConfirmedAt.ToString("O"));

    /// <summary>
    /// 묶음을 시작한다. 같은 열쇠(같은 계획·같은 밤)면 하던 곳에서 그대로(화면 재접속) — 멈춰 둔 상태면 다시 확인할 첫 작업부터 이어서.
    /// 다른 열쇠면 하던 작업과 계속 도는 장비(가이딩)를 멈추고 확인한 뒤 처음부터.
    /// </summary>
    public void Start(PrepContext ctx, string key)
    {
        bool same;
        lock (_gate)
        {
            same = _ctx is not null && _key == key;
            if (same)
            {
                if (!_suspended) return;
                _suspended = false;
                _handoff = null;
                Changed();
            }
        }
        if (same) RunNext();
        else _ = StartFreshAsync(ctx, key);
    }

    /// <summary>끝난 작업 (그날 밤 기록에 남겨 AA를 다시 켰을 때 건너뛰려고)</summary>
    public IReadOnlyList<string> DoneIds()
    {
        lock (_gate) return _rows.Where(r => r.Value.Status == PrepTaskStatus.Done).Select(r => r.Key).ToList();
    }

    /// <summary>
    /// AA를 다시 켜 이어서 시작 (2026-10-08 사용자 결정): 하던 작업·가이딩을 멈춘 뒤, 전에 끝낸 작업(done)은 결과가 남아 있고
    /// 다시 확인(CheckResumeAsync)이 통과하면 끝남으로 둔다. 아니면 다시 한다. 결과 기록은 지우지 않는다(되살린 값)
    /// </summary>
    public async Task StartResumedAsync(PrepContext ctx, string key, IReadOnlySet<string> done)
    {
        if (!await StopCurrentAsync(() => StartResumedAsync(ctx, key, done))) return;
        if (!await StopLingeringAsync(_tasks.Select(t => t.Id).ToList(), () => StartResumedAsync(ctx, key, done))) return;
        var rows = new Dictionary<string, (PrepTaskStatus, string?)>();
        foreach (var t in _tasks)
        {
            if (!t.AppliesTo(ctx)) { rows[t.Id] = (PrepTaskStatus.Skipped, null); continue; }
            var kept = done.Contains(t.Id) && (ResultType(t.Id) is not { } type || ctx.Results.Has(type));
            if (kept)
            {
                EndStateCheck check;
                try { check = await t.CheckResumeAsync(ctx, CancellationToken.None); }
                catch (Exception e) { check = EndStateCheck.Fail(e.Message); }
                kept = check.Ok;
                if (!kept) log.LogInformation("이어서: {Task} 다시 함 ({Problem})", t.Id, check.Problem);
            }
            if (!kept && ResultType(t.Id) is { } drop) ctx.Results.Remove(drop);
            rows[t.Id] = kept ? (PrepTaskStatus.Done, "이어서 · 전에 마침") : (PrepTaskStatus.Pending, null);
        }
        lock (_gate)
        {
            _ctx = ctx;
            _key = key;
            _ready = false;
            _suspended = false;
            _handoff = null;
            _cur = null;
            _rows.Clear();
            foreach (var (id, row) in rows) _rows[id] = row;
            _heartbeat ??= new Timer(_ => Heartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Changed();
        }
        log.LogInformation("준비 이어서 ({Group}): 건너뜀 {Done}", _setup.Group, string.Join(",", rows.Where(r => r.Value.Item1 == PrepTaskStatus.Done).Select(r => r.Key)));
        RunNext();
    }

    private async Task StartFreshAsync(PrepContext ctx, string key)
    {
        // 새 계획: 하던 작업과 끝난 뒤에도 도는 작업(가이딩)을 멈추고 정지를 확인한 뒤 시작
        if (!await StopCurrentAsync(() => StartFreshAsync(ctx, key))) return;
        if (!await StopLingeringAsync(_tasks.Select(t => t.Id).ToList(), () => StartFreshAsync(ctx, key))) return;
        lock (_gate)
        {
            _ctx = ctx;
            _key = key;
            _ready = false;
            _suspended = false;
            _handoff = null;
            _cur = null;
            _rows.Clear();
            // 결과 기록은 두 묶음이 함께 쓴다: 이 묶음의 지난 결과만 지운다
            foreach (var t in _tasks)
                if (ResultType(t.Id) is { } type) ctx.Results.Remove(type);
            if (_tasks.Any(t => t.Id == "flat")) ctx.Results.Remove(typeof(Tasks.Wrap.WrapProgress)); // 마무리를 새로 시작하면 진행도 처음부터
            foreach (var t in _tasks)
                _rows[t.Id] = (t.AppliesTo(ctx) ? PrepTaskStatus.Pending : PrepTaskStatus.Skipped, null);
            _heartbeat ??= new Timer(_ => Heartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Changed();
        }
        log.LogInformation("준비 시작 ({Group}): {Target}", _setup.Group, ctx.HasPlan ? ctx.Plan.TargetName : "계획 없음");
        RunNext();
    }

    /// <summary>다음으로 할 작업(대기 또는 다시 확인 필요)을 시작한다. 없으면 준비 끝</summary>
    private void RunNext()
    {
        IPrepTask? next;
        lock (_gate)
        {
            next = _tasks.FirstOrDefault(t => _rows[t.Id].Status is PrepTaskStatus.Pending or PrepTaskStatus.NeedsRecheck);
        }
        if (next is not null) { RunTask(next); return; }
        // 다시 할 작업이 없음 (예: 멈춰 뒀다가 이어서 했는데 영향받은 작업이 없었음) → 끝 버튼
        lock (_gate)
        {
            if (_cur is not { } c) return;
            c.Status = null;
            c.Actions = [_setup.Finish];
            Changed();
        }
    }

    private void RunTask(IPrepTask task)
    {
        Current cur;
        CancellationToken ct;
        lock (_gate)
        {
            // 앞 실행이 아직 돌고 있으면 멈추라고 알린 뒤 바꾼다 — 예전에는 취소 없이 버려서 앞 작업이 그대로 돌 수 있었다 (L1).
            // 앞 실행이 아직 이 토큰을 쓰고 있을 수 있어 바로 버리지 않고, 그 실행이 끝난 뒤에 정리한다
            if (_runCts is { } old)
            {
                old.Cancel();
                _ = _runTask.ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
            }
            _runCts = new CancellationTokenSource();
            ct = _runCts.Token;
            cur = new Current(task, ++_runId);
            _cur = cur;
            _rows[task.Id] = (PrepTaskStatus.Running, null);
            Changed();
        }
        var handle = new RunHandle(this, cur.RunId);
        _runTask = Task.Run(async () =>
        {
            try
            {
                if (BeforeRun is { } before && !await before(task.Id))
                {
                    lock (_gate)
                    {
                        if (_cur != cur) return;
                        _rows[task.Id] = (PrepTaskStatus.Failed, null);
                        cur.Status = new StatusLine("다른 단계에서 쓰던 장비를 멈추지 못했습니다. 장비 상태를 확인한 뒤 다시 시도해 주세요.", Tone.Fail);
                        cur.Actions = [new("retry", "다시 시도", true)];
                        Changed();
                    }
                    return;
                }
                var outcome = await task.RunAsync(handle, ct);
                await OnOutcomeAsync(cur, outcome);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (MountUnconfirmedException e)
            {
                // 적도의 정지 미확인 (CX-21-03): 오류가 아니라 이유를 보여 주고 다시 시도만
                log.LogWarning("준비 작업을 시작하지 않음: {Task} — {Why}", task.Id, e.Message);
                lock (_gate)
                {
                    if (_cur != cur) return;
                    _rows[task.Id] = (PrepTaskStatus.Failed, null);
                    cur.Status = new StatusLine(e.Message, Tone.Fail);
                    cur.Actions = [new("retry", "다시 시도", true)];
                    Changed();
                }
            }
            catch (Exception e)
            {
                log.LogError(e, "준비 작업 오류: {Task}", task.Id);
                lock (_gate)
                {
                    if (_cur != cur) return;
                    _rows[task.Id] = (PrepTaskStatus.Failed, null);
                    cur.Status = new StatusLine("예상하지 못한 오류로 멈췄습니다. 다시 시도해 주세요.", Tone.Fail);
                    cur.Actions = [new("retry", "다시 시도", true)];
                    Changed();
                }
            }
        }, CancellationToken.None);
    }

    private async Task OnOutcomeAsync(Current cur, TaskOutcome outcome)
    {
        lock (_gate) if (_cur != cur) return; // 지난 실행
        switch (outcome)
        {
            case RedoRequest redo:
                // 제안한 작업도 끝나지 않았으니 다시 확인 필요 (앞 작업을 다시 한 뒤 차례가 오면 다시 한다)
                lock (_gate) _rows[cur.Task.Id] = (PrepTaskStatus.NeedsRecheck, null);
                _ = RedoAsync(redo.TaskId, fromRunning: false);
                return;
            case Completed done:
                _ctx!.Results.Set(done.Result);
                await FinishAsync(cur, done);
                return;
        }
    }

    /// <summary>끝 상태 약속을 장비에서 확인하고, 지켜졌으면 끝 버튼(다음 작업 시작)을 보인다</summary>
    private async Task FinishAsync(Current cur, Completed done)
    {
        lock (_gate)
        {
            if (_cur != cur) return;
            _pendingFinish = done; // 약속이 어긋나 "다시 확인"을 누르면 이것으로 다시
            cur.Status = new StatusLine("장비 상태를 확인하는 중입니다", Tone.Busy);
            cur.Actions = [];
            Changed();
        }
        EndStateCheck check;
        try { check = await cur.Task.CheckEndStateAsync(_ctx!, CancellationToken.None); }
        catch (Exception e) { check = EndStateCheck.Fail($"장비 상태를 읽지 못했습니다 ({e.Message})"); }
        lock (_gate)
        {
            if (_cur != cur) return;
            if (!check.Ok)
            {
                // 약속이 어긋나면 다음으로 넘기지 않는다
                _rows[cur.Task.Id] = (PrepTaskStatus.Failed, null);
                cur.Status = new StatusLine($"{cur.Task.Title}이 끝났지만 장비 상태가 맞지 않습니다: {check.Problem}", Tone.Fail);
                cur.Actions = [new("endcheck", "다시 확인", true), new($"redo:{cur.Task.Id}", "이 작업 다시 하기")];
                Changed();
                return;
            }
            cur.Finished = true;
            try { TaskFinished?.Invoke(cur.Task, done.Result, _ctx!); }
            catch (Exception e) { log.LogWarning(e, "작업 끝 기록을 남기지 못함: {Task}", cur.Task.Id); }
            cur.Guide = new GuideView(done.GuideTitle, done.GuideText);
            cur.Status = null;
            _rows[cur.Task.Id] = (PrepTaskStatus.Done, done.ResultLine);
            var next = _tasks.FirstOrDefault(t => _rows[t.Id].Status is PrepTaskStatus.Pending or PrepTaskStatus.NeedsRecheck);
            if (done.AutoNext && next is not null)
            {
                // 묻지 않고 넘어감: 결과를 잠깐 보여 준 뒤 다음 작업 (그 사이 중단·다시 하기가 오면 넘어가지 않는다)
                cur.Actions = [];
                Changed();
                _ = AutoNextAsync(cur);
                return;
            }
            if (done.AutoNext)
            {
                // 묻지 않는 마지막 작업 (마무리의 장비 정리): 끝 버튼 없이 묶음 끝 → 화면이 다음(요약)으로
                cur.Actions = [];
                _ready = true;
                Changed();
                return;
            }
            var primary = next is null ? _setup.Finish : new PrepAction("next", next.StartLabel, true);
            cur.Actions = [primary, .. next?.StartAlternatives ?? [], .. done.Extra ?? []];
            Changed();
        }
    }

    private async Task AutoNextAsync(Current cur)
    {
        await Task.Delay(AutoNextDelay);
        lock (_gate)
        {
            if (_cur != cur || !cur.Finished || cur.Actions.Count > 0 || _suspended) return;
        }
        RunNext();
    }

    private static Type? ResultType(string taskId) => taskId switch
    {
        "polar" => typeof(PolarResult),
        "calibration" => typeof(CalibrationResult),
        "slew" => typeof(SlewResult),
        "focus" => typeof(FocusResult),
        "focuscheck" => typeof(FocusCheckResult),
        "flat" => typeof(Tasks.Wrap.FlatResult),
        "dark" => typeof(Tasks.Wrap.DarkResult),
        "pack" => typeof(Tasks.Wrap.PackResult),
        "center" => typeof(CenterResult),
        "guiding" => typeof(GuidingResult),
        "test" => typeof(TestShotResult),
        _ => null,
    };

    // ── 오래된 값 표시 (IMPL-04) ─────────

    private void Heartbeat()
    {
        lock (_gate)
        {
            if (_cur is not { ReadoutLive: true, Readout: { Freshness: Freshness.Fresh } r } c) return;
            if (DateTimeOffset.Now - r.ObservedAt <= StaleAfter) return;
            c.Readout = r with { Freshness = Freshness.Stale };
            Changed();
        }
    }

    /// <summary>[테스트] 시간이 지났다고 보고 오래된 값 검사를 바로 한다</summary>
    internal void CheckStaleNow(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_cur is not { ReadoutLive: true, Readout: { Freshness: Freshness.Fresh } r } c) return;
            if (now - r.ObservedAt <= StaleAfter) return;
            c.Readout = r with { Freshness = Freshness.Stale };
            Changed();
        }
    }

    // ── 작업이 화면을 갱신하는 통로 (지난 실행의 갱신은 버린다) ─────────

    private bool Update(int runId, Action<Current> change)
    {
        lock (_gate)
        {
            if (_cur is not { } c || c.RunId != runId) return false;
            change(c);
            Changed();
            return true;
        }
    }
}
