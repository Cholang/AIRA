using Astro.Core.Sky;
using Astro.Server.Assistant;

namespace Astro.Server.Tests.Plan;

/// <summary>
/// 계획의 촬영 시간대 (2026-10-14 사용자 결정): 촬영 시작 = 이동 + 준비 시간, 이동은 15° 이상, 기본 시작은 30° 위가 되는 때,
/// 시작을 옮기면 15° ~ 끝나기 10분 전, 끝은 시작 고도와 30° 중 낮은 쪽.
/// 대상 고도는 시각에 대한 직선(올라가는 대상)이나 봉우리 모양으로 만든다
/// </summary>
public class ShootWindowTests
{
    private static readonly DateTimeOffset Sunset = new(2026, 10, 13, 17, 56, 0, TimeSpan.FromHours(9));
    private static readonly NightWindow Night = new(Sunset, Sunset.AddMinutes(84), Sunset.AddHours(11).AddMinutes(9), Sunset.AddHours(12).AddMinutes(33), true);
    private static DateTimeOffset At(int h, int m) => new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.FromHours(9)).AddHours(h).AddMinutes(m);

    /// <summary>from에 alt0°, 한 시간에 rate°씩 올라감</summary>
    private static Func<DateTimeOffset, double> Rising(DateTimeOffset from, double alt0, double rate) => t => alt0 + (t - from).TotalHours * rate;

    /// <summary>peakAt에 peak°, 한 시간에 rate°씩 낮아지는 봉우리</summary>
    private static Func<DateTimeOffset, double> Peak(DateTimeOffset peakAt, double peak, double rate) => t => peak - Math.Abs((t - peakAt).TotalHours) * rate;

    [Fact]
    public void 올라오는_대상은_30도가_되는_때부터_찍고_이동은_준비시간_전이다()
    {
        // 21:00에 10°, 한 시간에 10°씩 → 23:00에 30°
        var w = ShootWindow.Compute(Rising(At(21, 0), 10, 10), Night, At(19, 30), null)!;
        Assert.Equal(At(23, 0), w.Start);
        Assert.Equal(ShootWait.Altitude, w.Wait);
        Assert.Equal(At(22, 40), w.MoveAt);
        Assert.Equal(30, w.EndAltitude);
    }

    [Fact]
    public void 해가_지기_전이면_어두워지는_때부터_찍는다()
    {
        var w = ShootWindow.Compute(_ => 50, Night, At(16, 0), null)!;
        Assert.Equal(Night.DarkStart, w.Start);
        Assert.Equal(ShootWait.Dark, w.Wait);
    }

    [Fact]
    public void 이미_높은_대상은_지금_더하기_준비시간부터()
    {
        var w = ShootWindow.Compute(_ => 50, Night, At(20, 0), null)!;
        Assert.Equal(At(20, 20), w.Start);
        Assert.Equal(ShootWait.None, w.Wait);
        Assert.Equal(ShootStop.Dawn, w.Stop);
    }

    [Fact]
    public void 시작을_낮게_옮기면_끝도_그_고도까지()
    {
        var alt = Peak(At(1, 0).AddDays(1), 60, 10); // 01:00에 60°
        var auto = ShootWindow.Compute(alt, Night, At(19, 30), null)!;
        var low = ShootWindow.Compute(alt, Night, At(19, 30), At(21, 0))!; // 21:00이면 20°
        Assert.Equal(20, low.StartAltitude, 0);
        Assert.Equal(20, low.EndAltitude, 0);
        Assert.True(low.Limit > auto.Limit);
        Assert.Equal(ShootWait.Chosen, low.Wait);
    }

    [Fact]
    public void 시작을_높게_옮겨도_끝은_30도()
    {
        var alt = Peak(At(1, 0).AddDays(1), 60, 10);
        var w = ShootWindow.Compute(alt, Night, At(19, 30), At(0, 0).AddDays(1))!; // 50°
        Assert.Equal(50, w.StartAltitude, 0);
        Assert.Equal(30, w.EndAltitude);
    }

    [Fact]
    public void 옮긴_시작은_15도와_끝나기_10분_전_사이로_맞춘다()
    {
        var alt = Rising(At(21, 0), 10, 10);
        // 너무 이르면: 이동(시작 − 20분) 때도 15° 이상이어야 → 21:30 + 20분 = 21:50
        var early = ShootWindow.Compute(alt, Night, At(19, 30), At(20, 0))!;
        Assert.Equal(At(21, 50), early.Start);
        Assert.Equal(early.DragFrom, early.Start);
        // 너무 늦으면: 끝나기 10분 전
        var late = ShootWindow.Compute(alt, Night, At(19, 30), At(4, 59).AddDays(1))!;
        Assert.Equal(late.DragTo, late.Start);
        Assert.True(late.Limit - late.Start >= PlanRules.EndMargin);
    }

    [Fact]
    public void 오늘_밤_15도_위로_못_올라오면_찍을_수_없다()
    {
        Assert.Null(ShootWindow.Compute(_ => 12, Night, At(19, 30), null));
    }

    [Fact]
    public void 장수는_총노출_나누기_노출이고_못_채우면_시간대의_최대()
    {
        var w = ShootWindow.Compute(_ => 50, Night, At(20, 0), null)!;
        var a = ShootAmount.Of(w, 120, 180);
        Assert.Equal(90, a.Frames);
        Assert.False(a.Clipped);
        Assert.Equal(90, a.Planned);
        // 끝 시각 = 시작 + 장수 × (노출 + 쉬는 시간)
        Assert.Equal(w.Start + TimeSpan.FromSeconds(90 * (120 + PlanRules.FrameOverheadSeconds)), a.End);

        var shortNight = ShootWindow.Compute(_ => 50, Night, At(4, 0).AddDays(1), null)!; // 04:20 ~ 05:05
        var b = ShootAmount.Of(shortNight, 120, 180);
        Assert.True(b.Clipped);
        Assert.Equal(b.MaxFrames, b.Planned);
        Assert.True(b.MaxFrames < b.Frames);
    }
}
