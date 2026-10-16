using Astro.Core.Sky;

namespace Astro.Server.Tests.Plan;

/// <summary>
/// 오늘 밤 계산의 바탕 (Gemini 리뷰 GM-7-E-01): 율리우스일·항성시·고도·시간각·각거리, 태양·달 위치, 일몰·박명·일출, 대상의 자오선 통과.
/// 기준값은 알려진 천문 자료(분 단위로 어림). 계산식이 틀어지면 계획 화면의 그래프·촬영 시간대가 통째로 틀어진다
/// </summary>
public class AstronomyAndNightTests
{
    private static readonly Site Seoul = new(37.5665, 126.9780);
    private static readonly TimeZoneInfo Kst = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
    private static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Julian_Date_And_Sidereal_Time_At_J2000()
    {
        Assert.Equal(2451545.0, Astronomy.JulianDate(Utc(2000, 1, 1, 12, 0)), 6);
        Assert.Equal(280.46, Astronomy.LocalSiderealDegrees(Utc(2000, 1, 1, 12, 0), 0), 2);
        // 경도만큼 더하고 0~360°로 (서울 +126.978° → 280.46 + 126.978 - 360)
        Assert.Equal((Astronomy.LocalSiderealDegrees(Utc(2000, 1, 1, 12, 0), 0) + 126.978) % 360, Astronomy.LocalSiderealDegrees(Utc(2000, 1, 1, 12, 0), 126.978), 6);
    }

    [Fact]
    public void Separation_Is_Zero_Opposite_And_Wraps_Around_Ra_Zero()
    {
        Assert.Equal(0, Astronomy.Separation(new(10, 20), new(10, 20)), 6);
        Assert.Equal(180, Astronomy.Separation(new(0, 0), new(180, 0)), 6);
        Assert.Equal(2, Astronomy.Separation(new(359, 0), new(1, 0)), 6);
        Assert.Equal(90, Astronomy.Separation(new(0, 0), new(0, 90)), 6);
    }

    [Fact]
    public void Polaris_Altitude_Is_About_The_Latitude_All_Night()
    {
        var polaris = new Equatorial(37.95, 89.264);
        for (var h = 0; h < 24; h += 3)
            Assert.InRange(Astronomy.Altitude(polaris, Seoul, Utc(2026, 10, 16, h, 0)), Seoul.Latitude - 0.8, Seoul.Latitude + 0.8);
    }

    [Fact]
    public void Hour_Angle_Is_Zero_On_The_Meridian_And_Stays_Within_Half_A_Turn()
    {
        var t = Utc(2026, 10, 16, 13, 0);
        var onMeridian = new Equatorial(Astronomy.LocalSiderealDegrees(t, Seoul.Longitude), 30);
        Assert.Equal(0, Astronomy.HourAngle(onMeridian, Seoul, t), 6);
        // 자오선 위에서 고도는 가장 높다: 90 - |위도 - 적위|, 방위는 남(180°)
        Assert.Equal(90 - (Seoul.Latitude - 30), Astronomy.Altitude(onMeridian, Seoul, t), 6);
        Assert.Equal(180, Astronomy.Azimuth(onMeridian, Seoul, t), 3);
        for (var ra = 0; ra < 360; ra += 15)
            Assert.InRange(Astronomy.HourAngle(new(ra, 0), Seoul, t), -180, 180);
    }

    [Theory]
    [InlineData(2024, 6, 20, 20, 51, 23.44)]   // 하지
    [InlineData(2024, 12, 21, 9, 20, -23.44)]  // 동지
    [InlineData(2024, 3, 20, 3, 6, 0)]         // 춘분
    public void Sun_Declination_At_Solstices_And_Equinox(int y, int mo, int d, int h, int mi, double dec) =>
        Assert.InRange(Astronomy.Sun(Utc(y, mo, d, h, mi)).Dec, dec - 0.1, dec + 0.1);

    [Fact]
    public void Moon_Is_Full_And_New_On_Known_Dates()
    {
        Assert.True(Astronomy.MoonIllumination(Utc(2024, 1, 25, 17, 54)) > 0.97); // 보름
        Assert.True(Astronomy.MoonIllumination(Utc(2024, 1, 11, 11, 57)) < 0.03); // 그믐(삭)
    }

    [Fact]
    public void Seoul_Mid_October_Night_Has_Sunset_Dark_And_Sunrise_In_Order_At_Known_Times()
    {
        var n = Night.Window(Seoul, new DateOnly(2026, 10, 16), Kst);
        Assert.True(n.AstronomicalDark);
        Assert.True(n.Sunset < n.DarkStart && n.DarkStart < n.DarkEnd && n.DarkEnd < n.Sunrise);
        // 서울 10월 16일: 일몰 17:51 · 천문 박명 끝 19:14 · 다음 날 천문 박명 시작 05:18 · 일출 06:41 (KST, 몇 분 어림)
        static void Near(DateTimeOffset at, int day, int h, int m) =>
            Assert.InRange((at - new DateTimeOffset(2026, 10, day, h, m, 0, TimeSpan.FromHours(9))).TotalMinutes, -8, 8);
        Near(n.Sunset, 16, 17, 51);
        Near(n.DarkStart, 16, 19, 14);
        Near(n.DarkEnd, 17, 5, 18);
        Near(n.Sunrise, 17, 6, 41);
    }

    [Fact]
    public void High_Latitude_Summer_Night_Falls_Back_To_Nautical_Twilight()
    {
        var oslo = new Site(59.91, 10.75);
        var cest = TimeZoneInfo.CreateCustomTimeZone("CEST", TimeSpan.FromHours(2), "CEST", "CEST");
        var n = Night.Window(oslo, new DateOnly(2026, 6, 21), cest);
        Assert.False(n.AstronomicalDark); // 6월 오슬로는 -18°까지 어두워지지 않는다
        Assert.True(n.Sunset <= n.DarkStart && n.DarkStart <= n.DarkEnd && n.DarkEnd <= n.Sunrise);
    }

    [Fact]
    public void Target_Transits_Inside_The_Dark_With_The_Right_Highest_Altitude()
    {
        var night = Night.Window(Seoul, new DateOnly(2026, 10, 16), Kst);
        var m31 = new Equatorial(10.6847, 41.2690);
        var t = Night.Target(m31, Seoul, night);
        Assert.NotNull(t.Transit);
        Assert.Equal(0, Astronomy.HourAngle(m31, Seoul, t.Transit!.Value.UtcDateTime), 0); // 1° 안
        Assert.InRange(t.HighestAltitude, 90 - (41.269 - Seoul.Latitude) - 1, 90 - (41.269 - Seoul.Latitude) + 0.1);
        Assert.NotNull(t.UsableStart);
        Assert.True(t.UsableStart >= night.DarkStart && t.UsableEnd <= night.DarkEnd);
    }
}
