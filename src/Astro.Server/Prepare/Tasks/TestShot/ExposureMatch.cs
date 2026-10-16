namespace Astro.Server.Prepare.Tasks.TestShot;

/// <summary>
/// 노출 맞추기 (OPERATION_PROCEDURE.md ⑦, 2026-10-15 합의): 시험 사진의 하늘 배경 밝기로 그날 밤에 알맞은 한 장 노출을 정한다.
/// 알맞은 노출 = 카메라 읽기 잡음이 하늘 배경 잡음에 묻히는 가장 짧은 노출 — 더 길게 찍어도 얻는 것은 적고 포화·별 흐름 위험만 크다.
/// 카메라별 읽기 잡음을 아직 모르므로 지금은 "바닥값 위 배경이 눈금의 5~10%"(카메라 화면 히스토그램의 1/4~1/3쯤)를 목표로 한다 [임시] —
/// 마무리 다크로 ISO별 읽기 잡음을 재면 그 값으로 바꾼다.
/// 배경은 바닥값을 뺀 중앙값 비율(<see cref="ShotStats.Background"/>)이고, 노출에 비례한다고 본다(하늘 빛이 바닥 위 신호의 대부분).
/// 적도의가 움직이지 않는 일이라 묻지 않고 바꾼 뒤 확인용으로 한 장 더 찍는다 — 많아야 <see cref="MaxShots"/>장.
/// </summary>
public static class ExposureMatch
{
    /// <summary>[임시] 목표 배경 (바닥 위, 눈금의 비율)</summary>
    public const double Target = 0.07;
    /// <summary>[임시] 이 사이면 그대로 둔다</summary>
    public const double Low = 0.05, High = 0.10;
    /// <summary>[임시] 가장 짧은 노출 — 너무 짧으면 장마다 붙는 시간(X-T5 약 10초, PlanRules.FrameOverheadSeconds)에 밤이 많이 간다</summary>
    public const int MinSeconds = 10;
    /// <summary>늘릴 때 상한: 가이드 없으면 60초(가이드 없이 권하는 노출과 같게 — 2026-10-17 사용자), 있으면 [임시] 300초</summary>
    public const int MaxUnguided = Guiding.GuidingTask.UnguidedExposure, MaxGuided = 300;
    /// <summary>시험 사진은 많아야 이 장수 (처음 한 장 + 바꿔서 확인 두 장)</summary>
    public const int MaxShots = 3;
    /// <summary>포화 화소가 이 %를 넘으면 배경과 관계없이 줄인다 (<see cref="TestShotRules.SaturatedLimit"/>와 같음)</summary>
    public const double SaturatedLimit = TestShotRules.SaturatedLimit;

    /// <summary>바꿀 노출. 지금 노출이 알맞거나 더 바꿀 수 없으면(상한·하한) null</summary>
    public static int? Next(double background, double saturatedPercent, int exposure, bool guided)
    {
        var saturated = saturatedPercent > SaturatedLimit;
        if (!saturated && background is >= Low and <= High) return null;
        var max = guided ? MaxGuided : MaxUnguided;
        double want = background > 0.0005 ? exposure * Target / background : double.MaxValue;
        if (saturated) want = Math.Min(want, exposure * 0.75);
        int next;
        if (want > exposure)
        {
            // 늘리는 쪽: 상한까지만. 이미 상한 이상이면(사용자가 길게 정함) 그대로 — 늘리려다 줄이지 않는다
            if (exposure >= max) return null;
            next = Round(Math.Min(want, max));
        }
        else next = Math.Min(exposure, Round(Math.Max(want, MinSeconds))); // 줄이는 쪽: 하한까지만 (이미 하한보다 짧으면 그대로)
        return next == exposure ? null : next;
    }

    /// <summary>1분 아래는 1초 단위, 그 위는 5초 단위</summary>
    private static int Round(double seconds) => seconds < 60 ? (int)Math.Round(seconds) : (int)Math.Round(seconds / 5) * 5;
}
