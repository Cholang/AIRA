using Astro.Core;
using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.Center;

/// <summary>⑤ 센터링 장비. 실제: N.I.N.A. slew(center=true, RA는 도 단위) — 사진 → ASTAP 솔빙 → Sync → 보정 반복 (N.I.N.A. 솔빙 설정 그대로)</summary>
public interface ICenterDevices
{
    /// <summary>한 회차: 사진 → 위치 계산 → 적도의 보정. blind = 하늘 전체에서 찾기. 남은 오차(′)와 카메라 방향(°)</summary>
    Task<CenterAttempt> AttemptAsync(double raDeg, double decDeg, double exposureSeconds, bool blind, CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<CenterEndState> ReadEndStateAsync(CancellationToken ct);
}

/// <summary>한 회차 결과. Hfr·Stars = 그 사진의 별 크기·별 수 (못 재면 null — 초점 확인이 읽음)</summary>
/// <summary>StopUnconfirmed = 맞춤 이동이 끝나지 않아 멈췄지만 멈춘 것을 확인하지 못함 — 다시 찍지 않고 확인부터 (2026-10-14)</summary>
public sealed record CenterAttempt(bool Solved, double ErrorArcmin, double? CameraAngleDeg, double? Hfr = null, int? Stars = null, bool StopUnconfirmed = false);
public sealed record CenterEndState(double ErrorArcmin, bool MountMoving, bool Tracking, bool CameraExposing);

/// <summary>
/// ⑤ 센터링 (OPERATION_PROCEDURE.md ⑤): 사진·솔빙·보정 반복, 목표 1′ 안, 최대 10번. 카메라 방향은 돌리지 않고 기록만
/// (구도를 바꾸려면 사용자가 사진 위의 틀을 돌리고 카메라를 직접 돌린 뒤 다시 센터링 — Framing).
/// 솔빙 실패 → 노출 늘려 다시 → 하늘 전체 검색 → "초점이나 구름" 안내 + 초점 다시 맞추기(초점 확인에 넘김).
/// 맞추면 그 사진으로 구도를 보여 주고 "이 대상으로 확정 / 다른 대상"(탐색).
/// </summary>
public sealed class CenterTask(ICenterDevices devices) : IPrepTask
{
    public const double TargetArcmin = 1;
    public const int MaxAttempts = 10;
    private const double Exposure = 2, LongExposure = 4;

