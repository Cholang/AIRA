using System.Text.Json;
using Astro.Core.Assistant;
using Astro.Core.Sky;
using Astro.Server.Sky;

namespace Astro.Server.Assistant;

/// <summary>오늘 밤 계산에 필요한 것 한 묶음 (한 대화 동안 그대로)</summary>
public sealed record NightContext(
    Site Site, Rig Rig, NightWindow Window, MoonNight Moon, IReadOnlyList<CloudHour> Clouds, TimeZoneInfo Zone);

/// <summary>
/// 계획을 고치는 곳 하나 (2026-10-14 개편): 화면(API)과 AI 도구가 같은 함수로 같은 계획을 고친다.
/// 숫자(시각·고도·화각·장수)는 모두 여기서 계산하고, AI는 결과를 읽어 대화만 한다. 기준값은 PlanRules.
/// </summary>
public sealed class PlanTools(DsoCatalog catalog, Engine.EquipmentChoices equipment)
{
    public static readonly IReadOnlyList<ToolSpec> Specs =
    [
        Spec("get_tonight", "오늘 밤 조건: 어두운 시간, 달(밝기·떠 있는 시간), 어두운 시간 동안 시간별 구름양, 촬영 장비(화각 포함).",
            """{"type":"object","properties":{}}"""),
        Spec("search_targets", "대상을 이름으로 찾는다. 'M45', 'NGC 7000', 'IC 434', 'Sh2-155', 'C14', 영문 통칭('Pleiades', 'Orion Nebula')을 넣는다. 한국어 이름은 영문 통칭이나 번호로 바꿔서 넣을 것. 오늘 밤 이 관측지에서 15° 위로 올라오지 않는 대상은 빠진다. 찍을 수 있는 시간과 화각에 차지하는 크기도 함께 돌려준다.",
            """{"type":"object","properties":{"query":{"type":"string","description":"찾을 이름"}},"required":["query"]}"""),
        Spec("suggest_targets", "오늘 밤 이 장비로 찍기 좋은 대상을 고른다 (메시에·칼드웰 중 오래 높이 뜨고 화각에 잘 맞고 달과 먼 것).",
            """{"type":"object","properties":{"kind":{"type":"string","enum":["all","galaxy","nebula","cluster"],"description":"종류 제한"},"limit":{"type":"integer","description":"개수 (기본 4)"}}}"""),
        Spec("pick_target", "대상을 고른다: 그래프에 더하고 지금 대상으로 한다(이미 있으면 지금 대상으로만). 그래프에는 최대 3개, 넘치면 가장 오래 안 본 것이 빠진다. 사용자가 고르거나 동의했을 때만 부른다. 처음 고르면 촬영 설정이 기본값으로 채워진다.",
            """{"type":"object","properties":{"target_id":{"type":"string","description":"search_targets·suggest_targets가 돌려준 id"},"korean_name":{"type":"string","description":"한국어 통칭 (예: 플레이아데스). 모르면 비움"}},"required":["target_id"]}"""),
        Spec("remove_target", "고른 대상 하나를 그래프에서 뺀다. 지금 대상을 빼면 남은 것 중 가장 최근 대상이 지금 대상이 된다.",
            """{"type":"object","properties":{"target_id":{"type":"string"}},"required":["target_id"]}"""),
        Spec("set_settings", "지금 대상의 촬영 설정을 바꾼다. 바꿀 값만 넣는다. 사용자가 말로 부탁했을 때 부른다. 필터를 바꾸면 고치지 않은 노출·ISO는 그 필터의 기본값으로 바뀐다. 장수·끝 시각은 서버가 계산한다.",
            """{"type":"object","properties":{"filter":{"type":"string","enum":["none","light_pollution","dual_narrowband"]},"iso":{"type":"integer","description":"ISO(게인)"},"exposure_seconds":{"type":"integer","description":"한 장 노출(초)"},"total_minutes":{"type":"integer","description":"총 노출(분) = 노출 × 장수"}}}"""),
        Spec("set_start", "지금 대상의 촬영 시작 시각을 옮긴다(예: 더 높이 올라온 뒤에 시작). 'auto'면 기본(30° 위가 되는 때). 범위 밖이면 가능한 가장 가까운 시각으로 맞춘다.",
            """{"type":"object","properties":{"time":{"type":"string","description":"HH:mm 또는 auto"}},"required":["time"]}"""),
        Spec("offer_choices", "답 아래에 누를 수 있는 선택지를 보여 준다(2~4개, 각 12자 이내). 질문할 때 먼저 부른다.",
            """{"type":"object","properties":{"choices":{"type":"array","items":{"type":"string"}}},"required":["choices"]}"""),
    ];

