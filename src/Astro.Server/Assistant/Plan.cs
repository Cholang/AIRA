using System.Text.Json.Serialization;

namespace Astro.Server.Assistant;

// 촬영 계획 (OPERATION_PROCEDURE.md "계획", 2026-10-14 개편): 계획 = 무엇을(대상) + 얼마나(촬영 설정).
// 사용자는 화면에서 직접, AI는 도구로 같은 계획을 고친다. 구도·끝난 뒤는 묻지 않고 기본값으로 채운다(준비 단계가 읽는 모양은 그대로).
// 칸에는 실행에 쓰는 값(좌표·종료 규칙·끝난 뒤 동작)을 두고, 화면 문장은 그 값에서 만든다 (Codex 리뷰 CX-PLAN-04·07)

public sealed class ShootingPlan
{
    /// <summary>지금 대상 (Picks 가운데 하나). 설정·확정은 이 대상으로</summary>
    public PlanTarget? Target { get; set; }

    /// <summary>그래프에 함께 그리는 고른 대상들. 먼저 고른 것부터, 다시 고르면 맨 뒤(가장 최근)로, 최대 PlanRules.MaxPicks</summary>
    public List<PlanTarget> Picks { get; set; } = [];

    /// <summary>사용자가 시작점을 옮긴 대상 (대상 id → 촬영 시작). 없으면 기본 시작</summary>
    public Dictionary<string, DateTimeOffset> StartOverrides { get; set; } = [];

    public PlanFraming? Framing { get; set; }
    public PlanSettings? Settings { get; set; }
    public PlanAfter? After { get; set; }

    /// <summary>지금 묻는 것 (예전 칸 이름: target · settings). 화면 강조용</summary>
    public string? Asking { get; set; }

    /// <summary>대상과 설정이 정해졌다. 실제로 찍을 수 있는지는 확정할 때·준비 단계에서 확인한다 (CX-PLAN-05)</summary>
    public bool Complete => Target is not null && Framing is not null && Settings is not null && After is not null;
}

public sealed record PlanTarget(
    string Id, string Name, string? CommonName, string? KoreanName, string Type, string Constellation,
    double? SizeArcmin, double? Magnitude,
    /// <summary>J2000 적경·적위(도). 준비 단계에서 이 좌표로 이동·센터링한다</summary>
    double RaDegrees, double DecDegrees,
    DateTimeOffset? UsableStart, DateTimeOffset? UsableEnd,
    DateTimeOffset HighestAt, double HighestAltitude, DateTimeOffset? Transit, double MoonSeparation);

public sealed record PlanFraming(
    /// <summary>가운데 · 조금 비켜서 등 (AI가 사용자 말로 적음). V0.1에서 실행은 항상 가운데 맞추기, 이 말은 참고로만 넘긴다</summary>
    string Placement,
    /// <summary>카메라 방향(도, 사진 위쪽이 북쪽 = 0). null = 지금 카메라 방향 유지 (기본, CX-PLAN-02). 모른다고 0으로 채우지 않는다</summary>
    double? RotationDegrees,
    /// <summary>대상이 화면 긴 변에서 차지하는 비율(%)</summary>
    double? FillPercent);

/// <summary>촬영을 끝내는 규칙. 끝나는 시각(End)은 이 규칙으로 계산한다</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EndRule>))]
public enum EndRule
{
    /// <summary>대상이 30° 아래로 내려가거나 새벽이 올 때 (먼저 오는 것)</summary>
    TargetLowOrDawn,
    /// <summary>새벽(천문 박명)이 올 때</summary>
    Dawn,
    /// <summary>사용자가 정한 시각</summary>
    AtTime,
}

public sealed record PlanSettings(
    int ExposureSeconds, int Iso, string Filter,
    EndRule EndRule,
    /// <summary>촬영 시작과 물리 한계(새벽 또는 대상이 EndAltitude 아래로). 계획 장수 = 총 노출 ÷ 노출, 시간대 안에 다 못 찍으면 그 최대</summary>
    DateTimeOffset Start, DateTimeOffset End, int EstimatedFrames,
    bool ExposureRecommended, bool IsoRecommended,
    /// <summary>총 노출(분) = 노출 × 장수 (쉬는 시간은 끝 시각에만)</summary>
    int TotalMinutes = PlanRules.DefaultTotalMinutes,
    /// <summary>필터 키 (PlanRules.Filters). Filter는 화면 이름</summary>
    string? FilterKey = null,
    /// <summary>촬영을 끝내는 대상 고도 = 시작 고도와 30° 중 낮은 쪽</summary>
    double EndAltitude = PlanRules.MinAltitude)
{
    /// <summary>화면용 문장</summary>
    public string EndCondition => EndRule switch
    {
        EndRule.Dawn => "새벽이 올 때까지",
        EndRule.AtTime => $"{End:HH:mm}까지",
        _ => "대상이 낮아지거나 새벽이 올 때까지",
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<CalibrationFrames>))]
public enum CalibrationFrames { None, Darks, Flats, DarksAndFlats }