    public string Id => "center";
    public string Title => UiText.Get("rail.task.center");
    public string StartLabel => UiText.Get("button.center.start");
    public string? StartPrompt => "사진을 찍어 실제 위치를 계산하고, 대상이 가운데 오도록 적도의를 조금씩 옮길까요?";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("shoot", UiText.Get("rail.step.center.shoot")), new("solve", UiText.Get("rail.step.center.solve")), new("correct", UiText.Get("rail.step.center.correct"))];

    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        // 이동에서 "지금 위치를 목표로"를 골랐으면 가운데 맞추기도 하지 않는다 (2026-10-08 사용자 결정)
        if (ctx.Results.Get<SlewResult>() is { AcceptedHere: true }) return Skip(ctx, "지금 위치를 목표로 해서 가운데 맞추기를 하지 않아요.");
        run.Guide(UiText.Get("rail.step.center.shoot"), "사진을 찍어 실제 위치를 계산하고, 적도의를 조금씩 옮깁니다. 카메라 방향은 그대로 둡니다.");
        run.Live("solved-photo", "/api/prepare/live/photo");
        while (true)
        {
            var exposure = Exposure;
            var blind = false;
            CenterAttempt? last = null;
            for (var i = 1; i <= MaxAttempts; i++)
            {
                run.SubStep(0);
                run.Status($"사진을 찍는 중입니다 · {i}회차 · 노출 {exposure:0.#}초 · 목표 1′ 안 · 최대 {MaxAttempts}번");
                run.SubStep(1);
                CenterAttempt a;
                await using (await ctx.Mount.AcquireAsync(ct))
                    a = await devices.AttemptAsync(ctx.Plan.RaDegrees, ctx.Plan.DecDegrees, exposure, blind, ct);
                if (a.StopUnconfirmed)
                {
                    var still = false;
                    while (!still)
                    {
                        run.Status("적도의가 멈췄는지 확인하지 못했습니다", Tone.Fail);
                        await run.AskAsync("적도의를 직접 본 뒤 장비 상태를 다시 읽어 볼까요?", [new("recheck-stop", ButtonText.Recheck, true)], ct);
                        await using (await ctx.Mount.AcquireAsync(ct))
                            still = await devices.StopAsync(ct);
                    }
                    run.Status("적도의를 멈췄습니다", Tone.Warn);
                    await run.AskAsync("다시 찍어 가운데로 맞출까요? 적도의가 조금씩 움직여요.", [new("retry", ButtonText.Retry, true)], ct);
                    continue;
                }
                if (!a.Solved)
                {
                    if (exposure < LongExposure) { exposure = LongExposure; run.Status("위치를 계산하지 못해 노출을 4초로 늘려 다시 찍습니다", Tone.Warn); i--; continue; }
                    if (!blind) { blind = true; run.Status("다시 실패 · 하늘 전체에서 찾는 중입니다 (전체 하늘 검색)", Tone.Warn); i--; continue; }
                    run.Guide(UiText.Get("title.center.solveFailed"), "노출을 늘리고 하늘 전체에서 찾아도 별 배치를 찾지 못했습니다. 초점이 나갔거나 구름이 지나가는 중일 수 있습니다. 초점이나 구름을 확인해 주세요.");
                    run.Status("센터링에 실패했습니다", Tone.Fail);
                    var c = await run.AskAsync("초점이나 구름을 확인한 뒤 센터링을 다시 할까요?\n초점 다시 맞추기를 누르면 자동초점부터 다시 하고, 건너뛰기를 누르면 가운데 맞추지 않고 넘어가요.",
                        [new("retry", ButtonText.Retry, true), new("refocus", UiText.Get("button.refocus")), new("skip", ButtonText.Skip)], ct);
                    if (c == "skip") return Skip(ctx, "대상이 가운데에서 벗어나 있을 수 있어요. 시험 사진에서 구도를 확인해 주세요.");
                    if (c == "refocus")
                    {
                        // 초점 확인이 비교 없이 바로 다시 맞춘다 (장비 준비의 초점은 그대로)
                        ctx.RefocusRequested = true;
                        return new RedoRequest("focuscheck");
                    }
                    goto NextRound;
                }
                run.SubStep(2);
                last = a;
                run.Readout("center-error", $"{a.ErrorArcmin:F1}′", "대상과 화면 가운데의 거리 · 목표 1′ 안", a.ErrorArcmin <= TargetArcmin ? Tone.Ok : Tone.Busy,
                    new Dictionary<string, double> { ["errorArcmin"] = a.ErrorArcmin, ["attempt"] = i });
                if (a.ErrorArcmin <= TargetArcmin)
                {
                    if (a.CameraAngleDeg is { } angle) ctx.Memory.SetCameraAngle(ctx.Plan.TargetId, angle);
                    var angleText = a.CameraAngleDeg is { } ang ? $" · 카메라 방향 {ang:F0}°" : "";
                    var (title, text, frame) = FramingCheck(ctx, a.CameraAngleDeg);
                    var values = new Dictionary<string, double> { ["errorArcmin"] = a.ErrorArcmin, ["attempt"] = i };
                    // 구도 회전 (화면이 사진 위에 틀을 그리고 돌리게 한다): 카메라 방향을 알 때만 — 돌린 뒤 맞았는지 재야 하므로
                    if (a.CameraAngleDeg is { } now)
                    {
                        values["cameraAngle"] = now;
                        values["frameDeg"] = frame;
                        values["frameTolerance"] = Framing.ToleranceDeg;
                    }
                    run.Readout("center-error", $"{a.ErrorArcmin:F1}′", $"대상과 화면 가운데의 거리 · 목표 1′ 안{angleText}", Tone.Ok, values);
                    run.Status(null);
                    // 탐색: 이 사진(센터링 사진)으로 구도를 보고 이 대상으로 확정하거나 다른 대상으로 (OPERATION_PROCEDURE.md "장비 준비 · 대상")
                    return new Completed(new CenterResult(a.ErrorArcmin, a.CameraAngleDeg, i, ctx.Now(), a.Hfr, a.Stars), $"오차 {a.ErrorArcmin:F1}′{angleText}",
                        title, text, [new("flow:replan", UiText.Get("button.otherTarget"))]) { ExtraPrompt = "다른 대상을 누르면 계획 화면으로 돌아가 새 대상을 골라요." };
                }
                run.Status("적도의를 조금 옮기는 중입니다");
            }
            run.Guide(UiText.Get("title.center.notCentered"), $"{MaxAttempts}번 보정해도 목표(1′) 안에 들어오지 않았습니다. 적도의가 바람에 흔들리거나 케이블이 당기고 있을 수 있습니다.");
            run.Status($"마지막 오차 {last?.ErrorArcmin:F1}′", Tone.Fail);
            if (await run.AskAsync("케이블·바람을 확인한 뒤 센터링을 다시 할까요?\n건너뛰기를 누르면 지금 위치 그대로 넘어가요.", [new("retry", ButtonText.Retry, true), new("skip", ButtonText.Skip)], ct) == "skip")
                return Skip(ctx, $"대상이 가운데에서 {last?.ErrorArcmin:F1}′쯤 벗어나 있어요. 시험 사진에서 구도를 확인해 주세요.");
        NextRound:;
        }
    }

    /// <summary>
    /// 구도 회전 (2026-10-16 사용자 결정): 정해 둔 구도(FramingGoal)가 있으면 이 사진의 카메라 방향으로 남은 각도를 재고 안내 문장을 고른다.
    /// frame = 이 사진 위에 그릴 틀의 각도 (남은 회전, 없거나 맞췄으면 0). 남았으면 이 사진 기준으로 다시 적어 둔다.
    /// 안내 문장에는 숫자를 두지 않는다 (DESIGN.md "하늘 화면") — 돌릴 방향·각도는 화면이 사진 가운데에
    /// </summary>
    private static (string Title, string Text, double Frame) FramingCheck(PrepContext ctx, double? angle)
    {
        const string confirm = "구도가 마음에 들면 확정하세요. 다른 대상을 고르면 계획으로 돌아가고, 장비 준비는 그대로 씁니다.";
        if (ctx.Results.Get<FramingGoal>() is { } old && old.PlanKey != FramingGoal.KeyOf(ctx))
            ctx.Results.Remove(typeof(FramingGoal)); // 다른 계획에서 정한 구도 (CX-36-07)
        if (ctx.Results.Get<FramingGoal>() is not { } goal)
            return ("이 대상으로 확정할까요?",
                "가운데에 맞춘 사진이에요. " + (angle is null ? "" : "사진 모서리를 끌어 틀을 돌리면 그 구도로 카메라를 돌리는 방법을 알려 드려요. ") + confirm, 0);
        if (angle is not { } now)
        {
            ctx.Results.Remove(typeof(FramingGoal));
            return ("카메라 방향을 읽지 못했어요", "이번 사진에서 카메라 방향을 계산하지 못해 구도를 확인할 수 없어요. 사진을 보고 " + confirm, 0);
        }
        var left = Framing.Remaining(goal, now);
        if (Math.Abs(left) <= Framing.ToleranceDeg)
        {
            ctx.Results.Remove(typeof(FramingGoal));
            return ("구도를 맞췄어요", "원하던 구도대로 카메라가 돌아갔어요. " + confirm, 0);
        }
        var worse = Math.Abs(left) > Math.Abs(goal.TurnDeg) + Framing.ToleranceDeg;
        ctx.Results.Set(goal with { BaseAngleDeg = now, TurnDeg = left });
        return ("조금 더 돌려 주세요",
            (worse ? "돌린 뒤 원하던 구도에서 더 멀어졌어요. 사진 가운데 안내대로 반대쪽으로 돌려 주세요. " : "사진 가운데 안내대로 조금 더 돌린 뒤 다시 찍어 확인하세요. ")
            + "지금 구도로 찍으려면 확정하세요.", left);
    }

    /// <summary>센터링 건너뛰기 (2026-10-08 사용자 결정)</summary>
    private static Completed Skip(PrepContext ctx, string why) =>
        // 탐색의 "이 대상으로 확정 / 다른 대상"은 그대로 묻는다
        new(new CenterResult(0, null, 0, ctx.Now(), Skipped: true), UiText.Get("rail.result.skipped"), UiText.Get("title.center.skipped"),
            why, [new("flow:replan", UiText.Get("button.otherTarget"))]) { ExtraPrompt = "다른 대상을 누르면 계획 화면으로 돌아가 새 대상을 골라요." };

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (s.MountMoving) return EndStateCheck.Fail("적도의가 아직 움직이고 있습니다");
        if (!s.Tracking) return EndStateCheck.Fail("적도의 추적이 꺼져 있습니다");
        if (s.CameraExposing) return EndStateCheck.Fail("카메라가 아직 노출 중입니다");
        if (ctx.Results.Get<CenterResult>() is not { Skipped: true } && s.ErrorArcmin > TargetArcmin) return EndStateCheck.Fail($"대상이 가운데에서 {s.ErrorArcmin:F1}′ 벗어나 있습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}
