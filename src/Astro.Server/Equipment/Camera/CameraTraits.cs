using Astro.Server.Assistant;

namespace Astro.Server.Equipment.Camera;

/// <summary>센서 색 무늬. Unknown이면 어느 컬러 센서에도 맞는 쪽(X-Trans와 같은 평균)으로</summary>
public enum SensorPattern { Unknown, Mono, Bayer, XTrans }

/// <summary>
/// 카메라 특성 (장비 › 카메라의 틀, 2026-10-17): 카메라라면 다 갖는 속성 목록. 카탈로그의 한 줄(모델)이 값을 채운다.
/// 사용자는 보거나 고르지 않는다 — 아이라가 카메라 이름(N.I.N.A. 프로필·카메라 정보·사진 머리글 INSTRUME)으로 고른다(<see cref="CameraCatalog.Match"/>).
/// 써 보며 잰 값(장 사이 쉬는 시간)은 배운 값으로 따로(<see cref="CameraLearned"/>)
/// </summary>
public sealed record CameraTraits(
    string Id,
    string Name,
    /// <summary>원본 최댓값(포화). null = 모름 — 사진 값·N.I.N.A. 비트 수로 정한다</summary>
    int? RawWhite,
    /// <summary>바닥값(빛이 없을 때의 값, 블랙 레벨). 원본 눈금일 때만 쓴다. 0 = 모름</summary>
    int BlackLevel,
    /// <summary>원본 최댓값 위의 값이 조금 있으면 별 중심이 넘친 자리로 보고 포화로 센다 (눈금을 올리지 않음)</summary>
    bool OverRangeIsSaturation,
    /// <summary>노출을 소프트웨어로 중단할 수 있는가. false면 멈추기·종료 때 지금 노출이 끝나기를 기다린다</summary>
    bool CanAbortExposure,
    /// <summary>장 사이 쉬는 시간(초) — 내려받기·저장·디더링. 배운 값이 생기면 그것을 쓴다</summary>
    double FrameOverheadSeconds,
    SensorPattern Pattern,
    /// <summary>USB 제조사 번호 (연결 전 확인 — 꽂혀 있는지). null = 확인하지 않음</summary>
    string? UsbVendorId = null) : IEquipmentModel
{
    /// <summary>색 무늬를 지우는 평균 칸의 한 변 (9칸 확대 보기): X-Trans·모름 6(무늬 한 주기 — 베이어 2도 나눠떨어짐), 베이어 2, 모노 1</summary>
    public int ColorBlock => Pattern switch { SensorPattern.Mono => 1, SensorPattern.Bayer => 2, _ => 6 };

    /// <summary>별 모양을 잴 때 평균 반지름 (3×3이면 1): 컬러 센서 1, 모노 0</summary>
    public int ShapeSmoothing => Pattern == SensorPattern.Mono ? 0 : 1;
}

/// <summary>
/// 카메라 카탈로그: 아이라가 아는 카메라 모델. 새 카메라를 들이면 여기에 한 줄 더하고, 그 카메라의 원본 사진·다크로 값을 확인해 적는다.
/// 모르는 카메라는 기본 모델 <see cref="Generic"/> — 지금까지 후지가 아닌 카메라에 쓰던 값 그대로
/// </summary>
public sealed class CameraCatalog : IEquipmentCatalog<CameraTraits>
{
    private CameraCatalog() { }

    public static string Kind => "camera";

    /// <summary>
    /// 후지필름 X 시리즈 (X-T5 — 2026-10-15·17 원본 FITS로 확인): 14비트 원본(0~16383), 바닥 1024, 별 중심이 14비트 위로 넘친 값이 조금 섞임,
    /// N.I.N.A. 후지 플러그인은 셔터 속도를 정한 일반 노출을 중단하지 못함(2026-10-11 실기), 장 사이 6~10초(N.I.N.A. 연속 촬영 사진 시각),
    /// X-Trans 색 무늬(머리글의 BAYERPAT는 RGGB로 잘못 적힘)
    /// </summary>
    public static readonly CameraTraits FujiX = new("fuji-x", "후지필름 X 시리즈", 16383, 1024, true, false, 10, SensorPattern.XTrans, "04CB");

    /// <summary>후지필름 GFX — 후지 플러그인이라 노출 중단 불가는 같음. 눈금·바닥은 X 시리즈와 같다고 본다 [확인 필요]</summary>
    public static readonly CameraTraits FujiGfx = new("fuji-gfx", "후지필름 GFX", 16383, 1024, true, false, 10, SensorPattern.Bayer, "04CB");

