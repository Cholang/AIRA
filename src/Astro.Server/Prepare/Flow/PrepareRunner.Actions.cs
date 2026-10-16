
namespace Astro.Server.Prepare.Flow;

/// <summary>준비 진행자 — 사용자 버튼(누름·다시 하기·다시 시도) (PrepareRunner 나눔, 상태 필드는 PrepareRunner.cs)</summary>
public sealed partial class PrepareRunner
{
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

    /// <summary>예외로 멈춘 작업 "다시 시도": 장비가 멈췄는지 먼저 확인한 뒤 (CX-PREP-CODE-02)</summary>
    private async Task RetryAsync(IPrepTask task)
    {
        if (!await StopCurrentAsync(() => RetryAsync(task))) return;
        RunTask(task);
    }
}
