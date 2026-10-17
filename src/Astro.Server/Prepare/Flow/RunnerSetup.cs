using Astro.Core;
namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 묶음 하나의 설정: 이름(rig · target), 다시 하면 "다시 확인 필요"가 되는 뒤 작업(OPERATION_PROCEDURE.md "장비 준비 · 대상" 다시 하기), 마지막 작업 뒤의 끝 버튼.
/// 끝 버튼이 "flow:"로 시작하면 화면이 다음 단계로 넘어간다 (장비 준비 끝 → 대상 고르기).
/// </summary>
public sealed record RunnerSetup(string Group, IReadOnlyDictionary<string, string[]> Dependents, PrepAction Finish, string? FinishPrompt = null)
{
    /// <summary>장비 준비 묶음: 극축 정렬 · 캘리브레이션 · 초점 (계획 없이, 그날 밤 유지)</summary>
    public static readonly RunnerSetup Rig = new("rig", new Dictionary<string, string[]>
    {
        ["polar"] = ["calibration", "focus"],
        ["calibration"] = [],
        ["focus"] = [],
    }, new PrepAction("flow:target", UiText.Get("button.toPlan"), true), "장비 준비를 마쳤어요. 오늘 밤 찍을 대상을 고를까요?");

    /// <summary>대상 묶음: 이동 · 센터링 · 초점 확인 · 가이딩 · 시험 사진 (확정 계획 하나)</summary>
    public static readonly RunnerSetup Target = new("target", new Dictionary<string, string[]>
    {
        ["slew"] = ["center", "focuscheck", "guiding", "test"],
        ["center"] = ["focuscheck", "guiding", "test"],
        ["focuscheck"] = ["test"],
        ["guiding"] = ["test"],
        ["test"] = [],
    }, new PrepAction("start", UiText.Get("button.shoot.start"), true), "계획대로 촬영을 시작할까요? 촬영 중에는 적도의가 대상을 따라가요.");

    /// <summary>마무리 묶음: 플랫 · 다크 · 장비 정리 (계획 없이, 그날 밤). 마지막 작업이 끝나면 끝 버튼 없이 요약으로</summary>
    public static readonly RunnerSetup Wrap = new("wrap", new Dictionary<string, string[]>
    {
        ["flat"] = [],
        ["dark"] = [],
        ["pack"] = [],
    }, new PrepAction("flow:summary", UiText.Get("button.toSummary"), true), "오늘 밤 찍은 사진과 보정 프레임을 정리해서 볼까요?");

    /// <summary>한 묶음에 일곱 작업 (재구성 전 순서 — 러너 시험용)</summary>
    public static readonly RunnerSetup Single = new("all", new Dictionary<string, string[]>
    {
        ["polar"] = ["calibration", "slew", "focus", "center", "guiding", "test"],
        ["calibration"] = ["slew", "center", "guiding", "test"],
        ["slew"] = ["center", "guiding", "test"],
        ["focus"] = ["test"],
        ["center"] = ["guiding", "test"],
        ["guiding"] = ["test"],
        ["test"] = [],
    }, new PrepAction("start", UiText.Get("button.shoot.start"), true));
}
