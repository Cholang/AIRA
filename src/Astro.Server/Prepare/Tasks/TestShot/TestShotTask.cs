using Astro.Core;
using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.TestShot;

/// <summary>⑦ 시험 사진 장비. 실제: N.I.N.A. 촬영(주 카메라 X-T5) · 이미지 통계, 파일은 같은 밤 폴더의 "시험"</summary>
public interface ITestShotDevices
{
    /// <summary>계획 노출로 한 장. 남은 초를 알린다</summary>
    Task<bool> ExposeAsync(int exposureSeconds, int iso, Action<int> remaining, CancellationToken ct);
    /// <summary>
    /// since(이번 노출을 시작한 시각) 뒤에 저장된 사진만 내려받고 검사한다. 못 받거나 그보다 오래된 사진뿐이면 null
    /// — 노출이 실패했는데 이전 사진을 이번 시험 사진으로 받아들이지 않게 (CX-PREP-CODE-05)
    /// </summary>
    Task<ShotStats?> DownloadAndAnalyzeAsync(DateTimeOffset since, CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<TestShotEndState> ReadEndStateAsync(CancellationToken ct);
}

/// <summary>
/// 사진 검사값. Background = 배경 밝기(0~1), Eccentricity = 별 길쭉함(가운데 칸 이심률, 0 둥긂 ~ 1), SaturatedPercent = 포화 화소 %,
/// Histogram = 0~최대 밝기를 나눈 칸마다 픽셀 수, Field = 아홉 칸 별 크기·길쭉함 (못 재면 null),
/// Tiles = 9칸 확대 보기를 쓸 수 있음 (실장비 = 저장된 사진을 잘라 하늘 화면 이미지 test-tile-0~8로, 모의 = 그림) — 없으면 화면이 버튼을 숨기고 안내도 말하지 않는다
/// </summary>
public sealed record ShotStats(double Hfr, double? Eccentricity, double Background, double SaturatedPercent, double GuidingRms, string FilePath, int[]? Histogram = null, StarField? Field = null, bool Tiles = false);
public sealed record TestShotEndState(bool CameraExposing, bool Guiding, bool FileSaved);

/// <summary>
/// ⑦ 시험 사진 (OPERATION_PROCEDURE.md ⑦): 계획 노출로 한 장 → 노출 맞추기(배경이 알맞지 않으면 묻지 않고 노출을 바꿔 한 장 더, 많아야 3장 — <see cref="ExposureMatch"/>)
/// → 마지막 사진을 규칙으로 바로 검사 → "좋아요" 또는 문제 + 할 일. 첫 장으로 쓰지 않는다. 끝 버튼은 러너의 "촬영 시작".
/// </summary>
public sealed class TestShotTask(ITestShotDevices devices) : IPrepTask
{
    public string Id => "test";
    public string Title => UiText.Get("rail.task.testShot");
    public string StartLabel => UiText.Get("button.testShot.start");
    public string? StartPrompt => "계획한 노출 그대로 한 장 찍어 초점·추적·구도를 확인할까요? 이 사진은 촬영 사진에 넣지 않아요.";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("expose", UiText.Get("rail.step.testShot.expose")), new("download", UiText.Get("rail.step.testShot.download")), new("check", UiText.Get("rail.step.testShot.check")), new("result", UiText.Get("rail.step.testShot.result"))];

    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        var shots = 0;
        string? matched = null; // 노출 맞추기로 바꾼 내용 (끝 안내에)
        while (true)
        {
            run.SubStep(0);
            if (matched is null)
                run.Guide(UiText.Get("rail.step.testShot.expose"), "계획한 노출 그대로 한 장 찍어 초점·추적·구도를 확인합니다. 이 사진은 촬영 사진에 넣지 않습니다.");
            run.Live("none");
            run.Status($"노출 중입니다 · {ctx.ExposureSeconds}초 · ISO {ctx.Plan.Iso}");
            var since = ctx.Now();
            var exposed = await devices.ExposeAsync(ctx.ExposureSeconds, ctx.Plan.Iso, s =>
                run.Readout("countdown", $"{s}초", "남은 노출", Tone.Busy, new Dictionary<string, double> { ["remaining"] = s }, live: true), ct);
            if (!exposed)
            {
                // 노출이 실패하면 내려받기·검사로 가지 않는다 (CX-PREP-CODE-05)
                run.ClearReadout();
                run.Guide(UiText.Get("title.testShot.failed"), "카메라가 노출하지 못했습니다. 카메라 전원·USB 연결과 카메라의 PC 연결 방식(테더링)을 확인해 주세요.");
                run.Status("카메라가 노출하지 못했습니다", Tone.Fail);
                if (await run.AskAsync("시험 사진을 다시 찍을까요?\n건너뛰기를 누르면 시험 사진 없이 촬영으로 가요.", [new("retry", ButtonText.Retry, true), SkipAction], ct) == "skip") return Skipped(ctx);
                continue;
            }

            run.SubStep(1);
            run.Guide(UiText.Get("rail.step.testShot.download"), "카메라에서 사진을 내려받습니다.");
            run.Status("사진을 내려받는 중입니다");
            var stats = await devices.DownloadAndAnalyzeAsync(since, ct);
            if (stats is null)
            {
                run.Status("사진을 내려받지 못해 한 번 더 받아 오는 중입니다", Tone.Warn);
                stats = await devices.DownloadAndAnalyzeAsync(since, ct);
            }
            if (stats is null)
            {
                run.Guide(UiText.Get("title.testShot.noPhoto"), "두 번 시도했지만 카메라에서 사진을 내려받지 못했습니다. 카메라 USB 연결과 전원, 카메라 화질 설정이 RAW인지 확인해 주세요 (JPEG면 사진을 쓸 수 없습니다).");
                run.Status("시험 사진을 찍지 못했습니다", Tone.Fail);
                if (await run.AskAsync("시험 사진을 다시 찍을까요?\n건너뛰기를 누르면 시험 사진 없이 촬영으로 가요.", [new("retry", ButtonText.Retry, true), SkipAction], ct) == "skip") return Skipped(ctx);
                continue;
            }

            run.SubStep(2);
            // 사진 위 오른쪽에 히스토그램 (2026-10-09 사용자 요청 — 배경 산의 위치로 노출이 맞는지 본다)
            run.Live("test-photo", $"/api/prepare/live/test?f={Uri.EscapeDataString(stats.FilePath)}",
                stats.Histogram is { } hist ? new { histogram = hist, background = stats.Background, tiles = stats.Tiles } : stats.Tiles ? new { tiles = true } : null);
            // 대상 단계에서 초점을 다시 맞췄으면 그 값과 비교한다
            var focusHfr = ctx.Results.Get<FocusCheckResult>() is { Refocused: true, Hfr: { } rh } ? rh : ctx.Results.Get<FocusResult>()?.Hfr;
            var verdict = TestShotRules.Judge(stats, focusHfr);
            var values = new Dictionary<string, double>
            {
                ["hfr"] = stats.Hfr, ["background"] = stats.Background,
                ["saturatedPercent"] = stats.SaturatedPercent, ["guidingRms"] = stats.GuidingRms,
            };
            if (stats.Eccentricity is { } ecc) values["eccentricity"] = ecc;
            // 9칸 확대 보기가 칸 위에 얹는 숫자 (칸 = 화면 방향 왼쪽 위부터 0~8)
            if (stats.Field is { } field)
                for (var k = 0; k < field.Tiles.Count; k++)
                {
                    values[$"tile{k}Stars"] = field.Tiles[k].Stars;
                    if (field.Tiles[k].Hfd is { } th) values[$"tile{k}Hfd"] = th;
                    if (field.Tiles[k].Eccentricity is { } te) values[$"tile{k}Ecc"] = te;
                }
            if (focusHfr is { } fh) values["focusHfr"] = fh;
            run.Readout("test-shot", $"HFR {stats.Hfr:F1}", verdict.Caption, verdict.Tone, values);

            // 노출 맞추기: 배경이 알맞지 않으면 묻지 않고 바꿔 한 장 더 (적도의가 움직이지 않는 일 — 2026-10-15 합의). 많아야 3장
            if (++shots < ExposureMatch.MaxShots && ExposureMatch.Next(stats.Background, stats.SaturatedPercent, ctx.ExposureSeconds, ctx.HasGuider) is { } next)
            {
                var why = stats.SaturatedPercent > ExposureMatch.SaturatedLimit && next < ctx.ExposureSeconds ? "밝은 별이 많이 포화돼"
                    : stats.Background > ExposureMatch.High ? "하늘 배경이 밝아" : "하늘 배경이 어두워";
                matched = $"{why} 노출을 {ctx.ExposureSeconds}초에서 {next}초로 바꿨어요";
                run.Guide(UiText.Get("title.testShot.matchingExposure"), $"{matched}. 확인용으로 한 장 더 찍습니다. 계획의 노출과 장수도 바뀌어요.");
                run.Status($"배경 {stats.Background * 100:F0}% · 목표 {ExposureMatch.Target * 100:F0}% 안팎 · 노출 {next}초로", Tone.Busy);
                ctx.ChangeExposure(next);
                continue;
            }

            run.SubStep(3);
            if (verdict.Problem is { } problem)
            {
                run.Guide(problem.Title, problem.Text);
                run.Status(problem.Status, Tone.Warn);
                var c = await run.AskAsync($"시험 사진을 다시 찍어 볼까요?\n이대로 진행을 누르면 지금 상태로 촬영하고, {problem.RedoLabel}를 누르면 그 단계부터 다시 해요.",
                    [new("retry", ButtonText.Retry, true), new("keep", ButtonText.Proceed), new(problem.RedoAction, problem.RedoLabel)], ct);
                if (c == "retry") continue;
                if (c == problem.RedoAction)
                {
                    if (problem.RedoAction == "redo:focuscheck") ctx.RefocusRequested = true; // 비교 없이 바로 다시 맞춘다
                    return new RedoRequest(problem.RedoAction[5..]);
                }
            }
            run.Status(null);
            var exposureNote = matched is null ? "" : $" {matched}(계획에 반영).";
            var result = new TestShotResult(ctx.ExposureSeconds, stats.Hfr, stats.Eccentricity, stats.SaturatedPercent, stats.FilePath, ctx.Now());
            // 아홉 칸이 고르지 않으면 알려만 준다 — 조절 순서(가이드 멈춤 → 조절 → 초점·센터링 확인)는 [계획]
            var looks = verdict.Field?.Kind switch
            {
                StarFieldRules.Kind.Tilted => $"초점·배경·가이드는 괜찮습니다. 다만 {verdict.Field.Side} 별이 다른 귀퉁이보다 커서 카메라가 조금 기울었을 수 있어요(틸트). 촬영은 그대로 해도 됩니다.",
                StarFieldRules.Kind.Corners => "초점·배경·가이드는 괜찮습니다. 다만 귀퉁이 별이 가운데보다 고르게 커서 백포커스(플래트너와 카메라 사이 거리)가 맞지 않거나 상이 굽었을 수 있어요. 촬영은 그대로 해도 됩니다.",
                _ => "초점·별 모양·배경·가이드 모두 괜찮습니다.",
            };
            return new Completed(result, $"좋아요 · HFR {stats.Hfr:F1} · {ctx.ExposureSeconds}초", UiText.Get("title.testShot.good"),
                $"{looks}{exposureNote} 끝나는 시각은 {ctx.Plan.End:HH:mm}입니다." + (stats.Tiles ? " 귀퉁이 별까지 보려면 9칸 확대 보기를 누르세요." : ""),
                [new("redo:test", ButtonText.Redo), new("redo:center", UiText.Get("button.recenter"))])
            { ExtraPrompt = "다시 하기를 누르면 시험 사진을 다시 찍고, 센터링 다시 하기를 누르면 구도부터 다시 맞춰요." };
        }
    }

    /// <summary>시험 사진 건너뛰기 (2026-10-08 사용자 결정 — 구름이 몰려오거나 구도를 이미 확인한 경우)</summary>
    private static readonly PrepAction SkipAction = new("skip", ButtonText.Skip);

    private static Completed Skipped(PrepContext ctx) =>
        new(new TestShotResult(ctx.ExposureSeconds, null, null, null, null, ctx.Now(), Skipped: true), UiText.Get("rail.result.skipped"), UiText.Get("title.testShot.skipped"),
            "바로 촬영을 시작할 수 있어요. 첫 사진들의 등급으로 초점·추적을 확인해요.");

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (s.CameraExposing) return EndStateCheck.Fail("카메라가 아직 노출 중입니다");
        if (ctx.HasGuider && !s.Guiding) return EndStateCheck.Fail("가이드가 멈춰 있습니다");
        if (ctx.Results.Get<TestShotResult>() is { Skipped: true }) return EndStateCheck.Pass;
        if (!s.FileSaved) return EndStateCheck.Fail("시험 사진 파일이 저장되지 않았습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}

/// <summary>시험 사진 판정 (순수 함수). 기준은 실기에서 다듬는다</summary>
public static class TestShotRules
{
    public const double HfrGrowthLimit = 1.25;   // 초점 때보다 25% 넘게 커지면 초점이 밀렸다고 본다
    public const double EccentricityLimit = 0.6; // 가운데 별이 이보다 길쭉하고 (긴 축 약 1.25배)
    public const double AlignedLimit = 0.5;      // [임시] 그 방향이 이만큼 한쪽으로 모였으면 가이드·추적 문제 (잡음·광학으로 길쭉한 별은 방향이 제각각)
    public const double SaturatedLimit = 1.0;    // [임시] 포화 화소가 이 %를 넘으면 노출을 줄이자고 한다 (밝은 별 꼭대기는 늘 조금 넘침 — 실제 사진으로 다듬기)

    public sealed record Problem(string Title, string Text, string Status, string RedoAction, string RedoLabel);
    /// <summary>Field = 아홉 칸 판정 (재지 못했으면 null)</summary>
    public sealed record Verdict(string Caption, Tone Tone, Problem? Problem, StarFieldRules.Verdict? Field = null);

    /// <summary>별이 흘렀나: 가운데 별이 길쭉하고 그 방향이 한쪽으로 모임. 방향을 모르면(모의) 길쭉함만으로</summary>
    public static bool Trailed(ShotStats s) => s.Eccentricity > EccentricityLimit && (s.Field?.Alignment ?? 1) >= AlignedLimit;

    public static Verdict Judge(ShotStats s, double? focusHfr)
    {
        var parts = new List<string>();
        var trailed = Trailed(s);
        var field = s.Field is { } sf ? StarFieldRules.Judge(sf) : null;
        if (focusHfr is { } f) parts.Add($"초점 때 {f:F1}");
        if (s.Eccentricity is { } ecc) parts.Add(ecc <= EccentricityLimit ? "별 둥긂" : trailed ? "별 길쭉함" : "별 모양 제각각"); // 재지 못했으면 말하지 않는다
        if (field is not null) parts.Add(field.Caption);
        parts.Add(s.Background < ExposureMatch.Low ? "배경 어두움" : s.Background <= ExposureMatch.High ? "배경 알맞음" : "배경 밝음");
        parts.Add($"포화 {s.SaturatedPercent:F1}%");
        parts.Add($"가이드 {s.GuidingRms:F1}″");
        parts.Add("\"시험\" 폴더에 보관함");
        var caption = string.Join(" · ", parts);

        if (focusHfr is { } fh && s.Hfr > fh * HfrGrowthLimit)
            return new Verdict(caption, Tone.Warn, new(UiText.Get("title.testShot.focusDrift"), $"별 크기가 초점을 맞출 때({fh:F1})보다 커졌습니다({s.Hfr:F1}). 기온이 바뀌었을 수 있어요.", "별 크기 증가", "redo:focuscheck", UiText.Get("button.refocus")), field);
        if (trailed)
            return new Verdict(caption, Tone.Warn, new(UiText.Get("title.testShot.trailed"), "별이 한쪽으로 늘어졌습니다. 가이드나 추적이 흔들렸을 수 있어요.", "별 모양 길쭉함", "redo:guiding", UiText.Get("button.reguide")), field);
        return new Verdict(caption, Tone.Ok, null, field);
    }
}
