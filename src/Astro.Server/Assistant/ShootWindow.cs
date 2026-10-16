using Astro.Core.Sky;

namespace Astro.Server.Assistant;

/// <summary>촬영 시작이 늦어지는 까닭: 없음 · 어두워지기를 · 대상이 올라오기를 · 사용자가 시작을 옮김</summary>
public enum ShootWait { None, Dark, Altitude, Chosen }

/// <summary>끝을 막는 것: 새벽(천문 박명) · 대상이 낮아짐</summary>
public enum ShootStop { Dawn, Low }

/// <summary>
/// 대상 하나의 오늘 밤 촬영 시간대 (OPERATION_PROCEDURE.md "계획", 2026-10-14 사용자 결정).
/// 촬영 시작 = 이동 + 준비 시간(Lead — 프로필의 준비 시간 기록, 없으면 PlanRules.DefaultPrepLead). 이동은 대상이 15° 이상일 때만.
/// 기본 시작은 가장 이른 시작 뒤 30°가 되는 때, 사용자가 시작점을 옮겼으면 그때(끌 수 있는 범위 안).
/// 끝은 시작 고도와 30° 중 낮은 쪽 아래로 내려갈 때 또는 새벽.
/// </summary>
public sealed record ShootWindow(
    DateTimeOffset Start, DateTimeOffset Limit, double StartAltitude, double EndAltitude,
    ShootWait Wait, ShootStop Stop, bool Moved,
    /// <summary>시작점을 옮길 수 있는 범위</summary>
    DateTimeOffset DragFrom, DateTimeOffset DragTo,
    /// <summary>자오선 통과 (시작~끝 사이에 있을 때만)</summary>
    DateTimeOffset? Transit,
    /// <summary>준비 시간</summary>
    TimeSpan Lead)
{
    /// <summary>적도의를 옮기는 시각 (촬영 시작 − 준비 시간)</summary>
    public DateTimeOffset MoveAt => Start - Lead;

    private static readonly TimeSpan Step = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 계산. 오늘 밤 찍을 수 없으면 null (어두운 시간에 15° 위로 못 올라오거나, 시간이 남지 않음).
    /// chosenStart: 사용자가 옮긴 시작 — 범위 밖이면 범위 안으로 맞춘다. prepLead: 준비 시간 (없으면 기본값)
    /// </summary>
    public static ShootWindow? Compute(Func<DateTimeOffset, double> altitude, NightWindow night, DateTimeOffset now, DateTimeOffset? chosenStart, TimeSpan? prepLead = null)
    {
        var lead = prepLead ?? PlanRules.DefaultPrepLead;
        var floor = PlanRules.LowestAltitude;
        var dusk = night.DarkStart;
        var dawn = night.DarkEnd;
        var ready = Ceil(now + lead);
        var earliest = ready > dusk ? ready : dusk;

        // 시작할 수 있는 때: 어두운 시간 안, 그때 대상이 15° 이상이고 준비를 시작할 때(이동)도 15° 이상
        bool CanStart(DateTimeOffset t) => t <= dawn && altitude(t) >= floor && altitude(t - lead) >= floor;

        var from = earliest;
        while (from <= dawn && !CanStart(from)) from += Step;
        if (from > dawn) return null;

        // 15° 이상이 이어지는 끝 (시작점을 옮길 수 있는 마지막 = 그 끝 − 여유)
        var run = from;
        while (run + Step <= dawn && altitude(run + Step) >= floor) run += Step;
        var dragTo = run - PlanRules.EndMargin;
        if (dragTo < from) dragTo = from;

        // 기본 시작: 가장 이른 시작 뒤 처음으로 30° 이상 (없으면 15° 구간의 시작 — 낮게만 지나가는 대상)
        var auto = from;
        while (auto <= dragTo && altitude(auto) < PlanRules.MinAltitude) auto += Step;
        var reachesMin = auto <= dragTo;
        if (!reachesMin) auto = from;

        var moved = chosenStart is not null;
        var start = moved ? Clamp(Round(chosenStart!.Value), from, dragTo) : auto;
        var startAlt = altitude(start);
        // 끝: 시작 고도와 30° 중 낮은 쪽 (시작을 30°보다 낮게 잡았으면 내려갈 때도 그만큼까지, 높게 잡아도 끝은 30°).
        // 다만 내려가는 쪽에서 30°보다 낮게 시작하면 그 고도가 곧 끝이라 찍을 시간이 없어진다 → 그때는 가장 낮은 고도(15°)까지
        // (2026-10-14 자체 리뷰: 지는 쪽 28°로 시작점을 끌면 시간대가 사라지고 손잡이·되돌리기도 함께 사라졌다)
        var rising = altitude(start + Step) > startAlt;
        var endAlt = Math.Min(PlanRules.MinAltitude, startAlt);
        if (!reachesMin && !moved) endAlt = Math.Min(endAlt, floor);
        if (!rising && startAlt < PlanRules.MinAltitude) endAlt = floor;
        var limit = start;
        while (limit + Step <= dawn && altitude(limit + Step) >= endAlt - 0.01) limit += Step;
        if (limit <= start) return null;
        var stop = limit + Step > dawn ? ShootStop.Dawn : ShootStop.Low;

        // 기다림의 까닭 (화면 문장·버튼 이름이 다르다)
        var wait = start > earliest ? (moved ? ShootWait.Chosen : ShootWait.Altitude)
            : earliest > ready ? ShootWait.Dark
            : ShootWait.None;

        return new ShootWindow(start, limit, Math.Round(startAlt, 1), Math.Round(endAlt, 1), wait, stop, moved,
            from, dragTo, TransitBetween(altitude, start, limit), lead);
    }

    /// <summary>자오선 통과: 고도가 오르다 내리기 시작하는 때 (시작~끝 사이)</summary>
    private static DateTimeOffset? TransitBetween(Func<DateTimeOffset, double> altitude, DateTimeOffset from, DateTimeOffset to)
    {
        var prev = altitude(from);
        for (var t = from + Step; t <= to; t += Step)
        {
            var a = altitude(t);
            var next = altitude(t + Step);
            if (a >= prev && a > next) return t;
            prev = a;
        }
        return null;
    }

    private static DateTimeOffset Round(DateTimeOffset t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);
    private static DateTimeOffset Ceil(DateTimeOffset t) => t.Second == 0 && t.Millisecond == 0 ? t : Round(t).AddMinutes(1);
    private static DateTimeOffset Clamp(DateTimeOffset t, DateTimeOffset lo, DateTimeOffset hi) => t < lo ? lo : t > hi ? hi : t;
}

