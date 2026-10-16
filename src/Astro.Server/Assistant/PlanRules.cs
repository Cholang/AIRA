namespace Astro.Server.Assistant;

/// <summary>
/// 촬영 계획의 기준값 한곳 (OPERATION_PROCEDURE.md "계획"). 계획 화면·AI 도구·이동·촬영 단계가 모두 여기서 읽는다.
/// 숫자를 고칠 때는 이 파일만 고친다.
/// </summary>
public static class PlanRules
{
    /// <summary>기본 시작·끝 고도: 이보다 높을 때 찍는다 (대기 두께·빛 공해)</summary>
    public const double MinAltitude = 30;

    /// <summary>가장 낮은 고도: 시작점을 이보다 낮게 끌 수 없고, 적도의도 이보다 낮은 대상으로는 옮기지 않는다 (2026-10-14 사용자 결정)</summary>
    public const double LowestAltitude = 15;

    /// <summary>
    /// 적도의 이동부터 촬영 시작까지 걸리는 준비 시간(이동·센터링·초점 확인·가이딩·시험 사진). 촬영 시작 = 이동 + 준비 시간.
    /// 준비 시간은 프로필마다 최근 PrepSamples번 기록의 중앙값(Prepare.PrepTimes, 2026-10-16 사용자 결정), 기록이 없으면 이 기본값
    /// </summary>
    public const double DefaultPrepMinutes = 20;
    public static readonly TimeSpan DefaultPrepLead = TimeSpan.FromMinutes(DefaultPrepMinutes);

    /// <summary>준비 시간을 낼 때 보는 최근 기록 수</summary>
    public const int PrepSamples = 5;

    /// <summary>시작점은 끝나기 이만큼 전까지만 옮길 수 있다</summary>
    public static readonly TimeSpan EndMargin = TimeSpan.FromMinutes(10);

    /// <summary>사진 한 장마다 내려받기·저장·디더링으로 쉬는 시간 (2026-10-08 실기 X-T5: 약 38초)</summary>
    public const double FrameOverheadSeconds = 40;

    /// <summary>자오선 반전으로 멈추는 시간</summary>
    public static readonly TimeSpan FlipPause = TimeSpan.FromMinutes(5);

    /// <summary>다 채우는 밤의 끝 시각 오차 (화면에 "±15분 무렵")</summary>
    public static readonly TimeSpan EndUncertainty = TimeSpan.FromMinutes(15);

    /// <summary>총 노출 기본값(분) — 대상과 상관없음 (2026-10-14 사용자 결정: 늘 180분)</summary>
    public const int DefaultTotalMinutes = 180;

    /// <summary>계획 화면 그래프에 함께 그리는 대상 수. 다시 고르면 가장 최근으로, 넘치면 가장 오래 안 본 것이 빠진다</summary>
    public const int MaxPicks = 3;

    /// <summary>화면의 − + 버튼 한 번에 바뀌는 양 (직접 입력은 1 단위)</summary>
    public const int ExposureStepSeconds = 1; // 2026-10-16 사용자: 노출은 1초씩
    public const int TotalStepMinutes = 10;

    public const int MinExposureSeconds = 1;
    public const int MaxExposureSeconds = 1800;
    public const int MinTotalMinutes = 10;
    public const int MaxTotalMinutes = 1200;

    /// <summary>필터 종류 (지금은 고정 세 가지 — 가진 필터를 프로필에 넣는 것은 나중에). 키는 저장·AI 도구에, 이름은 화면에</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Filters =
    [
        ("none", "필터 없음"),
        ("light_pollution", "광해 필터"),
        ("dual_narrowband", "듀얼 내로우밴드"),
    ];

    public const string DefaultFilter = "light_pollution";

    public static string FilterLabel(string key) => Filters.FirstOrDefault(f => f.Key == key).Label ?? Filters[0].Label;

    /// <summary>화면 이름·옛 기록의 이름으로도 키를 찾는다</summary>
    public static string FilterKey(string? keyOrLabel) =>
        Filters.FirstOrDefault(f => f.Key == keyOrLabel || f.Label == keyOrLabel).Key ?? DefaultFilter;

    /// <summary>
    /// 대상과 상관없는 기본 노출·ISO (단순 표 — 2026-10-14: 추천 태그 대신 기본값. 기록이 쌓이면 "필터 + 지난번 잘 찍힌 값"으로).
    /// 밝은 광학계일수록 짧게, 좁은 대역 필터는 길게(빛이 적음), 광해 필터는 조금 길게(빛을 20~30% 줄임 — 어림값),
    /// 가이딩이 없으면 120초까지, 달이 밝으면 짧게.
    /// </summary>
    public static (int ExposureSeconds, int Iso, IReadOnlyList<string> Notes) DefaultExposure(string filter, double focalRatio, bool guiding, bool brightMoonUp)
    {
        var f = focalRatio > 0 ? focalRatio : 5;
        var exposure = f <= 4.5 ? 180 : f <= 6 ? 240 : 300;
        var iso = 800;
        var notes = new List<string>();
        if (filter == "dual_narrowband") { exposure = Math.Min(exposure * 2, 600); iso = 1600; notes.Add("좁은 대역 필터는 빛이 적어 길게"); }
        else if (filter == "light_pollution") exposure = (int)(exposure * 1.3 / 30) * 30;
        if (!guiding && exposure > 120) { exposure = 120; notes.Add("가이드가 없어 짧게"); }
        if (filter != "dual_narrowband" && brightMoonUp)
        {
            exposure = Math.Max(60, (int)(exposure * 0.67 / 30) * 30);
            notes.Add("달이 밝아 배경이 빨리 밝아지므로 짧게");
        }
        return (exposure, iso, notes);
    }

    /// <summary>
    /// 카메라가 고를 수 있는 ISO 단계 (미러리스·DSLR — 1/3 스톱). 범위를 알면 그 안만.
    /// 전용 천체 카메라(게인 0~수백)는 목록이 아니라 범위 안 정수로 고른다 (IsStepIso가 false)
    /// </summary>
    public static readonly int[] IsoStops = [64, 80, 100, 125, 160, 200, 250, 320, 400, 500, 640, 800, 1000, 1250, 1600, 2000, 2500, 3200, 4000, 5000, 6400, 8000, 10000, 12800, 16000, 20000, 25600, 51200];

    /// <summary>ISO 단계로 고르는 카메라인지: 범위를 모르거나(연결 전·모의) 최솟값이 50 이상이면 ISO 카메라로 본다</summary>
    public static bool IsStepIso(int? min, int? max) => min is null || max is null || min >= 50;

    public static IReadOnlyList<int> IsoOptions(int? min, int? max)
    {
        var lo = min ?? 100;
        var hi = max ?? 12800;
        var list = IsoStops.Where(i => i >= lo && i <= hi).ToList();
        return list.Count > 0 ? list : [800];
    }
}