    private static ToolSpec Spec(string name, string description, string schema) =>
        new(name, description, JsonDocument.Parse(schema).RootElement.Clone());

    /// <summary>도구 실행. 계획이 바뀌면 plan을 고치고, 선택지는 choices로 돌려준다.</summary>
    public JsonElement Run(string name, JsonElement args, NightContext night, ShootingPlan plan, out IReadOnlyList<string>? choices)
    {
        choices = null;
        try
        {
            object result = name switch
            {
                "get_tonight" => Tonight(night, Guiding(night)),
                "search_targets" => SearchTool(Str(args, "query") ?? "", night),
                "suggest_targets" => Suggest(Str(args, "kind") ?? "all", Int(args, "limit") ?? 4, night),
                // set_target은 예전 이름 (연습 대화·옛 기록 호환)
                "pick_target" or "set_target" => Result(Pick(Str(args, "target_id") ?? "", Str(args, "korean_name"), night, plan), plan, night),
                "remove_target" => Result(Remove(Str(args, "target_id") ?? "", night, plan), plan, night),
                "set_settings" => Result(ChangeSettings(night, plan, Str(args, "filter"), Int(args, "iso"), Int(args, "exposure_seconds"), Int(args, "total_minutes")), plan, night),
                "set_start" => Result(SetStartTool(Str(args, "time"), night, plan), plan, night),
                "recommend_settings" => Defaults(night, PlanRules.FilterKey(Str(args, "filter"))),
                // 구도·끝난 뒤는 묻지 않는다 (기본값) — 예전 도구 이름은 그대로 성공
                "set_framing" or "set_after" => new { ok = true, note = "구도·끝난 뒤는 기본값을 씁니다." },
                "offer_choices" => Offer(args, plan, out choices),
                _ => new { error = $"없는 도구입니다: {name}" },
            };
            return JsonSerializer.SerializeToElement(result, Json);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return JsonSerializer.SerializeToElement(new { error = e.Message }, Json);
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>도구 결과: 문제가 있으면 그 문장, 없으면 지금 대상의 시간대·장수 요약</summary>
    private object Result(string? problem, ShootingPlan plan, NightContext n) =>
        problem is not null ? new { error = problem } : new { ok = true, now = Summary(plan, n) };

    private object? Summary(ShootingPlan plan, NightContext n)
    {
        if (plan is not { Target: { } t, Settings: { } s }) return null;
        return new
        {
            target = t.Name, korean_name = t.KoreanName,
            filter = s.Filter, iso = s.Iso, exposure_seconds = s.ExposureSeconds, total_minutes = s.TotalMinutes,
            start = Hm(s.Start), shootable_until = Hm(s.End), planned_frames = s.EstimatedFrames,
            picks = plan.Picks.Select(p => p.Name).ToList(),
        };
    }

    private static string Hm(DateTimeOffset? t) => t?.ToString("HH:mm") ?? "-";

    /// <summary>가이딩을 쓰는지: 프로필에 가이더가 있고, 장비 연결에서 "없이 진행"을 고르지 않았을 때</summary>
    public bool Guiding(NightContext n) => n.Rig.HasGuider && !equipment.IsWithout("guider");

    // ── 계산 ─────────

    /// <summary>대상 고도(도) — 시각 함수</summary>
    public static Func<DateTimeOffset, double> AltitudeOf(double raDeg, double decDeg, Site site)
    {
        var eq = new Equatorial(raDeg, decDeg);
        return t => Astronomy.Altitude(eq, site, t.UtcDateTime);
    }

    /// <summary>대상 하나의 촬영 시간대 (사용자가 옮긴 시작 반영). 오늘 밤 찍을 수 없으면 null</summary>
    public static ShootWindow? WindowOf(PlanTarget t, ShootingPlan plan, NightContext n, DateTimeOffset now) =>
        ShootWindow.Compute(AltitudeOf(t.RaDegrees, t.DecDegrees, n.Site), n.Window, now, plan.StartOverrides.TryGetValue(t.Id, out var at) ? at : null);

    /// <summary>오늘 밤 이 관측지에서 15° 위로 올라오는 때가 어두운 시간 안에 있나 (장소·날짜상 절대 못 찍는 대상 거르기)</summary>
    public static bool VisibleTonight(Equatorial position, NightContext n)
    {
        for (var t = n.Window.DarkStart; t <= n.Window.DarkEnd; t += TimeSpan.FromMinutes(10))
            if (Astronomy.Altitude(position, n.Site, t.UtcDateTime) >= PlanRules.LowestAltitude) return true;
        return false;
    }

    private bool BrightMoonUp(NightContext n) => n.Moon.Illumination > 0.5 && n.Moon.UpIntervals.Count > 0;

    /// <summary>필터에 따른 기본 노출·ISO (대상과 상관없음 — 장비·가이딩·달). ISO는 카메라가 쓸 수 있는 단계로 맞춘다</summary>
    public (int ExposureSeconds, int Iso) DefaultsFor(NightContext n, string filterKey)
    {
        var (exposure, iso, _) = PlanRules.DefaultExposure(filterKey, n.Rig.FocalRatio, Guiding(n), BrightMoonUp(n));
        return (exposure, NearestIso(n.Rig, iso));
    }

    private object Defaults(NightContext n, string filterKey)
    {
        var (exposure, iso, notes) = PlanRules.DefaultExposure(filterKey, n.Rig.FocalRatio, Guiding(n), BrightMoonUp(n));
        return new { exposure_seconds = exposure, iso = NearestIso(n.Rig, iso), notes };
    }

    private static int NearestIso(Rig rig, int iso)
    {
        if (!PlanRules.IsStepIso(rig.IsoMin, rig.IsoMax)) return Math.Clamp(iso, rig.IsoMin ?? iso, rig.IsoMax ?? iso);
        return PlanRules.IsoOptions(rig.IsoMin, rig.IsoMax).OrderBy(i => Math.Abs(i - iso)).First();
    }

    /// <summary>대상이 화면 긴 변에서 차지하는 비율(%)</summary>
    public static double? Fill(double? sizeArcmin, Rig rig) =>
        sizeArcmin is { } s && rig.FieldOfView.Width > 0 ? Math.Round(s / 60 / rig.FieldOfView.Width * 100) : null;

    /// <summary>지금 대상의 설정에 시간대·장수를 다시 넣는다 (시각이 흐르거나, 설정·시작이 바뀔 때마다)</summary>
    public static void Recount(ShootingPlan plan, NightContext n, DateTimeOffset now)
    {
        if (plan is not { Target: { } t, Settings: { } s }) return;
        var w = WindowOf(t, plan, n, now);
        if (w is null)
        {
            plan.Settings = s with { Start = n.Window.DarkStart, End = n.Window.DarkStart, EstimatedFrames = 0, EndRule = EndRule.TargetLowOrDawn };
            return;
        }
        var amount = ShootAmount.Of(w, s.ExposureSeconds, s.TotalMinutes);
        plan.Settings = s with
        {
            Start = w.Start, End = w.Limit, EstimatedFrames = amount.Planned, EndRule = EndRule.TargetLowOrDawn, EndAltitude = w.EndAltitude,
        };
    }

    // ── 고치기 (화면과 AI가 함께 쓴다). 문제가 있으면 사용자에게 보여 줄 문장 ─────────

    /// <summary>대상 고르기: 그래프에 더하고 지금 대상으로. 이미 있으면 가장 최근으로 올린다. 처음이면 설정을 기본값으로</summary>
    public string? Pick(string id, string? korean, NightContext n, ShootingPlan plan)
    {
        var existing = plan.Picks.FirstOrDefault(p => p.Id == id);
        PlanTarget target;
        if (existing is not null)
        {
            target = string.IsNullOrWhiteSpace(korean) ? existing : existing with { KoreanName = korean.Trim() };
            plan.Picks.Remove(existing);
        }
        else
        {
            if (catalog.Get(id) is not { } d) return "그 대상을 찾지 못했습니다. 이름으로 다시 찾아 주세요.";
            if (!VisibleTonight(d.Position, n)) return $"{d.Name}{Core.Josa.Pick(d.Name, "은", "는")} 오늘 밤 이 관측지에서 15° 위로 올라오지 않습니다.";
            var tn = Night.Target(d.Position, n.Site, n.Window);
            target = new PlanTarget(d.Id, d.Name, d.CommonName, string.IsNullOrWhiteSpace(korean) ? KoreanNames.Of(d) : korean.Trim(),
                d.Type, d.Constellation, d.SizeArcmin, d.Magnitude, d.Position.Ra, d.Position.Dec,
                tn.UsableStart, tn.UsableEnd, tn.HighestAt, tn.HighestAltitude, tn.Transit, tn.MoonSeparation);
        }
        plan.Picks.Add(target);
        while (plan.Picks.Count > PlanRules.MaxPicks)
        {
            plan.StartOverrides.Remove(plan.Picks[0].Id);
            plan.Picks.RemoveAt(0);
        }
        Use(target, n, plan);
        return null;
    }

    /// <summary>고른 대상 빼기. 지금 대상이면 남은 것 중 가장 최근으로, 다 빠지면 처음 상태(설정 없음)</summary>
    public string? Remove(string id, NightContext n, ShootingPlan plan)
    {
        var p = plan.Picks.FirstOrDefault(x => x.Id == id);
        if (p is null) return "그래프에 없는 대상입니다.";
        plan.Picks.Remove(p);
        plan.StartOverrides.Remove(id);
        if (plan.Target?.Id != id) return null;
        if (plan.Picks.Count > 0) Use(plan.Picks[^1], n, plan);
        else
        {
            plan.Target = null;
            plan.Framing = null;
            plan.Settings = null;
            plan.After = null;
            plan.Asking = "target";
        }
        return null;
    }

    /// <summary>지금 대상으로: 구도·끝난 뒤는 기본값, 설정은 처음이면 기본값(이미 있으면 그대로) — 그리고 다시 계산</summary>
    private void Use(PlanTarget target, NightContext n, ShootingPlan plan)
    {
        plan.Target = target;
        // 구도·끝난 뒤는 묻지 않는다: 가운데·지금 카메라 방향 / 파킹·다크·플랫·연결 해제 (2026-10-14, 다크·플랫은 늘 찍음)
        plan.Framing = new PlanFraming("가운데", null, Fill(target.SizeArcmin, n.Rig));
        plan.After ??= new PlanAfter(true, CalibrationFrames.DarksAndFlats, true);
        if (plan.Settings is null)
        {
            var filter = PlanRules.DefaultFilter;
            var (exposure, iso) = DefaultsFor(n, filter);
            plan.Settings = new PlanSettings(exposure, iso, PlanRules.FilterLabel(filter), EndRule.TargetLowOrDawn,
                n.Window.DarkStart, n.Window.DarkEnd, 0, true, true, PlanRules.DefaultTotalMinutes, filter);
        }
        if (plan.Asking == "target") plan.Asking = null;
        Recount(plan, n, DateTimeOffset.Now);
    }

    /// <summary>
    /// 설정 바꾸기 (바꿀 값만). 범위 밖이면 맞추지 않고 이유를 돌려준다(조용히 다른 값으로 찍지 않게, Codex C06).
    /// 필터를 바꾸면, 기본값 그대로이던 노출·ISO는 새 필터의 기본값으로 따라간다
    /// </summary>
    public string? ChangeSettings(NightContext n, ShootingPlan plan, string? filter, int? iso, int? exposure, int? totalMinutes)
    {
        if (plan.Settings is not { } s) return "대상을 먼저 골라 주세요.";
        var key = s.FilterKey ?? PlanRules.FilterKey(s.Filter);
        if (filter is not null)
        {
            // 모르는 필터는 조용히 기본 필터로 바꾸지 않는다 (Codex C06 원칙 — 2026-10-14 자체 리뷰)
            if (!PlanRules.Filters.Any(f => f.Key == filter || f.Label == filter))
                return $"필터는 {string.Join(", ", PlanRules.Filters.Select(f => f.Label))} 가운데에서 골라 주세요.";
            var newKey = PlanRules.FilterKey(filter);
            var (oldExposure, oldIso) = DefaultsFor(n, key);
            var (newExposure, newIso) = DefaultsFor(n, newKey);
            s = s with
            {
                Filter = PlanRules.FilterLabel(newKey), FilterKey = newKey,
                ExposureSeconds = s.ExposureSeconds == oldExposure ? newExposure : s.ExposureSeconds,
                Iso = s.Iso == oldIso ? newIso : s.Iso,
            };
            key = newKey;
        }
        if (iso is { } i)
        {
            if (PlanRules.IsStepIso(n.Rig.IsoMin, n.Rig.IsoMax))
            {
                if (!PlanRules.IsoOptions(n.Rig.IsoMin, n.Rig.IsoMax).Contains(i))
                    return $"이 카메라({n.Rig.Camera})는 ISO {string.Join(", ", PlanRules.IsoOptions(n.Rig.IsoMin, n.Rig.IsoMax))} 가운데에서 골라야 합니다.";
            }
            else if (n.Rig.IsoMin is { } lo && n.Rig.IsoMax is { } hi && (i < lo || i > hi))
                return $"이 카메라({n.Rig.Camera})의 게인은 {lo}~{hi}입니다.";
            s = s with { Iso = i };
        }
        if (exposure is { } e)
        {
            if (e < PlanRules.MinExposureSeconds || e > PlanRules.MaxExposureSeconds) return $"노출은 {PlanRules.MinExposureSeconds}~{PlanRules.MaxExposureSeconds}초로 정해 주세요.";
            s = s with { ExposureSeconds = e };
        }
        if (totalMinutes is { } m)
        {
            if (m < PlanRules.MinTotalMinutes || m > PlanRules.MaxTotalMinutes) return $"총 노출은 {PlanRules.MinTotalMinutes}~{PlanRules.MaxTotalMinutes}분으로 정해 주세요.";
            s = s with { TotalMinutes = m };
        }
        var (defaultExposure, defaultIso) = DefaultsFor(n, key);
        plan.Settings = s with { ExposureRecommended = s.ExposureSeconds == defaultExposure, IsoRecommended = s.Iso == defaultIso };
        if (plan.Asking == "settings") plan.Asking = null;
        Recount(plan, n, DateTimeOffset.Now);
        return null;
    }

    /// <summary>지금 대상의 촬영 시작을 옮긴다 (null = 기본으로). 범위 밖이면 범위 안으로 맞춘다(시간대 계산이 맞춤)</summary>
    public string? SetStart(string? id, DateTimeOffset? at, NightContext n, ShootingPlan plan)
    {
        var target = id is null ? plan.Target : plan.Picks.FirstOrDefault(p => p.Id == id);
        if (target is null) return "대상을 먼저 골라 주세요.";
        if (at is { } t) plan.StartOverrides[target.Id] = t;
        else plan.StartOverrides.Remove(target.Id);
        if (plan.Target?.Id == target.Id) Recount(plan, n, DateTimeOffset.Now);
        return null;
    }

    private string? SetStartTool(string? time, NightContext n, ShootingPlan plan)
    {
        if (string.IsNullOrWhiteSpace(time) || time.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)) return SetStart(null, null, n, plan);
        if (!TimeOnly.TryParse(time, out var tt)) return "시각은 HH:mm로 넣으세요.";
        return SetStart(null, AtLocal(n, tt), n, plan);
    }

    /// <summary>밤 기준 현지 시각: 정오 이전 시각이면 다음 날</summary>
    public static DateTimeOffset AtLocal(NightContext n, TimeOnly t)
    {
        var day = DateOnly.FromDateTime(n.Window.Sunset.DateTime);
        if (t.Hour < 12) day = day.AddDays(1);
        var local = day.ToDateTime(t);
        return new DateTimeOffset(local, n.Zone.GetUtcOffset(local));
    }

    // ── 찾기·추천 ─────────

    /// <summary>화면 검색창: 입력하면서 찾기, 오늘 밤 15° 위로 못 올라오는 대상은 뺀다</summary>
    public IReadOnlyList<Dso> Search(string query, NightContext n) =>
        catalog.Search(query, 12).Where(d => VisibleTonight(d.Position, n)).Take(8).ToList();

    private object Tonight(NightContext n, bool guiding)
    {
        var (fw, fh) = n.Rig.FieldOfView;
        return new
        {
            dark = new { start = Hm(n.Window.DarkStart), end = Hm(n.Window.DarkEnd), astronomical = n.Window.AstronomicalDark },
            sunset = Hm(n.Window.Sunset),
            sunrise = Hm(n.Window.Sunrise),
            moon = new
            {
                illumination_percent = (int)Math.Round(n.Moon.Illumination * 100),
                up = n.Moon.UpIntervals.Select(u => $"{Hm(u.Start)}-{Hm(u.End)}").ToList(),
            },
            clouds_during_dark = n.Clouds.Where(c => c.Time >= n.Window.DarkStart.AddHours(-1) && c.Time <= n.Window.DarkEnd)
                .Select(c => new { time = Hm(c.Time), cover_percent = c.Cover }).ToList(),
            clouds_available = n.Clouds.Count > 0,
            prep_minutes = (int)PlanRules.PrepLead.TotalMinutes,
            rig = new
            {
                telescope = n.Rig.Telescope,
                focal_length_mm = n.Rig.FocalLength,
                focal_ratio = n.Rig.FocalRatio,
                camera = n.Rig.Camera,
                field_of_view_degrees = $"{fw:F1} x {fh:F1}",
                pixel_scale_arcsec = Math.Round(n.Rig.PixelScale, 2),
                filter_wheel = n.Rig.HasFilterWheel,
                guiding,
            },
        };
    }

    private object Describe(Dso d, NightContext n)
    {
        var t = Night.Target(d.Position, n.Site, n.Window);
        var w = ShootWindow.Compute(AltitudeOf(d.Position.Ra, d.Position.Dec, n.Site), n.Window, DateTimeOffset.Now, null);
        return new
        {
            id = d.Id,
            name = d.Name,
            korean_name = KoreanNames.Of(d),
            common_name = d.CommonName,
            type = d.Type,
            constellation = d.Constellation,
            magnitude = d.Magnitude,
            size_arcmin = d.SizeArcmin is { } s ? Math.Round(s, 1) : (double?)null,
            fill_percent_of_frame = Fill(d.SizeArcmin, n.Rig),
            shootable = w is null ? "오늘 밤에는 찍을 수 있는 시간이 없음" : $"{Hm(w.Start)}-{Hm(w.Limit)} (시작 고도 {w.StartAltitude:F0}°)",
            highest = $"{Hm(t.HighestAt)} {t.HighestAltitude:F0}°",
            transit = Hm(t.Transit),
            moon_separation_degrees = Math.Round(t.MoonSeparation),
        };
    }

    private object SearchTool(string query, NightContext n)
    {
        var found = catalog.Find(query).Where(d => VisibleTonight(d.Position, n)).ToList();
        return found.Count == 0
            ? new { results = Array.Empty<object>(), note = "찾지 못했거나 오늘 밤 이 관측지에서 15° 위로 올라오지 않습니다. 번호(M, NGC, IC)나 영문 통칭으로 다시 찾아보세요." }
            : new { results = found.Select(d => Describe(d, n)).ToList(), note = "" };
    }

    private object Suggest(string kind, int limit, NightContext n)
    {
        var fovDeg = n.Rig.FieldOfView.Width;
        var bright = n.Moon.Illumination > 0.5;
        var scored = catalog.Showpieces()
            .Where(d => kind switch
            {
                "galaxy" => d.Type is "은하" or "은하단",
                "nebula" => d.Type.Contains("성운") || d.Type == "초신성 잔해",
                "cluster" => d.Type.Contains("성단"),
                _ => true,
            })
            .Select(d => (Dso: d, Night: Night.Target(d.Position, n.Site, n.Window)))
            .Where(x => x.Night.UsableStart is not null)
            .Select(x =>
            {
                var hours = (x.Night.UsableEnd!.Value - x.Night.UsableStart!.Value).TotalHours;
                var fill = (x.Dso.SizeArcmin ?? 1) / 60 / fovDeg; // 화면 긴 변 대비
                var fit = fill switch { < 0.05 => 0.2, < 0.15 => 0.6, <= 0.9 => 1.0, <= 1.3 => 0.7, _ => 0.3 };
                var moon = x.Night.MoonSeparation < 30 && bright ? 0.5 : 1.0;
                var mag = x.Dso.Magnitude is { } m ? Math.Clamp((12 - m) / 8, 0.2, 1) : 0.5;
                return (x.Dso, Score: hours * fit * moon * mag);
            })
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(limit, 1, 8))
            .Select(x => Describe(x.Dso, n))
            .ToList();
        return new { suggestions = scored, moon_bright = bright };
    }

    private static object Offer(JsonElement a, ShootingPlan plan, out IReadOnlyList<string>? choices)
    {
        if (Str(a, "field") is { } field) plan.Asking = field;
        choices = a.TryGetProperty("choices", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Take(4).ToList()
            : [];
        return new { ok = true, note = "선택지를 화면에 보여 주었습니다. 이제 질문 글을 쓰고 사용자의 답을 기다리세요." };
    }

    private static string? Str(JsonElement a, string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement a, string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : null;
}