    /// <summary>캐논·니콘·소니 — 값은 기본 모델과 같고 USB 제조사 번호만 안다 (연결 전 확인용, 예전 DevicePrecheck 표). 쓰게 되면 원본 사진으로 값을 채운다</summary>
    public static readonly CameraTraits Canon = new("canon", "캐논", null, 0, false, true, PlanRules.FrameOverheadSeconds, SensorPattern.Unknown, "04A9");
    public static readonly CameraTraits Nikon = new("nikon", "니콘", null, 0, false, true, PlanRules.FrameOverheadSeconds, SensorPattern.Unknown, "04B0");
    public static readonly CameraTraits Sony = new("sony", "소니", null, 0, false, true, PlanRules.FrameOverheadSeconds, SensorPattern.Unknown, "054C");

    /// <summary>기본 모델 (모르는 카메라): 눈금은 사진·N.I.N.A. 비트 수로, 바닥 0, 노출 중단 가능, 쉬는 시간은 계획 기본값</summary>
    public static readonly CameraTraits Generic = new("generic", "모르는 카메라", null, 0, false, true, PlanRules.FrameOverheadSeconds, SensorPattern.Unknown);

    public static IReadOnlyList<CameraTraits> Models { get; } = [FujiX, FujiGfx, Canon, Nikon, Sony, Generic];

    public static CameraTraits Default => Generic;

    /// <summary>
    /// 고르기: 이름이 "X-"로 시작하거나 후지 드라이버면 X 시리즈, "GFX"면 GFX, 캐논·니콘·소니 드라이버면 그 모델, 아니면 기본 모델.
    /// (예전 Fits.IsFuji·NinaRig.IsFujifilm과 같은 기준을 한곳으로)
    /// </summary>
    public static CameraTraits Match(string? name, string? driver = null)
    {
        var n = (name ?? "").Trim();
        var all = n + " " + (driver ?? "");
        if (n.StartsWith("GFX", StringComparison.OrdinalIgnoreCase)) return FujiGfx;
        if (n.StartsWith("X-", StringComparison.OrdinalIgnoreCase)) return FujiX;
        if (all.Contains("fuji", StringComparison.OrdinalIgnoreCase))
            return all.Contains("GFX", StringComparison.OrdinalIgnoreCase) ? FujiGfx : FujiX;
        if (all.Contains("Canon", StringComparison.OrdinalIgnoreCase)) return Canon;
        if (all.Contains("Nikon", StringComparison.OrdinalIgnoreCase)) return Nikon;
        if (all.Contains("Sony", StringComparison.OrdinalIgnoreCase)) return Sony;
        return Generic;
    }
}

/// <summary>카메라의 지금 장비·배운 값 (공통 배운 값 저장소 <see cref="EquipmentLearned"/> 위의 도우미)</summary>
public static class CameraLearned
{
    /// <summary>배운 값 속성: 장 사이 쉬는 시간(초)</summary>
    public const string FrameOverhead = "frameOverhead";

    public static string? CurrentCamera(this EquipmentLearned learned) => learned.Current(CameraCatalog.Kind);

    public static void SetCurrentCamera(this EquipmentLearned learned, string? name) => learned.SetCurrent(CameraCatalog.Kind, name);

    /// <summary>지금 카메라(또는 이름을 준 카메라)의 모델</summary>
    public static CameraTraits CameraModel(this EquipmentLearned learned, string? name = null) =>
        CameraCatalog.Match(name ?? learned.CurrentCamera());

    /// <summary>계획에 쓸 장 사이 쉬는 시간(초): 이 카메라로 잰 최근 밤들의 중앙값, 없으면 모델 값</summary>
    public static double FrameOverheadSeconds(this EquipmentLearned learned, string? name = null) =>
        learned.Value(CameraCatalog.Kind, name, FrameOverhead) ?? learned.CameraModel(name).FrameOverheadSeconds;

    /// <summary>한 밤의 장 사이 쉬는 시간(그 밤 중앙값)을 지금 카메라에 남긴다</summary>
    public static void RecordFrameOverhead(this EquipmentLearned learned, double seconds, DateTimeOffset at)
    {
        if (seconds >= 0) learned.Record(CameraCatalog.Kind, null, FrameOverhead, seconds, at);
    }
}
