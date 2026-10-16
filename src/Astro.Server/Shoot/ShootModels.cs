using System.Text.Json.Serialization;
using Astro.Server.Prepare.Flow;

namespace Astro.Server.Shoot;

/// <summary>그날 밤 대상 하나의 촬영 기록 (마무리의 다크·요약이 읽는다)</summary>
/// <summary>PausedMinutes = 원인별 멈춘 시간(분): cloud · light · jump · guider …,
/// NotMoved = 제외할 사진인데 제외 폴더로 옮기지 못한 파일 (사용자가 직접 옮김, CX-APP-R2),
/// PlanKey = 어느 확정 계획의 촬영인지 (AA를 다시 켜 이어서 찍을 때 같은 줄에 이어 센다)</summary>
public sealed record TargetShots(string TargetId, string Name, int Good, int Excluded, int ExposureSeconds, int Iso, IReadOnlyDictionary<string, int> Tally, string? Folder,
    IReadOnlyDictionary<string, double>? PausedMinutes = null, IReadOnlyList<string>? NotMoved = null, string? PlanKey = null);

/// <summary>그날 밤 찍은 대상들 (결과 기록 — 촬영이 갱신)</summary>
public sealed record NightShootResult(IReadOnlyList<TargetShots> Targets);

/// <summary>촬영 화면 상태. 화면은 이것만 보고 그린다</summary>
public sealed record ShootView(
    bool Started, string? Target, int Planned, int Good, int Excluded, int ExposureSeconds, int Iso, int Elapsed,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShootMode>))] ShootMode Mode,
    int FlipStep, DateTimeOffset? FlipAt, int? FlipInMinutes, IReadOnlyList<double> Guide, IReadOnlyList<double> Hfr, double FocusHfr,
    double PixelScale, string? LastGrade, IReadOnlyDictionary<string, int> Tally, IReadOnlyDictionary<string, string> Criteria,
    StatusLine? Note, IReadOnlyList<object>? FocusPoints, string? Ended, DateTimeOffset? EndAt, string? PhotoUrl, DateTimeOffset? PhotoAt,
    int Version, bool Simulated, string? Pause = null, int PausedSeconds = 0, int GuideExposureMs = 0, bool GuideStopped = true, string? Ask = null,
    DewStatus? Dew = null, bool MountStopped = true);

/// <summary>Paused = 가이드 별을 잃어 멈춤 (원인은 ShootView.Pause: light · cloud · jump · guider · unstable)</summary>
public enum ShootMode { Idle, Shoot, Dither, Flip, Focus, Paused, Finishing, Ended }
