namespace Astro.Server.Prepare.Real;

/// <summary>
/// 적도의가 "멈췄다"·"도착했다"를 N.I.N.A. 정보(equipment/mount/info)로 판정하는 기준 한곳 (2026-10-14 자체 리뷰).
/// 문제: N.I.N.A.는 적도의 정보를 약 2초마다 갱신하는데 아이라는 1.2초마다 읽어, 연이은 두 읽기가 같은 갱신 값이면 움직이는 중에도 "좌표가 같다 = 멈춤"이 됐다.
/// 홈으로 가는 동안은 Slewing도 false라(2026-10-06 실기) 판정이 사실상 Slewing 하나에 기대고 있었다.
/// 고침: 항성시(SiderealTime)가 바뀐 두 읽기 — 서로 다른 갱신 — 끼리만 비교하고, 적경이 그대로(추적 중 멈춤)이거나 시간각이 그대로(추적 끈 채 멈춤)면 멈춤.
///
/// [실기 확인] 아래 값은 2026-10-06 실내 실기 기록("정보는 약 2초마다 갱신")에서 추정했다. 집에서 장비를 연결하면 확인해 이 파일만 고친다
/// (docs/FIELD_TEST_CHECKLIST.md "적도의 정보 갱신 간격"): ① 연이어 읽을 때 항성시가 몇 초마다 바뀌는지 ② 홈 이동 중 좌표가 갱신마다 바뀌는지
/// ③ 멈춰 있을 때 갱신 사이 적경·시간각이 얼마나 흔들리는지(아래 허용 오차보다 작아야 한다)
/// </summary>
public static class MountTiming
{
    /// <summary>[실기 확인] N.I.N.A. 적도의 정보 갱신 간격 — 2026-10-06 기록 "약 2초"</summary>
    public static readonly TimeSpan InfoRefresh = TimeSpan.FromSeconds(2);

    /// <summary>읽는 간격: 갱신보다 조금 길게 — 대개 매번 새 갱신을 읽는다(같은 값이면 아래 판정이 건너뜀)</summary>
    public static readonly TimeSpan PollInterval = InfoRefresh + TimeSpan.FromMilliseconds(400);

    /// <summary>[실기 확인] 멈춤 허용 오차(도): 한 갱신(약 2초) 사이의 흔들림. 추적 끈 채 멈추면 적경이 2초에 약 0.008° 흐르므로 그때는 시간각으로 본다</summary>
    public const double StillToleranceDeg = 0.005;

    /// <summary>[실기 확인] 도착 판정 허용 오차(도) — 이동 끝의 미세한 움직임</summary>
    public const double ArrivedToleranceDeg = 0.01;

    /// <summary>같은 갱신에서 읽은 값인가: 항성시가 그대로면 같은 갱신. 항성시를 주지 않으면(0·NaN) 알 수 없으니 다른 갱신으로 본다(읽는 간격이 갱신보다 길다)</summary>
    public static bool SameSnapshot(double previousSiderealHours, double siderealHours) =>
        double.IsFinite(previousSiderealHours) && double.IsFinite(siderealHours) && previousSiderealHours != 0
        && Math.Abs(previousSiderealHours - siderealHours) < 1e-9;

    /// <summary>두 읽기 사이에 멈춰 있었나: 적위가 그대로이고, 적경이 그대로(추적 중)이거나 시간각이 그대로(추적 끔)</summary>
    public static bool Unmoved(NinaRig.MountState before, NinaRig.MountState after, double toleranceDeg) =>
        Math.Abs(before.DecDeg - after.DecDeg) < toleranceDeg
        && (AngleDiff(before.RaDeg, after.RaDeg) < toleranceDeg || AngleDiff(HourAngleDeg(before), HourAngleDeg(after)) < toleranceDeg);

    private static double HourAngleDeg(NinaRig.MountState m) => m.SiderealHours * 15 - m.RaDeg;

    /// <summary>두 각도의 차이(도, 0~180) — 0°/360° 경계를 넘어도</summary>
    private static double AngleDiff(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }
}
