using Astro.Server.Assistant;

namespace Astro.Server.Camera;

/// <summary>센서 색 무늬. Unknown이면 어느 컬러 센서에도 맞는 쪽(X-Trans와 같은 평균)으로</summary>
public enum SensorPattern { Unknown, Mono, Bayer, XTrans }

/// <summary>
/// 카메라 내부 프로필 (2026-10-17 사용자 결정): 카메라마다 다른 특성을 한 묶음으로. 사용자가 보거나 고르지 않는다 —
/// 아이라가 카메라 이름(N.I.N.A. 프로필·카메라 정보·사진 머리글 INSTRUME)으로 알아서 고른다(<see cref="CameraCatalog.Match"/>).
/// 실제로 써 보며 알게 된 값(장 사이 쉬는 시간)은 카메라마다 따로 기억한다(<see cref="CameraProfiles"/>).
/// </summary>
public sealed record CameraProfile(
    string Id,
    /// <summary>기록에 남길 이름</summary>
    string Name,
    /// <summary>원본 최댓값(포화). null = 모름 — 사진 값·N.I.N.A. 비트 수로 정한다</summary>
    int? RawWhite,
    /// <summary>바닥값(빛이 없을 때의 값, 블랙 레벨). 원본 눈금일 때만 쓴다. 0 = 모름</summary>
    int BlackLevel,
    /// <summary>원본 최댓값 위의 값이 조금 있으면 별 중심이 넘친 자리로 보고 포화로 센다 (눈금을 올리지 않음)</summary>
    bool OverRangeIsSaturation,
    /// <summary>노출을 소프트웨어로 중단할 수 있는가. false면 멈추기·종료 때 지금 노출이 끝나기를 기다린다</summary>
    bool CanAbortExposure,
    /// <summary>장 사이 쉬는 시간(초) 기본값 — 내려받기·저장·디더링. 배운 값이 생기면 그것을 쓴다</summary>
    double FrameOverheadSeconds,
    SensorPattern Pattern)
{
    /// <summary>색 무늬를 지우는 평균 칸의 한 변 (9칸 확대 보기): X-Trans·모름 6(무늬 한 주기 — 베이어 2도 나눠떨어짐), 베이어 2, 모노 1</summary>
    public int ColorBlock => Pattern switch { SensorPattern.Mono => 1, SensorPattern.Bayer => 2, _ => 6 };

    /// <summary>별 모양을 잴 때 평균 반지름 (3×3이면 1): 컬러 센서 1, 모노 0</summary>
    public int ShapeSmoothing => Pattern == SensorPattern.Mono ? 0 : 1;
}

/// <summary>
/// 아이라가 아는 카메라. 새 카메라를 들이면 여기에 한 줄 더하고, 그 카메라의 값은 실제 사진으로 확인해 적는다.
/// 모르는 카메라는 <see cref="Generic"/> — 지금까지 후지가 아닌 카메라에 쓰던 값 그대로
/// </summary>
public static class CameraCatalog
{
    /// <summary>
    /// 후지필름 X 시리즈 (X-T5 — 2026-10-15·17 원본 FITS로 확인): 14비트 원본(0~16383), 바닥 1024, 별 중심이 14비트 위로 넘친 값이 조금 섞임,
    /// N.I.N.A. 후지 플러그인은 셔터 속도를 정한 일반 노출을 중단하지 못함(2026-10-11 실기), 장 사이 6~10초(N.I.N.A. 연속 촬영 사진 시각),
    /// X-Trans 색 무늬(머리글의 BAYERPAT는 RGGB로 잘못 적힘)
    /// </summary>
    public static readonly CameraProfile FujiX = new("fuji-x", "후지필름 X 시리즈", 16383, 1024, true, false, 10, SensorPattern.XTrans);

    /// <summary>후지필름 GFX — 후지 플러그인이라 노출 중단 불가는 같음. 눈금·바닥은 X 시리즈와 같다고 본다 [확인 필요]</summary>
    public static readonly CameraProfile FujiGfx = new("fuji-gfx", "후지필름 GFX", 16383, 1024, true, false, 10, SensorPattern.Bayer);

    /// <summary>모르는 카메라: 눈금은 사진·N.I.N.A. 비트 수로, 바닥 0, 노출 중단 가능, 쉬는 시간은 계획 기본값</summary>
    public static readonly CameraProfile Generic = new("generic", "그 밖의 카메라", null, 0, false, true, PlanRules.FrameOverheadSeconds, SensorPattern.Unknown);

    public static IReadOnlyList<CameraProfile> All { get; } = [FujiX, FujiGfx, Generic];

    /// <summary>
    /// 카메라 이름(그리고 드라이버 설명)으로 프로필을 고른다. 이름이 "X-"로 시작하거나 후지 드라이버면 X 시리즈, "GFX"면 GFX, 아니면 Generic.
    /// (예전 Fits.IsFuji·NinaRig.IsFujifilm과 같은 기준을 한곳으로)
    /// </summary>
    public static CameraProfile Match(string? name, string? driver = null)
    {
        var n = (name ?? "").Trim();
        var all = n + " " + (driver ?? "");
        if (n.StartsWith("GFX", StringComparison.OrdinalIgnoreCase)) return FujiGfx;
        if (n.StartsWith("X-", StringComparison.OrdinalIgnoreCase)) return FujiX;
        if (all.Contains("fuji", StringComparison.OrdinalIgnoreCase))
            return all.Contains("GFX", StringComparison.OrdinalIgnoreCase) ? FujiGfx : FujiX;
        return Generic;
    }
}
