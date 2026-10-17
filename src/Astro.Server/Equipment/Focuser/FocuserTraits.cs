namespace Astro.Server.Equipment.Focuser;

/// <summary>
/// 포커서 특성 (장비 › 포커서의 틀, 2026-10-17): 포커서라면 다 갖는 속성 목록. 카탈로그의 한 줄(모델)이 값을 채운다.
/// 아이라가 N.I.N.A.의 포커서 이름·드라이버로 고른다(<see cref="FocuserCatalog.Match"/>). 위치·기온·이동 중 여부처럼 드라이버가 알려 주는 것은 여기 두지 않는다
/// </summary>
public sealed record FocuserTraits(
    string Id,
    string Name,
    /// <summary>아이라가 제조사 SDK로 0점을 잡을 수 있는가 (ASCOM·N.I.N.A.에는 0점 명령이 없다). 못 하면 사용자가 드라이버 설정 창에서 Set Zero</summary>
    bool ZeroBySdk,
    /// <summary>포커서 최대 위치(걸음). N.I.N.A.가 알려 주지 않는다. null = 모름 — 설정 Prepare:FocuserMax(기본 56000)</summary>
    int? MaxPosition,
    /// <summary>끝에 닿아 멈췄을 때(멈춤 감지) 푸는 방법 — 안내 문장에</summary>
    string StallFix,
    /// <summary>USB 제조사 번호 (연결 전 확인 — 꽂혀 있는지). null = 확인하지 않음</summary>
    string? UsbVendorId) : IEquipmentModel;

/// <summary>포커서 카탈로그: 아이라가 아는 포커서 모델. 모르는 포커서는 기본 모델 <see cref="Generic"/> — 0점은 사용자가, 최대 위치는 설정값</summary>
public sealed class FocuserCatalog : IEquipmentCatalog<FocuserTraits>
{
    private FocuserCatalog() { }

    public static string Kind => "focuser";

    /// <summary>
    /// Astroasis Oasis (ASCOM.AOFocuser.Focuser — 2026-10-17 집에서 드라이버 2.0.2.1 확인): 제조사 SDK(OasisFocuser64.dll)로 0점,
    /// 범위 0~56000(2026-10-05 실기 — 사용자가 설정 창에서 정한 값), 멈춤 감지는 설정 창의 Clear stall로 푼다, USB 338F
    /// </summary>
    public static readonly FocuserTraits Oasis = new("oasis", "Astroasis Oasis", true, 56000,
        "포커서가 끝에 닿아 멈춘 것 같습니다. 포커서 설정 창(N.I.N.A. 포커서 톱니바퀴)에서 Clear stall을 눌러 주세요.", "338F");

    /// <summary>기본 모델 (모르는 포커서)</summary>
    public static readonly FocuserTraits Generic = new("generic", "모르는 포커서", false, null,
        "포커서가 끝에 닿아 멈춘 것 같습니다. 포커서 드라이버 설정이나 제조사 프로그램에서 멈춤을 풀어 주세요.", null);

    public static IReadOnlyList<FocuserTraits> Models { get; } = [Oasis, Generic];

    public static FocuserTraits Default => Generic;

    /// <summary>고르기: 이름·드라이버에 "Oasis" 또는 "AOFocuser"가 있으면 Oasis (예전 RealFocusDevices·DevicePrecheck 기준을 한곳으로)</summary>
    public static FocuserTraits Match(string? name, string? driver = null)
    {
        var all = (name ?? "") + " " + (driver ?? "");
        return all.Contains("Oasis", StringComparison.OrdinalIgnoreCase) || all.Contains("AOFocuser", StringComparison.OrdinalIgnoreCase) ? Oasis : Generic;
    }
}
