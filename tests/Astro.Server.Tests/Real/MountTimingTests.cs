using Astro.Core.Sky;
using Astro.Server.Prepare.Real;

namespace Astro.Server.Tests.Real;

/// <summary>멈춤·도착 판정 (2026-10-14 자체 리뷰): 같은 갱신은 비교하지 않고, 적경이나 시간각이 그대로면 멈춤</summary>
public class MountTimingTests
{
    private static NinaRig.MountState At(double ra, double dec, double lst, bool slewing = false) =>
        new(true, slewing, false, false, ra, dec, lst, new Site(37.5, 127));

    [Fact]
    public void 항성시가_같으면_같은_갱신이다()
    {
        Assert.True(MountTiming.SameSnapshot(5.123456, 5.123456));
        Assert.False(MountTiming.SameSnapshot(5.123456, 5.124012));
        Assert.False(MountTiming.SameSnapshot(0, 0)); // 항성시를 주지 않음 → 알 수 없으니 다른 갱신으로
    }

    [Fact]
    public void 추적_중_멈춤은_적경이_그대로()
    {
        var lst2 = 5 + 2.0 / 3600; // 2초 뒤
        Assert.True(MountTiming.Unmoved(At(80, 40, 5), At(80, 40, lst2), MountTiming.StillToleranceDeg));
    }

    [Fact]
    public void 추적을_끈_채_멈춤은_시간각이_그대로()
    {
        var lst2 = 5 + 2.0 / 3600;
        var ra2 = 80 + 2.0 / 3600 * 15; // 적경이 하늘을 따라 흐름 (약 0.008°)
        Assert.True(MountTiming.Unmoved(At(80, 40, 5), At(ra2, 40, lst2), MountTiming.StillToleranceDeg));
    }

    [Fact]
    public void 움직이면_멈춤이_아니다()
    {
        var lst2 = 5 + 2.0 / 3600;
        Assert.False(MountTiming.Unmoved(At(80, 40, 5), At(81, 40, lst2), MountTiming.StillToleranceDeg)); // 홈으로 가는 중 (Slewing은 false로 나옴)
        Assert.False(MountTiming.Unmoved(At(80, 40, 5), At(80, 40.2, lst2), MountTiming.StillToleranceDeg));
        Assert.True(MountTiming.Unmoved(At(359.999, 40, 5), At(0.001, 40, 5), MountTiming.StillToleranceDeg)); // 0°/360° 경계
    }
}
