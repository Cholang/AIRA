namespace Astro.Server.Equipment.Mount;

/// <summary>
/// 적도의 특성 (장비 › 적도의의 틀, 2026-10-17): 적도의라면 다 갖는 속성 목록. 카탈로그의 한 줄(모델)이 값을 채운다.
/// 아이라가 N.I.N.A.의 적도의 이름·드라이버로 고른다(<see cref="MountCatalog.Match"/>). 드라이버가 그때그때 알려 주는 능력(축 돌리기 가능·Sync 가능 등)은
/// 여기 두지 않고 그 자리에서 묻는다 — 여기는 드라이버가 알려 주지 않는, 모델마다 다른 값만
/// </summary>
public sealed record MountTraits(
    string Id,
    string Name,
    /// <summary>극 찾기 때 RA축을 돌리는 각도(도) — 홈 근처에서 한쪽으로. 적도의가 그쪽으로 돌 수 있는 한계 안이어야 한다</summary>
    double PolarRotationDeg,
    /// <summary>극 찾기 회전 속도(°/s, ASCOM MoveAxis) — 드라이버가 허용하는 범위 안</summary>
    double RotationRateDegPerSec,
    /// <summary>홈 센서가 있어 "홈으로 보내기"로 정해진 자리에 돌아오는가. 없으면 극 찾기를 다시 하기 전 사용자가 옮긴다</summary>
    bool HasHomeSensor) : IEquipmentModel;

/// <summary>
/// 적도의 카탈로그: 아이라가 아는 적도의 모델. 새 적도의를 들이면 여기에 한 줄 더하고, 회전 한계·홈 센서를 실제로 확인해 적는다.
/// 모르는 적도의는 기본 모델 <see cref="Generic"/> — 회전을 작게, 홈 센서는 없다고 본다(안전한 쪽)
/// </summary>
public sealed class MountCatalog : IEquipmentCatalog<MountTraits>
{
    private MountCatalog() { }

    public static string Kind => "mount";

    /// <summary>
    /// Proxisky UMi17X (OnStepX). 홈에서 한쪽으로 약 60°까지만 돈다(2026-10-04 실기) — 제조사 사양에 RA 리밋 센서(소프트웨어로 위치 조절)가 있어 그 센서로 보인다 [실기 확인].
    /// 물리 홈 센서 있음(사양), GOTO 3.5°/s(사양 — 드라이버 최대 3.55°/s), 회전은 2°/s
    /// </summary>
    public static readonly MountTraits UMi17X = new("umi17x", "Proxisky UMi17X (OnStepX)", 60, 2.0, true);

    /// <summary>기본 모델 (모르는 적도의): 회전 45° [확인 필요 — SharpCap 극축 정렬이 받아들이는 최소 회전], 홈 센서 없음</summary>
    public static readonly MountTraits Generic = new("generic", "모르는 적도의", 45, 2.0, false);

    public static IReadOnlyList<MountTraits> Models { get; } = [UMi17X, Generic];

    public static MountTraits Default => Generic;

    /// <summary>
    /// 고르기: 이름·드라이버에 "UMi"가 있으면 UMi17X. "OnStep"("On-Step")도 지금은 UMi17X — 아는 OnStep 적도의가 이것뿐이다
    /// (OnStep은 여러 적도의가 쓰는 펌웨어라, 다른 OnStep 적도의를 들이면 이름으로 나눈다. 60°는 흔한 적도의보다 작은 회전이라 잘못 골라도 안전한 쪽)
    /// </summary>
    public static MountTraits Match(string? name, string? driver = null)
    {
        var all = (name ?? "") + " " + (driver ?? "");
        if (all.Contains("UMi", StringComparison.OrdinalIgnoreCase)) return UMi17X;
        if (all.Contains("OnStep", StringComparison.OrdinalIgnoreCase) || all.Contains("On-Step", StringComparison.OrdinalIgnoreCase)) return UMi17X;
        return Generic;
    }
}
