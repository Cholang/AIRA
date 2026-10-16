using Astro.Core.Sky;

namespace Astro.Server.Assistant;

/// <summary>
/// 계획 화면 "오늘 밤" 그래프 자료 (2026-10-14 개편, 시안 mockups/aira-plan-v3.html): 일몰~일출 시간축 하나에
/// 천문 박명, 지금, 정각마다 구름, 30분마다 달(위상·고도), 고른 대상들의 고도 곡선·촬영 시간대·시작점을 옮길 수 있는 범위.
/// 시각은 모두 일몰부터의 분(min)으로 보낸다 (화면이 시간대를 따로 계산하지 않게). 표시용 시각 글자도 함께.
/// </summary>
public static class NightChart
{
    private static readonly TimeSpan TrackStep = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MoonStep = TimeSpan.FromMinutes(30);

    public static object Build(NightContext n, ShootingPlan plan, DateTimeOffset now)
    {
        var from = n.Window.Sunset;
        var to = n.Window.Sunrise;
        double Min(DateTimeOffset t) => Math.Round((t - from).TotalMinutes, 1);
        static string Hm(DateTimeOffset t) => t.ToString("HH:mm");

        // 정각들 (시간 눈금·구름)
        var firstHour = new DateTimeOffset(from.Year, from.Month, from.Day, from.Hour, 0, 0, from.Offset).AddHours(1);
        var hours = new List<DateTimeOffset>();
        for (var h = firstHour; h <= to; h = h.AddHours(1)) hours.Add(h);

        // 달: 30분마다(정각·30분), 지평선 위만. 위상은 밝기와 차고 기우는지
        var firstHalf = new DateTimeOffset(from.Year, from.Month, from.Day, from.Hour, from.Minute >= 30 ? 30 : 0, 0, from.Offset).Add(MoonStep);
        var moon = new List<object>();
        for (var t = firstHalf; t <= to; t += MoonStep)
        {
            var alt = Astronomy.MoonAltitude(n.Site, t.UtcDateTime);
            if (alt >= 0) moon.Add(new { at = Min(t), alt = Math.Round(alt, 1), label = Hm(t) });
        }
        var mid = n.Window.DarkStart + (n.Window.DarkEnd - n.Window.DarkStart) / 2;
        var waxing = Astronomy.MoonIllumination(mid.AddHours(12).UtcDateTime) > Astronomy.MoonIllumination(mid.UtcDateTime);

        var settings = plan.Settings;
        var picks = plan.Picks.Select(p =>
        {
            var altitude = PlanTools.AltitudeOf(p.RaDegrees, p.DecDegrees, n.Site);
            var track = new List<double[]>();
            (DateTimeOffset At, double Alt) peak = (from, double.MinValue);
            for (var t = from; t <= to; t += TrackStep)
            {
                var a = altitude(t);
                track.Add([Min(t), Math.Round(a, 2)]);
                if (a > peak.Alt) peak = (t, a);
            }
            // 봉우리는 1분 단위로 다듬는다
            for (var t = peak.At - TrackStep; t <= peak.At + TrackStep; t += TimeSpan.FromMinutes(1))
                if (t >= from && t <= to && altitude(t) is var a && a > peak.Alt) peak = (t, a);

            var w = PlanTools.WindowOf(p, plan, n, now);
            object? window = null;
            if (w is not null)
            {
                var amount = settings is null ? null : ShootAmount.Of(w, settings.ExposureSeconds, settings.TotalMinutes, n.Rig.FrameOverheadSeconds);
                window = new
                {
                    start = Min(w.Start), limit = Min(w.Limit), end = amount is null ? Min(w.Limit) : Min(amount.End),
                    clipped = amount?.Clipped ?? false,
                    startAltitude = Math.Round(w.StartAltitude, 1), endAltitude = Math.Round(w.EndAltitude, 1),
                    transit = w.Transit is { } tr ? Min(tr) : (double?)null, transitLabel = w.Transit is { } tl ? Hm(tl) : null,
                    dragFrom = Min(w.DragFrom), dragTo = Min(w.DragTo), moved = w.Moved, moveAt = Min(w.MoveAt),
                };
            }
            return new
            {
                id = p.Id, name = p.Name, koreanName = p.KoreanName, type = p.Type,
                track,
                peak = peak.Alt > 0 ? new { at = Min(peak.At), alt = Math.Round(peak.Alt, 1), label = Hm(peak.At) } : null,
                window,
            };
        }).ToList();

        return new
        {
            sunsetLabel = Hm(from),
            sunriseLabel = Hm(to),
            // 시간축 끝(일출)까지의 분
            span = Min(to),
            // 일몰의 하루 중 분 — 화면이 분을 시각 글자로 바꿀 때
            startClock = from.Hour * 60 + from.Minute,
            dark = new { start = Min(n.Window.DarkStart), end = Min(n.Window.DarkEnd), startLabel = Hm(n.Window.DarkStart), endLabel = Hm(n.Window.DarkEnd) },
            now = Min(now),
            nowLabel = Hm(now),
            hours = hours.Select(h => new { at = Min(h), label = $"{h.Hour}시" }).ToList(),
            clouds = n.Clouds.Where(c => c.Time >= from && c.Time <= to).Select(c => new { at = Min(c.Time), cover = c.Cover }).ToList(),
            moon = new { illumination = Math.Round(n.Moon.Illumination * 100), waxing, samples = moon },
            current = plan.Target?.Id,
            picks,
            minAltitude = PlanRules.MinAltitude,
            lowestAltitude = PlanRules.LowestAltitude,
        };
    }
}
