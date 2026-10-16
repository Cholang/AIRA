using System.Runtime.CompilerServices;

namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 촬영 준비 진행자 (docs/implementation/PREPARE.md 2.3·2.4). 묶음 하나(장비 준비 또는 대상)를 맡고, 작업 내용은 모르고 다음만 맡는다:
/// 순서와 끝 버튼, 다시 하기 의존표, 끝 상태 약속 확인, 멈춤 확인(IMPL-01), 실행 번호로 늦은 갱신 차단·오래된 값 표시(IMPL-04),
/// 다시 하기 순서(중단 → 정지 확인 → 이전 결과 차단 → 무효화 → 재실행, IMPL-05). 상태는 서버가 갖고 화면은 그리기만 한다.
/// 묶음 사이의 일(다른 묶음 작업 다시 하기, 다른 묶음이 장비를 쓰기 전 멈추기)은 PrepareFlow가 BeforeRun·ForeignRedo·SuspendAsync로 맡는다.
/// </summary>
public sealed class PrepareRunner(IEnumerable<IPrepTask> tasks, ILogger<PrepareRunner> log, RunnerSetup? setup = null)
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

    private Completed? _pendingFinish;

    // ── 사용자 버튼 ─────────

    /// <summary>버튼을 눌렀을 때. 받을 수 없으면 이유(사용자에게 보여 줄 문장)</summary>
    public string? Act(string actionId)
    {
        Current? cur;
        lock (_gate)
        {
            if (_ctx is null) return "준비가 시작되지 않았습니다.";
            cur = _cur;
            if (cur is null || cur.Actions.All(a => a.Id != actionId)) return "지금은 그 버튼을 쓸 수 없습니다.";
            if (cur.Ask is { } ask)
            {
                cur.Ask = null;
                cur.Actions = [];
                _rows[cur.Task.Id] = (PrepTaskStatus.Running, null);
                Changed();
                ask.TrySetResult(actionId);
                return null;
            }
            // 다음 작업을 시작하는 버튼은 받는 즉시 지운다 — 거의 동시에 두 번 들어와도 같은 작업이 두 번 돌지 않게 (2026-10-11 자체 점검 L1).
            // 새 작업이 시작되면(또는 다음이 없으면 끝 버튼) 버튼이 다시 채워진다
            // 다시 시도·다시 하기·장비 상태 다시 확인도 받는 즉시 지운다 — 정지 확인을 기다리는 동안 한 번 더(또는 다른 화면에서) 눌러
            // 같은 작업이 두 번 시작되던 것 (Codex CX-30-03). 실패하면 각 흐름이 버튼을 다시 놓는다
            if (actionId is "next" or "restart" or "retry" or "device-check" || actionId.StartsWith("next:", StringComparison.Ordinal) || actionId.StartsWith("redo:", StringComparison.Ordinal))
            {
                cur.Actions = [];
                Changed();
            }
        }
        if (actionId == _setup.Finish.Id)
        {
            // 마지막 작업 뒤 끝 버튼: 대상 묶음은 촬영 시작, 장비 준비는 대상 단계로 (화면이 넘어간다)
            lock (_gate) { _ready = true; Changed(); }
            log.LogInformation("묶음 끝 ({Group}): {Action}", _setup.Group, actionId);
            return null;
        }
        // 화면이 다른 단계로 넘어가는 버튼 (예: 센터링 뒤 "다른 대상" → 계획). 러너는 그대로 둔다
        if (actionId.StartsWith("flow:", StringComparison.Ordinal)) return null;
        switch (actionId)
        {
            case "next": RunNext(); break;
            case "retry": _ = RetryAsync(cur.Task); break;
            case "restart": RunNext(); break;
            case "endcheck":
                if (_pendingFinish is { } done) _ = FinishAsync(cur, done);
                break;
            case "device-check": _ = RecheckStopAsync(); break;
            default:
                if (actionId.StartsWith("next:", StringComparison.Ordinal))
                {
                    // 다음 작업을 다른 선택으로 시작 (예: 캘리브레이션을 "가이딩 없이 진행")
                    _ctx!.StartChoice = actionId[5..];
                    RunNext();
                }
                else if (actionId.StartsWith("redo:", StringComparison.Ordinal)) _ = RedoAsync(actionId[5..], fromRunning: true);
                else return "지금은 그 버튼을 쓸 수 없습니다.";
                break;
        }
        return null;
    }

    /// <summary>
    /// 다시 하기 (IMPL-05): 진행 중 작업 중단 → 정지 확인 → 이전 실행 결과 차단(RunId) → 영향받는 결과 무효화 → 재실행.
    /// 정지를 확인하지 못하면 "장비 상태 확인 필요"에서 멈춘다.
    /// </summary>
    public async Task RedoAsync(string taskId, bool fromRunning)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
        {
            // 다른 묶음의 작업 (예: 가이딩 → 캘리브레이션): PrepareFlow가 이 묶음을 멈춰 두고 넘긴다
            if (ForeignRedo is { } foreign) await foreign(taskId);
            return;
        }
        if (fromRunning && !await StopCurrentAsync(() => RedoAsync(taskId, fromRunning: false))) return;
        var affected = new[] { taskId }.Concat(_setup.Dependents.GetValueOrDefault(taskId, [])).ToList();
        // 무효가 되는 작업 중 끝난 뒤에도 장비가 계속 도는 것(가이딩)은 결과만 지우지 않고 실제로 멈춘다 (CX-PREP-CODE-01)
        if (!await StopLingeringAsync(affected, () => RedoAsync(taskId, fromRunning: false))) return;
        lock (_gate)
        {
            if (_ctx is null) return;
            foreach (var id in affected)
            {
                if (!_rows.TryGetValue(id, out var row) || row.Status == PrepTaskStatus.Skipped) continue;
                _rows[id] = (id == taskId ? PrepTaskStatus.Running : PrepTaskStatus.NeedsRecheck, null);
                if (ResultType(id) is { } type) _ctx.Results.Remove(type);
            }
            _ready = false;
            Changed();
        }
        RunTask(task);
    }

    /// <summary>
    /// 준비 중단: 하던 작업을 멈추고, 끝난 뒤에도 도는 작업(가이딩)도 멈춘 뒤 정지를 확인한다.
    /// 작업이 끝나 다음 버튼을 기다리는 중이어도 중단 상태가 된다 — 다음·촬영 시작 버튼을 지운다 (CX-PREP-CODE-03).
    /// "다시 시작"은 아직 안 끝난(또는 멈춰서 다시 확인이 필요한) 첫 작업부터.
    /// </summary>
    public async Task<bool> AbortAsync()
    {
        Func<Task> again = async () => await AbortAsync();
        if (!await StopCurrentAsync(again)) return false;
        if (!await StopLingeringAsync(_tasks.Select(t => t.Id).ToList(), again)) return false;
        MarkAborted();
        return true;
    }

    /// <summary>
    /// 아이라를 끄기 전 (CX-20-02): 하던 작업과 계속 도는 장비(가이딩)를 멈추고 멈춘 것을 확인한다. 상태(결과·표시)는 건드리지 않는다.
    /// 시작하지 않았으면 할 일 없음. 정지를 확인하지 못하면 false (화면은 "장비 상태 다시 확인"을 보여 준다)
    /// </summary>
    public async Task<bool> StopForQuitAsync()
    {
        lock (_gate) if (_ctx is null) return true;
        // 정지를 확인하지 못해 종료를 취소한 뒤 "장비 상태 다시 확인"이 성공하면: 같은 정지를 다시 확인하고 "다시 시작"을 보여 준다 (CX-21-05)
        // 종료는 이미 취소됐으므로 자동으로 끄지도, 작업을 이어 가지도 않는다
        Func<Task> again = async () => { if (await StopForQuitAsync()) ShowQuitPaused(afterRecheck: true); };
        if (!await StopCurrentAsync(again) || !await StopLingeringAsync(_tasks.Select(t => t.Id).ToList(), again)) return false;
        // 멈춘 작업은 "다시 확인 필요"로 — 진행 중으로 남으면 종료가 프로그램을 닫지 않는다(PrepareBusy)
        ShowQuitPaused(afterRecheck: false);
        return true;
    }

    /// <summary>종료하려고 멈춘 작업: "다시 확인 필요" + "다시 시작" 버튼 (종료를 취소하고 돌아왔을 때 이어 갈 수 있게)</summary>
    private void ShowQuitPaused(bool afterRecheck)
    {
        lock (_gate)
        {
            if (_cur is not { } c) return;
            var status = _rows[c.Task.Id].Status;
            var stopped = !c.Finished && (status is PrepTaskStatus.Running or PrepTaskStatus.Waiting || afterRecheck && status is PrepTaskStatus.Failed);
            if (!stopped && !afterRecheck) return;
            if (stopped) _rows[c.Task.Id] = (PrepTaskStatus.NeedsRecheck, null);
            c.Ask = null;
            c.Status = new StatusLine($"{Astro.Core.Product.Reul} 끄려고 작업을 멈췄습니다", Tone.Warn);
            c.Actions = [new("restart", "다시 시작", true)];
            _ready = false;
            Changed();
        }
    }

    /// <summary>
    /// 다른 묶음이 장비를 쓰기 전에 이 묶음을 멈춰 둔다 (PrepareFlow): 하던 작업·계속 도는 장비(가이딩)를 멈추고 확인한 뒤,
    /// ids(영향받는 작업)를 "다시 확인 필요"로 바꾸고 결과를 지운다. handoff = 화면이 갈 묶음. 같은 열쇠로 Start하면 이어서 한다.
    /// 시작하지 않았으면 할 일 없음. 정지를 확인하지 못하면 false.
    /// </summary>
    public async Task<bool> SuspendAsync(IReadOnlyList<string> ids, string text, string? handoff = null)
    {
        lock (_gate) if (_ctx is null) return true;
        Func<Task> again = async () => await SuspendAsync(ids, text, handoff);
        if (!await StopCurrentAsync(again)) return false;
        if (!await StopLingeringAsync(_tasks.Select(t => t.Id).ToList(), again)) return false;
        lock (_gate)
        {
            foreach (var id in ids)
            {
                if (!_rows.TryGetValue(id, out var row) || row.Status is PrepTaskStatus.Skipped or PrepTaskStatus.Pending) continue;
                _rows[id] = (PrepTaskStatus.NeedsRecheck, null);
                if (ResultType(id) is { } type) _ctx!.Results.Remove(type);
            }
            if (_cur is { } c)
            {
                if (!c.Finished && _rows[c.Task.Id].Status is PrepTaskStatus.Running or PrepTaskStatus.Waiting or PrepTaskStatus.Failed)
                    _rows[c.Task.Id] = (PrepTaskStatus.NeedsRecheck, null);
                c.Ask = null;
                c.Status = new StatusLine(text, Tone.Warn);
                c.Actions = [];
            }
            _suspended = true;
            _handoff = handoff;
            _ready = false;
            Changed();
        }
        return true;
    }

    private void MarkAborted()
    {
        lock (_gate)
        {
            if (_ctx is null || _cur is not { } c) return;
            if (!c.Finished) _rows[c.Task.Id] = (PrepTaskStatus.Pending, null);
            c.Ask = null;
            c.Status = new StatusLine("준비를 멈췄습니다", Tone.Warn);
            c.Actions = [new("restart", "다시 시작", true)];
            _ready = false;
            Changed();
        }
    }

    /// <summary>
    /// 끝난 뒤에도 장비가 계속 도는 작업(KeepsRunning)을 멈추고 확인한다. 멈춘 작업과 그 뒤 작업은 "다시 확인 필요"로, 결과는 지운다.
    /// 확인하지 못하면 "장비 상태 다시 확인"에서 멈추고(then을 기억) false.
    /// </summary>
    private async Task<bool> StopLingeringAsync(IReadOnlyList<string> ids, Func<Task> then)
    {
        List<IPrepTask> lingering;
        lock (_gate)
        {
            if (_ctx is null) return true;
            lingering = _tasks.Where(t => t.KeepsRunning && ids.Contains(t.Id)
                && _rows.TryGetValue(t.Id, out var r) && r.Status is PrepTaskStatus.Done or PrepTaskStatus.Failed).ToList();
        }
        foreach (var task in lingering)
        {
            if (!await TryStopAsync(task))
            {
                ShowStopUnconfirmed(task, then);
                return false;
            }
            lock (_gate)
            {
                foreach (var id in new[] { task.Id }.Concat(_setup.Dependents.GetValueOrDefault(task.Id, [])))
                {
                    if (!_rows.TryGetValue(id, out var row) || row.Status is PrepTaskStatus.Skipped or PrepTaskStatus.Pending) continue;
                    _rows[id] = (PrepTaskStatus.NeedsRecheck, null);
                    if (ResultType(id) is { } type) _ctx!.Results.Remove(type);
                }
                _ready = false;
                Changed();
            }
        }
        return true;
    }

    /// <summary>예외로 멈춘 작업 "다시 시도": 장비가 멈췄는지 먼저 확인한 뒤 (CX-PREP-CODE-02)</summary>
    private async Task RetryAsync(IPrepTask task)
    {
        if (!await StopCurrentAsync(() => RetryAsync(task))) return;
        RunTask(task);
    }

    // ── [임시] 시험용 건너뛰기 (2026-10-10 사용자 요청, 2026-10-14 모든 작업으로 — 장비 없이도 모든 단계를 넘겨 보며 화면을 본다).
    //    성공 = 더미 성공 결과, 실패 = 그 작업이 실패해 건너뛸 때와 같은 결과를 채우고 다음으로. 지금 작업이 끝났으면 다음 작업을 시작하지 않고 넘긴다(장비가 움직이지 않게).
    //    진행 중이면 평소처럼 멈추고 멈춘 것을 확인한 뒤에만 넘긴다. 다음 작업은 평소처럼 버튼으로 (바로 시작하지 않는다).
    //    지울 때: 이 절 · AppServer의 /prepare/{group}/temp-skip · PrepView.TempSkip · 웹 components/TempSkip ("[임시] 건너뛰기"로 찾으면 됨)

    /// <summary>[임시] 실패가 의미 없는 작업 (성공으로만 넘김): 초점 확인 — 실패해도 확인을 안 한 것과 같다</summary>
    private static readonly string[] TempSkipSuccessOnly = ["focuscheck"];

    /// <summary>[임시] 건너뛰기 대상: 진행 중(또는 실패해 기다리는) 작업, 끝났으면 다음 작업. 없으면 null</summary>
    private IPrepTask? TempSkipTarget()
    {
        if (_ctx is null) return null;
        if (_cur is { Finished: false } c) return c.Task;
        return _tasks.FirstOrDefault(t => _rows.TryGetValue(t.Id, out var r) && r.Status is PrepTaskStatus.Pending or PrepTaskStatus.NeedsRecheck);
    }

    /// <summary>[임시] 건너뛰기 (success = 성공으로, 아니면 실패로). 안 되면 이유</summary>
    public async Task<string?> TempSkipAsync(bool success)
    {
        Current? cur;
        IPrepTask? task;
        lock (_gate)
        {
            cur = _cur;
            task = TempSkipTarget();
        }
        if (task is null) return "지금은 넘길 작업이 없습니다";
        if (!success && TempSkipSuccessOnly.Contains(task.Id)) return $"{Core.Josa.Pick(task.Title, "은", "는")} 성공으로만 넘길 수 있습니다";
        // 정지 확인에 실패했다가 "장비 상태 다시 확인"으로 확인되면 같은 마무리를 한다 (CX-20-09)
        if (cur is { Finished: false } && cur.Task == task
            && !await StopCurrentAsync(() => { FinishTempSkip(task, success); return Task.CompletedTask; })) return "장비가 멈췄는지 확인하지 못했습니다";
        FinishTempSkip(task, success);
        return null;
    }

    /// <summary>[임시] 건너뛰기 마무리: 더미 결과 · 완료(성공)나 건너뜀(실패) 표시 · 다음 작업 버튼</summary>
    private void FinishTempSkip(IPrepTask task, bool success)
    {
        if (_ctx is not { } ctx) return;
        foreach (var result in TempSkipResults(task.Id, success, ctx)) ctx.Results.Set(result);
        log.LogInformation("[임시] 건너뛰기({Outcome}): {Task}", success ? "성공" : "실패", task.Id);
        lock (_gate)
        {
            // 아직 시작하지 않은 작업을 넘기면 그 작업을 끝난 지금 작업으로 보인다
            if (_cur is not { } cur || cur.Task != task) _cur = cur = new Current(task, ++_runId);
            cur.Finished = true;
            cur.Ask = null;
            cur.Status = null;
            cur.Guide = new GuideView(success ? "[임시] 성공으로 넘겼습니다" : "[임시] 실패로 넘겼습니다",
                $"{Core.Josa.Pick(task.Title, "을", "를")} 시험용 {(success ? "성공" : "실패")} 기록으로 채우고 넘깁니다.");
            _rows[task.Id] = success ? (PrepTaskStatus.Done, "[임시] 성공으로 넘김") : (PrepTaskStatus.Skipped, "[임시] 실패로 넘김");
            var next = _tasks.FirstOrDefault(t => _rows.TryGetValue(t.Id, out var r) && r.Status is PrepTaskStatus.Pending or PrepTaskStatus.NeedsRecheck);
            cur.Actions = next is null ? [_setup.Finish] : [new PrepAction("next", next.StartLabel, true), .. next.StartAlternatives];
            Changed();
        }
    }

    /// <summary>
    /// [임시] 더미 결과. 실패는 그 작업이 실패해 사용자가 건너뛸 때 남기는 것과 같게 (캘리브레이션·가이딩은 그날 밤 가이딩 없이).
    /// 장비를 움직일 값(포커서 위치 등)은 넣지 않는다 — 뒤 작업이 가짜 값으로 장비를 옮기지 않게
    /// </summary>
    private static IEnumerable<object> TempSkipResults(string taskId, bool ok, PrepContext ctx)
    {
        var now = ctx.Now();
        var iso = ctx.HasPlan ? ctx.Plan.Iso : 800;
        var evening = Sky.TonightService.EveningOf(now);
        switch (taskId)
        {
            case "polar":
                yield return ok ? new PolarResult(1.5, 2, -1, "충분해요", now) : new PolarResult(null, 0, 0, "건너뜀", now, Skipped: true);
                break;
            case "calibration":
                yield return ok ? new CalibrationResult(false, 1, "[임시]", evening, now) : new CalibrationResult(false, null, "", evening, now, Skipped: true);
                if (!ok) yield return new GuiderSkipped("[임시] 캘리브레이션을 실패로 넘김", now);
                break;
            case "focus": // 손으로 맞춤과 같게 — 위치를 저장하지 않는다
                yield return new FocusResult(true, null, ok ? 2.2 : null, null, now);
                break;
            case "slew": // 실패 = 이동이 안 돼 지금 위치를 목표로 (센터링도 건너뜀)
                yield return new SlewResult(ok ? 0.05 : 0, 45, now, AcceptedHere: !ok);
                break;
            case "center":
                yield return ok ? new CenterResult(0.4, 0, 1, now, 2.3, 150) : new CenterResult(0, null, 0, now, Skipped: true);
                break;
            case "focuscheck":
                yield return new FocusCheckResult(false, 0.2, null, null, null, null, now);
                break;
            case "guiding":
                yield return ok ? new GuidingResult(0.8, 0.5, 0.6, "충분해요", now) : new GuidingResult(0, 0, 0, "건너뜀", now, Skipped: true);
                if (!ok) yield return new GuiderSkipped("[임시] 가이드를 실패로 넘김", now);
                break;
            case "test":
                yield return new TestShotResult(ctx.ExposureSeconds, ok ? 2.3 : null, null, ok ? 0.2 : null, null, now, Skipped: !ok);
                break;
            case "flat":
                yield return ok ? new Tasks.Wrap.FlatResult(false, 30, 1, iso, 50, now) : new Tasks.Wrap.FlatResult(true, 0, 0, iso, 0, now);
                break;
            case "dark":
                yield return ok ? new Tasks.Wrap.DarkResult(false, 30, [new Tasks.Wrap.DarkSet(ctx.ExposureSeconds, iso, 20)], true, now)
                    : new Tasks.Wrap.DarkResult(true, 0, [], false, now);
                break;
            case "pack":
                yield return ok ? new Tasks.Wrap.PackResult(true, true, true, true, false, now) : new Tasks.Wrap.PackResult(false, false, false, false, false, now);
                break;
        }
    }

    /// <summary>
    /// 진행 중 작업을 취소하고 장비 정지를 확인한다 (IMPL-01). 정지 호출은 취소된 토큰과 무관한 별도 토큰으로.
    /// 확인 못 하면 then을 기억해 두고 false — 사용자가 "장비 상태 다시 확인"을 누르면 다시 확인 후 then.
    /// </summary>
    private async Task<bool> StopCurrentAsync(Func<Task> then)
    {
        Current? cur;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cur = _cur;
            cts = _runCts;
        }
        if (cur is null) return true;
        var wasRunning = !_runTask.IsCompleted;
        cts?.Cancel();
        try { await _runTask.WaitAsync(StopTimeout); } catch { /* 취소·시간 초과 — 아래에서 정지 확인 */ }
        if (!wasRunning && cur.Finished) return true; // 끝난 작업은 움직이는 장비가 없다
        // 멈추라고 알린 것과 실제로 끝난 것은 다르다 — 앞 실행이 아직 정리 중이면 새 실행을 시작하지 않고 확인 필요로 (CX-30-03)
        if (await TryStopAsync(cur.Task) && _runTask.IsCompleted) return true;
        ShowStopUnconfirmed(cur.Task, then);
        return false;
    }

    /// <summary>정지를 확인하지 못함: 다음 동작을 막고 "장비 상태 다시 확인"만 (확인되면 then)</summary>
    private void ShowStopUnconfirmed(IPrepTask task, Func<Task> then)
    {
        lock (_gate)
        {
            _stopUnconfirmed = (task, then);
            if (_cur is not { } c) return;
            c.Ask = null;
            c.Status = new StatusLine($"{task.Title}을 멈췄지만 장비가 멈췄는지 확인하지 못했습니다. 장비 상태를 확인해 주세요.", Tone.Fail);
            c.Actions = [new("device-check", "장비 상태 다시 확인", true)];
            _rows[task.Id] = (PrepTaskStatus.Failed, null);
            _ready = false;
            Changed();
        }
    }

    private async Task<bool> TryStopAsync(IPrepTask task)
    {
        using var cts = new CancellationTokenSource(StopTimeout);
        try { return await task.StopAsync(cts.Token); }
        catch (Exception e)
        {
            log.LogWarning(e, "정지 확인 실패: {Task}", task.Id);
            return false;
        }
    }

    private async Task RecheckStopAsync()
    {
        (IPrepTask Task, Func<Task> Then)? pending;
        lock (_gate) pending = _stopUnconfirmed;
        if (pending is not { } p) return;
        if (!await TryStopAsync(p.Task) || !_runTask.IsCompleted)
        {
            // 아직 확인 못 함: 지운 버튼을 다시 놓는다 (CX-30-03 — 받는 즉시 지우므로)
            ShowStopUnconfirmed(p.Task, p.Then);
            return;
        }
        lock (_gate)
        {
            _stopUnconfirmed = null;
            if (_cur is { } c)
            {
                c.Status = new StatusLine("장비가 멈춘 것을 확인했습니다", Tone.Ok);
                c.Actions = [];
                Changed();
            }
        }
        await p.Then();
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

    private sealed class RunHandle(PrepareRunner r, int runId) : ITaskRun
    {
        public PrepContext Context => r._ctx!;
        public int RunId => runId;

        // 작업이 화면에 보이는 것을 기록 파일에도 (2026-10-10 — 모든 단계 로그). 계속 들어오는 측정값(live)은 남기지 않는다
        private string Task => r._cur?.Task.Id ?? "?";

        public void SubStep(int index)
        {
            r.Log.LogInformation("[{Task}] 세부 과정 {Index}", Task, index);
            r.Update(runId, c => c.SubIndex = index);
        }

        public void Guide(string title, string text)
        {
            r.Log.LogInformation("[{Task}] 안내: {Title} — {Text}", Task, title, text);
            r.Update(runId, c => c.Guide = new GuideView(title, text));
        }

        public void Readout(string kind, string big, string caption, Tone tone, IReadOnlyDictionary<string, double>? values = null, bool live = false, bool unverified = false, DateTimeOffset? observedAt = null) =>
            r.Update(runId, c =>
            {
                if (!live) r.Log.LogInformation("[{Task}] 결과 {Kind}: {Big} — {Caption} ({Tone})", Task, kind, big, caption, tone);
                c.Readout = new ReadoutView(kind, big, caption, tone, values ?? new Dictionary<string, double>(), observedAt ?? DateTimeOffset.Now,
                    unverified ? Freshness.Unverified : Freshness.Fresh);
                c.ReadoutLive = live;
            });

        public void ClearReadout() => r.Update(runId, c => { c.Readout = null; c.ReadoutLive = false; });
        public void Status(string? text, Tone tone = Tone.Busy)
        {
            if (text is not null) r.Log.Log(tone is Tone.Fail ? LogLevel.Warning : LogLevel.Information, "[{Task}] 상태({Tone}): {Text}", Task, tone, text);
            r.Update(runId, c => c.Status = text is null ? null : new StatusLine(text, tone));
        }
        public void Live(string kind, string? url = null, object? data = null) => r.Update(runId, c => c.Live = new LiveView(kind, url, data, DateTimeOffset.Now));

        public async Task<string> AskAsync(IReadOnlyList<PrepAction> actions, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!r.Update(runId, c =>
                {
                    c.Ask?.TrySetCanceled();
                    c.Ask = tcs;
                    c.Actions = actions;
                    r._rows[c.Task.Id] = (PrepTaskStatus.Waiting, null);
                }))
                throw new OperationCanceledException();
            // 취소되면(작업이 다른 길로 감) 그 버튼을 화면에서 바로 치운다
            await using var reg = ct.Register(() =>
            {
                if (tcs.TrySetCanceled(ct))
                    r.Update(runId, c => { if (c.Ask == tcs) { c.Ask = null; c.Actions = []; } });
            });
            r.Log.LogInformation("[{Task}] 물음: {Actions}", Task, string.Join(" / ", actions.Select(a => a.Label)));
            var picked = await tcs.Task;
            r.Log.LogInformation("[{Task}] 고름: {Picked}", Task, picked);
            return picked;
        }
    }
}
