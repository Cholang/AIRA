using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Astro.Core;
using Astro.Core.Assistant;
using Astro.Core.Sky;
using Astro.Server.Assistant.Providers;
using Astro.Server.Sky;
using Microsoft.Extensions.Options;

namespace Astro.Server.Assistant;

/// <summary>
/// 촬영 계획 대화 (한 사용자, 한 밤). AI에게 도구를 쥐여 주고, 도구 호출 → 실행 → 결과 돌려주기를 반복한다.
/// 이 반복·계획 상태·화면 이벤트는 AI 회사와 무관하다.
/// </summary>
public sealed partial class PlanAssistant(ChatModelFactory models, PlanTools tools, TonightService tonight, ILogger<PlanAssistant> log,
    Prepare.PrepTimes? prepTimes = null, Profiles.ProfileStore? profiles = null)
{
    private const int MaxRounds = 8;

    /// <summary>도구 이름 → 화면에 보일 "지금 하는 일"</summary>
    private static string Doing(string tool) => tool switch
    {
        "get_tonight" => "오늘 밤 조건을 보는 중…",
        "search_targets" or "suggest_targets" => "대상 후보를 찾는 중…",
        "pick_target" or "set_target" or "remove_target" => "대상을 정리하는 중…",
        "set_settings" or "set_start" or "recommend_settings" => "촬영 설정을 계산하는 중…",
        _ => "답을 정리하는 중…",
    };

    /// <summary>계획을 고치는 곳이 둘(화면·AI 도구)이라 한 번에 하나씩</summary>
    private readonly Lock _planGate = new();

    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly List<ChatMessage> _history = [];
    private readonly List<DisplayMessage> _display = [];
    private ShootingPlan _plan = new();
    private NightContext? _night;
    private DateOnly _evening;
    /// <summary>AI 한도·키 문제로 이름 검색만 쓰는 동안, 한도가 다시 차는 시각까지</summary>
    private DateTimeOffset _practiceUntil = DateTimeOffset.MinValue;
    private string _practiceReason = "";
    private string? _profileId;

    public ShootingPlan Plan => _plan;

    /// <summary>"이 계획으로 준비 시작"으로 확정한 계획. 준비 단계가 이것을 읽는다 (없으면 null)</summary>
    public PreparationPlan? Confirmed { get; private set; }

    /// <summary>지금 계획을 확정해 준비 단계로 넘긴다. 실행할 수 없는 계획이면 이유를 돌려준다. 시간대는 지금 시각으로 다시 계산한 값</summary>
    public string? Confirm()
    {
        PreparationPlan? plan;
        string? problem;
        lock (_planGate)
        {
            if (_night is { } n) PlanTools.Recount(_plan, n, DateTimeOffset.Now);
            (plan, problem) = PreparationPlan.From(_plan, DateTimeOffset.Now, _night?.PrepLead);
        }
        if (plan is null) return problem;
        Confirmed = plan;
        log.LogInformation("계획 확정: {Plan}", plan);
        return null;
    }
    public NightContext? Night => _night;

    /// <summary>
    /// 대상 준비가 노출을 바꾸면(가이드 없이 줄이기·노출 맞추기 — PrepContext.ChangeExposure) 확정 계획과 계획 칸에 반영한다 (CX-40-04).
    /// 확정 시각은 그대로 — 촬영의 누적 장수가 이것으로 이어진다
    /// </summary>
    public void AdjustConfirmed(int exposureSeconds, int estimatedFrames)
    {
        if (Confirmed is not { } c) return;
        Confirmed = c with { ExposureSeconds = exposureSeconds, EstimatedFrames = estimatedFrames };
        lock (_planGate)
            if (_plan.Settings is { } s && _plan.Target?.Id == c.TargetId)
                _plan.Settings = s with { ExposureSeconds = exposureSeconds, EstimatedFrames = estimatedFrames };
        log.LogInformation("확정 계획의 노출을 {Seconds}초 · {Frames}장으로 고침 (대상 준비에서 바꿈)", exposureSeconds, estimatedFrames);
    }

    /// <summary>지금 프로필의 준비 시간 (기록이 없으면 기본값)</summary>
    private TimeSpan PrepLead() => prepTimes?.Lead(profiles?.Current()?.Id) ?? PlanRules.DefaultPrepLead;

    /// <summary>
    /// [임시] 건너뛰기 (2026-10-14 — 장비 없이 모든 단계를 넘겨 볼 때): 지금 대상이 없으면 오늘 밤 찍을 수 있는 대표 대상 하나를 골라 확정한다. 안 되면 이유.
    /// 지울 때: 이 메서드 · AppServer의 /plan/temp-skip · 웹 App의 계획 건너뛰기
    /// </summary>
    public string? TempSkip()
    {
        bool none;
        lock (_planGate) none = _plan.Target is null;
        if (none)
        {
            if (_night is not { } n) return "오늘 밤 정보가 아직 없습니다. 화면을 다시 열어 주세요.";
            var picked = false;
            foreach (var name in new[] { "M31", "M45", "M42", "M33", "M13", "M57", "M27", "M8", "M20", "M51" })
                if (tools.Search(name, n).FirstOrDefault() is { } d && Pick(d.Id) is null) { picked = true; break; }
            if (!picked) return "오늘 밤 찍을 수 있는 대상을 찾지 못했습니다";
        }
        return Confirm();
    }

    /// <summary>
    /// AA를 다시 켜 "이어서"를 골랐을 때 (Session/NightSession): 오늘 밤 정보를 다시 만들고 확정 계획·계획 칸을 되살린다.
    /// 대화 기록은 되살리지 않는다(첫 인사부터) — 계획 칸과 확정 계획만
    /// </summary>
    public async Task RestoreAsync(PreparationPlan confirmed, ShootingPlan? plan, CancellationToken ct)
    {
        await EnsureStartedAsync(ct);
        if (plan is not null) _plan = plan;
        Confirmed = confirmed;
        log.LogInformation("이어서: 계획 되살림 {Plan}", confirmed);
    }
    public IReadOnlyList<DisplayMessage> Display => _display;

    /// <summary>
    /// 화면을 열 때·말할 때마다: 오늘 밤 정보를 준비하고, 처음이면 인사를 만든다. 날짜·관측지가 바뀌면 새로 시작.
    /// 같은 밤에 장비(망원경·카메라 등)만 바뀌었으면 대화는 두고 장비에 달린 값만 고친다 (Codex 리뷰 CX-PLAN-06)
    /// </summary>
    public async Task<string?> EnsureStartedAsync(CancellationToken ct)
    {
        var evening = TonightService.EveningOf(DateTimeOffset.Now);
        var read = await tonight.ReadProfileAsync(ct);
        var lead = PrepLead();
        if (_night is not null && _evening == evening)
        {
            // 준비 시간은 그 사이 기록(앞 대상)이나 프로필이 바뀌었을 수 있어 열 때마다 다시 읽는다
            if (_night.PrepLead != lead) lock (_planGate) _night = _night with { PrepLead = lead };
            // N.I.N.A.를 잠깐 못 읽었거나 위치가 비어 있으면 하던 대화는 그대로 (다음 번에 다시 확인)
            if (read is not { } now || now.Site is { Latitude: 0, Longitude: 0 }) return null;
            if (now.Site == _night.Site)
            {
                if (now.Rig != _night.Rig) ApplyRig(now.Rig);
                return null;
            }
            // 관측지가 바뀌었으면 시각·고도가 모두 달라지므로 새로 시작 (아래)
        }

        if (read is not { } profile)
            return "N.I.N.A. 프로필을 읽지 못했습니다. N.I.N.A.가 켜져 있는지 확인해 주세요.";
        if (profile.Site is { Latitude: 0, Longitude: 0 })
            return "N.I.N.A. 프로필에 관측지 위치가 없습니다. N.I.N.A.의 옵션 > 일반 > 천문 설정에서 위도·경도를 넣어 주세요.";

        var zone = TimeZoneInfo.Local;
        var window = Core.Sky.Night.Window(profile.Site, evening, zone);
        var moon = Core.Sky.Night.Moon(profile.Site, window);
        var clouds = await tonight.CloudsAsync(profile.Site, ct);
        _night = new NightContext(profile.Site, profile.Rig, window, moon, clouds, zone) { PrepLead = lead };
        _evening = evening;
        _history.Clear();
        _display.Clear();
        _plan = new ShootingPlan { Asking = "target" };
        // AI가 없으면 대화 없이 이름 검색만 (2026-10-14 사용자 결정) — 인사도 두지 않는다. 오늘 밤 한 줄은 화면 오른쪽에 늘 있다
        if (PracticeReason is null) _display.Add(new DisplayMessage("assistant", Greeting, ["오늘 뭐가 좋을까?"]));
        return null;
    }

    /// <summary>방금 받은 사용자 말을 기록에서 뺀다 (답하지 못한 말 — 다시 보내면 처음처럼)</summary>
    private void ForgetLastUserMessage()
    {
        if (_history.Count > 0 && _history[^1].Role == ChatRole.User) _history.RemoveAt(_history.Count - 1);
        if (_display.Count > 0 && _display[^1].Role == "user") _display.RemoveAt(_display.Count - 1);
    }

    /// <summary>AI를 쓸 수 없는 이유 (설정·키 없음·오늘 한도). 쓸 수 있으면 null — 화면은 이때 대화 대신 이름 검색만</summary>
    public string? PracticeReason => models.PracticeReason ?? (DateTimeOffset.Now < _practiceUntil ? _practiceReason : null);

    // ── 화면에서 계획 고치기 (AI 도구와 같은 함수). 문제가 있으면 사용자에게 보여 줄 문장 ─────────

    public string? Pick(string id) => Edit((p, n) => tools.Pick(id, null, n, p));
    public string? Remove(string id) => Edit((p, n) => tools.Remove(id, n, p));
    public string? ChangeSettings(string? filter, int? iso, int? exposure, int? totalMinutes) =>
        Edit((p, n) => tools.ChangeSettings(n, p, filter, iso, exposure, totalMinutes));
    /// <summary>시작점 옮기기: 밤 시간축의 분(일몰부터) — null이면 기본으로</summary>
    public string? SetStart(string id, double? minuteFromSunset) =>
        Edit((p, n) => tools.SetStart(id, minuteFromSunset is { } m ? n.Window.Sunset.AddMinutes(m) : null, n, p));

    private string? Edit(Func<ShootingPlan, NightContext, string?> change)
    {
        lock (_planGate)
        {
            if (_night is not { } n) return "오늘 밤 정보가 아직 없습니다. 화면을 다시 열어 주세요.";
            return change(_plan, n);
        }
    }

    /// <summary>화면에 보낼 계획 (지금 시각으로 시간대를 다시 계산)</summary>
    public object View()
    {
        lock (_planGate)
        {
            var n = _night!;
            PlanTools.Recount(_plan, n, DateTimeOffset.Now);
            return PlanView.From(_plan, n, tools, DateTimeOffset.Now);
        }
    }

    /// <summary>그래프 자료 (지금 시각 기준)</summary>
    public object Chart()
    {
        lock (_planGate) return NightChart.Build(_night!, _plan, DateTimeOffset.Now);
    }

    /// <summary>화면 검색창: 입력하면서 찾기 (오늘 밤 못 찍는 대상은 뺌)</summary>
    public IReadOnlyList<object> Search(string query)
    {
        if (_night is not { } n) return [];
        return tools.Search(query, n).Select(d => (object)new
        {
            id = d.Id, name = d.Name, koreanName = Sky.KoreanNames.Of(d), commonName = d.CommonName, type = d.Type, constellation = d.Constellation,
        }).ToList();
    }

    /// <summary>오늘 밤 한 줄 (화면 오른쪽 위, 늘): 어두운 시간 · 달 · 구름</summary>
    public string NightLine => _night is { } n ? NightSummary(n) : "";

    public void Reset()
    {
        _night = null;
    }

    /// <summary>프로필을 고를 때: 다른 프로필이면 계획 대화를 새로 시작한다 (다른 사람의 계획이 섞이지 않게). 같은 프로필이면 그대로</summary>
    public void ProfileSelected(string id)
    {
        if (_profileId is not null && _profileId != id) Reset();
        _profileId = id;
    }

    /// <summary>
    /// 같은 밤에 장비만 바뀌었을 때: 대상(시각·고도)은 그대로, 화각에 달린 구도의 채움 비율은 다시 계산.
    /// 기본 노출·ISO에 쓰는 것(F값·카메라·가이딩)이 바뀌었으면 기본값 그대로이던 설정만 새 기본값으로 (사용자가 고친 값은 둔다)
    /// </summary>
    private void ApplyRig(Rig rig)
    {
        lock (_planGate)
        {
            var old = _night!;
            var oldDefaults = _plan.Settings is { } s0 ? tools.DefaultsFor(old, s0.FilterKey ?? PlanRules.FilterKey(s0.Filter)) : default;
            _night = old with { Rig = rig };
            if (_plan.Framing is { } f && _plan.Target is { } t) _plan.Framing = f with { FillPercent = PlanTools.Fill(t.SizeArcmin, rig) };
            if (_plan.Settings is { } s)
            {
                var (exposure, iso) = tools.DefaultsFor(_night, s.FilterKey ?? PlanRules.FilterKey(s.Filter));
                _plan.Settings = s with
                {
                    ExposureSeconds = s.ExposureSeconds == oldDefaults.ExposureSeconds ? exposure : s.ExposureSeconds,
                    Iso = s.Iso == oldDefaults.Iso ? iso : s.Iso,
                };
                PlanTools.Recount(_plan, _night, DateTimeOffset.Now);
            }
            log.LogInformation("계획 중 장비가 바뀜: {Old} → {New}", old.Rig, rig);
            _display.Add(new DisplayMessage("notice", "장비가 바뀌어 화각과 기본 설정을 다시 계산했어요."));
        }
    }
}