public sealed record PlanAfter(bool Park, CalibrationFrames Calibration, bool Disconnect)
{
    public string MountText => Park ? "파킹" : "그대로 둠";
    public string CalibrationText => Calibration switch
    {
        CalibrationFrames.Darks => "다크 찍기",
        CalibrationFrames.Flats => "플랫 찍기",
        CalibrationFrames.DarksAndFlats => "다크·플랫 찍기",
        _ => "찍지 않음",
    };
    public string EquipmentText => Disconnect ? "연결 해제" : "연결 유지";
}

/// <summary>
/// 준비 단계에 넘기는 확정 계획 (CX-PLAN-07). "이 계획으로 준비 시작"을 누를 때 한 번 만들고 그 뒤로 바뀌지 않는다.
/// 화면 문장이나 AI의 말이 아니라 실행에 쓰는 값만 담는다. 이 값이 맞는지(장비가 할 수 있는지)는 준비 단계가 확인한다.
/// </summary>
public sealed record PreparationPlan(
    string TargetId, string TargetName, string? TargetKoreanName,
    double RaDegrees, double DecDegrees,
    /// <summary>V0.1은 항상 대상을 가운데로 맞춘다. 사용자가 말한 배치는 참고 문장으로만</summary>
    string PlacementNote,
    /// <summary>null = 지금 카메라 방향 유지</summary>
    double? RotationDegrees,
    int ExposureSeconds, int Iso, string Filter,
    EndRule EndRule, DateTimeOffset Start, DateTimeOffset End, int EstimatedFrames,
    bool Park, CalibrationFrames Calibration, bool Disconnect,
    DateTimeOffset ConfirmedAt,
    /// <summary>
    /// 촬영을 끝내는 대상 고도 (계획에서 시작점을 30°보다 낮게 잡았으면 그 고도, 2026-10-14). 이동 단계는 Start − 준비 시간에 옮기고,
    /// 촬영 단계는 대상이 이 고도 아래로 내려가면 끝낸다. 옛 기록에는 없으므로 기본 30°
    /// </summary>
    double MinAltitude = PlanRules.MinAltitude,
    /// <summary>계획에서 쓴 준비 시간(분) — 이동 단계는 Start − 이만큼에 옮긴다. 옛 기록에는 없으므로 기본값</summary>
    double PrepMinutes = PlanRules.DefaultPrepMinutes)
{
    public TimeSpan PrepLead => TimeSpan.FromMinutes(PrepMinutes);

    /// <summary>계획에서 만든다. 실행할 수 없는 계획이면 이유(사용자에게 보여 줄 문장)를 돌려준다. 시간대는 확정 직전에 다시 계산해 둔 값</summary>
    public static (PreparationPlan? Plan, string? Problem) From(ShootingPlan p, DateTimeOffset now, TimeSpan? prepLead = null)
    {
        if (p is not { Target: { } t, Framing: { } f, Settings: { } s, After: { } a })
            return (null, "대상을 먼저 골라 주세요.");
        var name = t.KoreanName is null ? t.Name : $"{t.Name} {t.KoreanName}";
        if (s.End <= s.Start || s.EstimatedFrames < 1)
            return (null, $"{name}{Core.Josa.Pick(name, "은", "는")} 오늘 밤 찍을 수 있는 시간이 없습니다. 다른 대상을 골라 주세요.");
        if (s.End <= now)
            return (null, $"찍을 수 있는 시간({s.End:HH:mm}까지)이 이미 지났습니다. 다른 대상을 골라 주세요.");
        return (new PreparationPlan(t.Id, t.Name, t.KoreanName, t.RaDegrees, t.DecDegrees,
            f.Placement, f.RotationDegrees,
            s.ExposureSeconds, s.Iso, s.Filter, s.EndRule, s.Start, s.End, s.EstimatedFrames,
            a.Park, a.Calibration, a.Disconnect, now, s.EndAltitude, (prepLead ?? PlanRules.DefaultPrepLead).TotalMinutes), null);
    }
}
