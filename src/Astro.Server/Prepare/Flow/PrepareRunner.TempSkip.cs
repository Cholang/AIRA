
namespace Astro.Server.Prepare.Flow;

/// <summary>준비 진행자 — [임시] 시험용 건너뛰기 — 지울 때 이 파일째 (PrepareRunner 나눔, 상태 필드는 PrepareRunner.cs)</summary>
public sealed partial class PrepareRunner
{
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
}
