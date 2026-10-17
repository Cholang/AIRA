namespace Astro.Server.Equipment.GuideCamera;

/// <summary>
/// 가이드 카메라 특성 (장비 › 가이드 카메라의 틀, 2026-10-17). 가이드 카메라는 PHD2가 쓰므로 아이라는 PHD2가 알려 주는 이름으로 고른다
/// (<see cref="GuideCameraCatalog.Match"/>). 게인 범위·픽셀 크기처럼 SharpCap·PHD2가 알려 주는 것은 여기 두지 않는다 —
/// 극 찾기의 처음 게인은 범위로 정한다(넓은 범위면 로그 눈금, SharpCapBridge)
/// </summary>
public sealed record GuideCameraTraits(
    string Id,
    string Name,
    /// <summary>USB 제조사 번호 (연결 전 확인 — 꽂혀 있는지). null = 확인하지 않음</summary>
    string? UsbVendorId) : IEquipmentModel;

/// <summary>가이드 카메라 카탈로그. 제조사 단위 모델 — 같은 제조사의 카메라는 USB 번호가 같다</summary>
public sealed class GuideCameraCatalog : IEquipmentCatalog<GuideCameraTraits>
{
    private GuideCameraCatalog() { }

    public static string Kind => "guidecamera";

    /// <summary>ToupTek 계열 (G3M662M — 2026-10-09 실기 USB 0547, 2026-10-17 집에서 PHD2 이름 "G3M662M(USB2.0)" 확인)</summary>
    public static readonly GuideCameraTraits ToupTek = new("touptek", "ToupTek", "0547");
    public static readonly GuideCameraTraits Zwo = new("zwo", "ZWO", "03C3");
    public static readonly GuideCameraTraits Qhy = new("qhy", "QHY", "1618");
    /// <summary>기본 모델 (모르는 가이드 카메라) — USB 확인 없음</summary>
    public static readonly GuideCameraTraits Generic = new("generic", "모르는 가이드 카메라", null);

    public static IReadOnlyList<GuideCameraTraits> Models { get; } = [ToupTek, Zwo, Qhy, Generic];

    public static GuideCameraTraits Default => Generic;

    /// <summary>
    /// 고르기: PHD2 카메라 이름에 제조사(ToupTek·ZWO·QHY)가 있으면 그 모델. 이름에 제조사가 없는 G3M662M(PHD2 이름 "G3M662M(USB2.0)", USB vid_0547)도 ToupTek으로 안다.
    /// (예전 DevicePrecheck.GuideCameraVendor 기준 + 모델 이름)
    /// </summary>
    public static GuideCameraTraits Match(string? name, string? driver = null)
    {
        var all = (name ?? "") + " " + (driver ?? "");
        bool Has(string s) => all.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("ToupTek") || Has("G3M662")) return ToupTek;
        if (Has("ZWO")) return Zwo;
        if (Has("QHY")) return Qhy;
        return Generic;
    }
}
