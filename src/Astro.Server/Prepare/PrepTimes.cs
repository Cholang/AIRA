using System.Text.Json;
using Astro.Core;
using Astro.Server.Assistant;
using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare;

/// <summary>
/// 준비 시간 기록 (OPERATION_PROCEDURE.md "계획", 2026-10-16 사용자 결정): 대상으로 이동을 시작한 때부터 시험 사진을 마친 때까지.
/// 계획의 준비 시간 = 프로필마다 최근 PlanRules.PrepSamples번의 중앙값 (실패해 오래 걸린 한 번에 끌려가지 않게), 기록이 없으면 기본값.
/// 데이터 폴더의 prep-times.json
/// </summary>
public sealed class PrepTimes
{
    /// <summary>프로필마다 남기는 기록 수 (계산에는 최근 PrepSamples번만)</summary>
    private const int Keep = 20;
    /// <summary>이보다 짧으면 준비를 한 것이 아니다 (기록하지 않음)</summary>
    private static readonly TimeSpan Shortest = TimeSpan.FromMinutes(1);

    private readonly string _file;
    private readonly Lock _gate = new();
    private List<Entry> _entries;

    private sealed record Entry(string Profile, DateTimeOffset At, double Minutes);

    public PrepTimes(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        _file = Path.Combine(root, "prep-times.json");
        _entries = Load();
    }

    /// <summary>계획에 쓸 준비 시간 (분 단위로 올림)</summary>
    public TimeSpan Lead(string? profileId)
    {
        List<double> recent;
        lock (_gate) recent = [.. _entries.Where(e => e.Profile == Key(profileId)).TakeLast(PlanRules.PrepSamples).Select(e => e.Minutes)];
        return recent.Count == 0 ? PlanRules.DefaultPrepLead : TimeSpan.FromMinutes(Math.Ceiling(Median(recent)));
    }

    public void Record(string? profileId, TimeSpan took, DateTimeOffset at)
    {
        if (took < Shortest) return;
        lock (_gate)
        {
            _entries.Add(new Entry(Key(profileId), at, Math.Round(took.TotalMinutes, 1)));
            _entries = [.. _entries.GroupBy(e => e.Profile).SelectMany(g => g.TakeLast(Keep)).OrderBy(e => e.At)];
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllText(_file, JsonSerializer.Serialize(_entries));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static string Key(string? profileId) => profileId ?? "";

    private List<Entry> Load()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_file)) ?? [] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}

/// <summary>
/// 대상 묶음의 작업이 실제로 끝날 때마다 받아(PrepareRunner.TaskFinished), 이동부터 시험 사진까지 그 밤에 할 작업이 모두
/// 실제로 끝났으면 걸린 시간을 남긴다. "지금 위치를 목표로"(이동 없음)·[임시] 건너뛰기·이어서 하기로 넘긴 작업이 있으면 남기지 않는다
/// (그런 작업은 여기로 오지 않는다). 모의 장비일 때는 만들지 않는다
/// </summary>
public sealed class PrepTimeRecorder(PrepTimes times, Func<string?> profileId)
{
    private DateTimeOffset? _movedAt;
    private IReadOnlySet<string> _needed = new HashSet<string>();
    private readonly HashSet<string> _done = [];

    public void Finished(IPrepTask task, object result, PrepContext ctx, IReadOnlyList<IPrepTask> group)
    {
        if (task.Id == "slew")
        {
            // 이동이 끝난 때 해야 할 작업들 (가이더가 없으면 가이딩은 빠짐)
            _movedAt = (result as SlewResult)?.MovedAt;
            _needed = group.Where(t => t.AppliesTo(ctx)).Select(t => t.Id).ToHashSet();
            _done.Clear();
        }
        if (_movedAt is not { } from) return;
        _done.Add(task.Id);
        if (task.Id != "test" || !_needed.All(_done.Contains)) return;
        var now = ctx.Now();
        times.Record(profileId(), now - from, now);
        _movedAt = null;
    }
}