/// <summary>촬영 설정과 시간대로 계산한 장수·끝 시각</summary>
public sealed record ShootAmount(
    /// <summary>목표 장수 = 총 노출 ÷ 한 장 노출</summary>
    int Frames,
    /// <summary>오늘 찍을 수 있는 최대 장수 (시간대 안)</summary>
    int MaxFrames,
    /// <summary>목표를 다 못 채움 → 끝은 Limit</summary>
    bool Clipped,
    /// <summary>다 채우는 밤의 예상 끝 (못 채우면 Limit)</summary>
    DateTimeOffset End,
    /// <summary>한 장에 걸리는 시간(초) = 노출 + 쉬는 시간(카메라별 — Rig.FrameOverheadSeconds)</summary>
    double SecondsPerFrame)
{
    /// <summary>실제로 찍을 장수 (준비·촬영 단계가 쓰는 계획 장수)</summary>
    public int Planned => Clipped ? MaxFrames : Frames;

    public static ShootAmount Of(ShootWindow w, int exposureSeconds, int totalMinutes, double overheadSeconds = PlanRules.FrameOverheadSeconds)
    {
        var exposure = Math.Max(PlanRules.MinExposureSeconds, exposureSeconds);
        var frames = Math.Max(1, (int)Math.Round(totalMinutes * 60.0 / exposure));
        var per = exposure + overheadSeconds;
        // 자오선 반전 쉼은 찍는 동안 자오선을 지날 때만 더한다 (그 전에 다 찍으면 반전이 없다 — 2026-10-14 자체 리뷰)
        var plain = w.Start + TimeSpan.FromSeconds(frames * per);
        var flip = w.Transit is { } tr && tr < plain ? PlanRules.FlipPause : TimeSpan.Zero;
        var usable = Math.Max(0, (w.Limit - w.Start - (w.Transit is not null ? PlanRules.FlipPause : TimeSpan.Zero)).TotalSeconds);
        var max = (int)(usable / per);
        var end = plain + flip;
        var clipped = end > w.Limit;
        return new ShootAmount(frames, max, clipped, clipped ? w.Limit : end, per);
    }
}
