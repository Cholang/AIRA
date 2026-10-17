using Astro.Core;

namespace Astro.Server.Prepare.Flow;

/// <summary>준비 진행자 — 중단·멈춤·정지 확인 (PrepareRunner 나눔, 상태 필드는 PrepareRunner.cs)</summary>
public sealed partial class PrepareRunner
{
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
            c.Show([new("restart", UiText.Get("button.restart"), true)], $"멈춘 {Core.Josa.Reul(c.Task.Title)} 처음부터 다시 시작할까요?");
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
            c.Show([new("restart", UiText.Get("button.restart"), true)], $"멈춘 {Core.Josa.Reul(c.Task.Title)} 처음부터 다시 시작할까요?");
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
            if (!TakeUserConfirmed(task) && !await TryStopAsync(task))
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
        // 사용자가 방금 이 작업의 장비가 멈춘 것을 직접 확인했다 — 같은 검사를 다시 돌려 확인 화면에 가두지 않는다 (CX-40-05)
        if (!wasRunning && TakeUserConfirmed(cur.Task)) return true;
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
            c.Status = new StatusLine($"{Core.Josa.Reul(task.Title)} 멈췄지만 장비가 멈췄는지 확인하지 못했습니다", Tone.Fail);
            // 적도의 홈의 "적도의 확인 필요"와 같은 방식: 다시 확인하거나, 사용자가 직접 보고 푼다 (2026-10-16 — 적도의가 끊겨 있으면 다시 확인이 끝내 통과하지 않아 갇혔다)
            c.Show([new("device-check", ButtonText.Recheck, true), new("device-confirm", UiText.Get("button.confirmStopped"))],
                "장비 상태를 다시 읽어 멈췄는지 확인할까요?\n적도의·포커서를 직접 보고 멈춰 있으면 멈춘 것을 확인했어요를 눌러 주세요.");
            _rows[task.Id] = (PrepTaskStatus.Failed, null);
            _ready = false;
            Changed();
        }
    }

    /// <summary>사용자가 직접 확인한 정지 — 그 작업의 다음 정지 확인 하나만 통과시킨다 (CX-40-05). 다른 작업·새 실행에는 쓰지 않는다</summary>
    private IPrepTask? _userConfirmed;

    private bool TakeUserConfirmed(IPrepTask task)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_userConfirmed, task)) return false;
            _userConfirmed = null;
            return true;
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

    /// <summary>
    /// 사용자가 장비가 멈춘 것을 직접 확인함: 정지 확인을 풀고 하려던 일을 이어서. 앞 실행이 아직 끝나지 않았으면(취소를 받지 않음) 풀지 않는다 —
    /// 그 실행이 장비를 계속 쓸 수 있다
    /// </summary>
    private async Task ConfirmStopByUserAsync()
    {
        (IPrepTask Task, Func<Task> Then)? pending;
        lock (_gate) pending = _stopUnconfirmed;
        if (pending is not { } p) return;
        if (!_runTask.IsCompleted)
        {
            ShowStopUnconfirmed(p.Task, p.Then);
            lock (_gate)
                if (_cur is { } c) { c.Status = new StatusLine($"{p.Task.Title}이 아직 끝나지 않았습니다. 잠시 뒤 다시 눌러 주세요.", Tone.Fail); Changed(); }
            return;
        }
        log.LogWarning("정지를 사용자가 직접 확인함: {Task}", p.Task.Id);
        lock (_gate)
        {
            _stopUnconfirmed = null;
            _userConfirmed = p.Task; // 이어서 할 일(재시도 등)이 같은 정지 확인을 다시 하면 이것으로 통과 (CX-40-05)
            if (_cur is { } c)
            {
                c.Status = new StatusLine("직접 확인한 대로 이어서 합니다", Tone.Warn);
                c.Actions = [];
                Changed();
            }
        }
        try { await p.Then(); }
        finally { lock (_gate) _userConfirmed = null; } // 한 번만 — 쓰지 않았으면 버린다
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
}
