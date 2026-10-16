namespace Astro.Server.Assistant;

/// <summary>화면·AI에 보내는 계획 모양 (2026-10-14 개편): 고른 대상들, 지금 대상, 설정과 기본값, 고를 수 있는 값, 지금 대상의 시간대·장수</summary>
public static class PlanView
{
    public static object From(ShootingPlan p, NightContext n, PlanTools tools, DateTimeOffset now)
    {
        static string Hm(DateTimeOffset t) => t.ToString("HH:mm");
        var fov = n.Rig.FieldOfView;
        var stepIso = PlanRules.IsStepIso(n.Rig.IsoMin, n.Rig.IsoMax);
        var s = p.Settings;
        var key = s is null ? PlanRules.DefaultFilter : s.FilterKey ?? PlanRules.FilterKey(s.Filter);
        var (defaultExposure, defaultIso) = tools.DefaultsFor(n, key);

        object? window = null;
        if (p.Target is { } t && s is not null && PlanTools.WindowOf(t, p, n, now) is { } w)
        {
            var a = ShootAmount.Of(w, s.ExposureSeconds, s.TotalMinutes);
            window = new
            {
                start = Hm(w.Start), limit = Hm(w.Limit), end = Hm(a.End), moveAt = Hm(w.MoveAt),
                startAltitude = Math.Round(w.StartAltitude), endAltitude = Math.Round(w.EndAltitude),
                wait = w.Wait.ToString().ToLowerInvariant(), stop = w.Stop.ToString().ToLowerInvariant(), moved = w.Moved,
                transit = w.Transit is { } tr ? Hm(tr) : null,
                frames = a.Frames, maxFrames = a.MaxFrames, planned = a.Planned, clipped = a.Clipped,
                secondsPerFrame = a.SecondsPerFrame, overheadSeconds = PlanRules.FrameOverheadSeconds,
                minutes = (int)Math.Round((w.Limit - w.Start).TotalMinutes),
            };
        }

        return new
        {
            complete = p.Complete,
            asking = p.Asking,
            night = PlanAssistant.NightSummary(n),
            current = p.Target?.Id,
            // 지금 대상의 시작을 사용자가 옮겼나 — 시간대가 없어져도 되돌리기를 보이게
            startMoved = p.Target is { } cur && p.StartOverrides.ContainsKey(cur.Id),
            picks = p.Picks.Select(x => new
            {
                id = x.Id, name = x.Name, koreanName = x.KoreanName, commonName = x.CommonName, type = x.Type, constellation = x.Constellation,
                sizeArcmin = x.SizeArcmin is { } sz ? Math.Round(sz, 1) : (double?)null,
                raDeg = Math.Round(x.RaDegrees, 4), decDeg = Math.Round(x.DecDegrees, 4),
                fillPercent = PlanTools.Fill(x.SizeArcmin, n.Rig), moonSeparation = Math.Round(x.MoonSeparation),
            }).ToList(),
            fov = new { width = Math.Round(fov.Width, 2), height = Math.Round(fov.Height, 2), camera = n.Rig.Camera, telescope = n.Rig.Telescope },
            settings = s is null ? null : new
            {
                filter = key, iso = s.Iso, exposure = s.ExposureSeconds, totalMinutes = s.TotalMinutes,
                defaults = new { iso = defaultIso, exposure = defaultExposure, totalMinutes = PlanRules.DefaultTotalMinutes },
            },
            options = new
            {
                filters = PlanRules.Filters.Select(f => new { key = f.Key, label = f.Label }).ToList(),
                gain = stepIso
                    ? (object)new { kind = "list", unit = "ISO", values = PlanRules.IsoOptions(n.Rig.IsoMin, n.Rig.IsoMax) }
                    : new { kind = "range", unit = "게인", min = n.Rig.IsoMin, max = n.Rig.IsoMax },
                exposure = new { min = PlanRules.MinExposureSeconds, max = PlanRules.MaxExposureSeconds, step = PlanRules.ExposureStepSeconds },
                totalMinutes = new { min = PlanRules.MinTotalMinutes, max = PlanRules.MaxTotalMinutes, step = PlanRules.TotalStepMinutes },
                maxPicks = PlanRules.MaxPicks,
                prepMinutes = (int)n.PrepLead.TotalMinutes,
            },
            window,
        };
    }
}
