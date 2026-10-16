using System.Text.Json;
using Astro.Core;
using Astro.Server.Prepare;

namespace Astro.Server.Camera;

/// <summary>
/// 지금 카메라와, 카메라마다 써 보며 배운 값 (데이터 폴더의 cameras.json). 카메라 내부 프로필(<see cref="CameraProfile"/>)의 기본값 위에 얹는다.
/// - 지금 카메라: N.I.N.A. 프로필의 카메라 이름이 들어올 때마다 바뀐다(<see cref="SetCurrent"/>) — 다른 카메라로 바꿔 끼우면 그 카메라의 묶음으로 넘어간다.
/// - 장 사이 쉬는 시간: 촬영이 끝날 때 그 밤의 중앙값을 남기고(<see cref="RecordOverhead"/>), 계획은 최근 <see cref="Nights"/>밤의 중앙값을 쓴다
/// </summary>
public sealed class CameraProfiles
{
    /// <summary>계산에 쓰는 최근 밤 수 (실패해 오래 걸린 한 밤에 끌려가지 않게 중앙값)</summary>
    public const int Nights = 5;
    private const int Keep = 20;

    private sealed record Night(DateTimeOffset At, double Seconds);
    private sealed class Data
    {
        public string? Current { get; set; }
        public Dictionary<string, List<Night>> Overheads { get; set; } = [];
    }

    private readonly string _file;
    private readonly Lock _gate = new();
    private readonly Data _data;

    public CameraProfiles(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        _file = Path.Combine(root, "cameras.json");
        _data = Load();
    }

    /// <summary>지금 카메라 이름 (N.I.N.A. 프로필에서 마지막으로 읽은 것). 모르면 null</summary>
    public string? CurrentName { get { lock (_gate) return _data.Current; } }

    /// <summary>지금 카메라(또는 이름을 준 카메라)의 내부 프로필</summary>
    public CameraProfile Profile(string? name = null) => CameraCatalog.Match(name ?? CurrentName);

    public void SetCurrent(string? name)
    {
        var key = Key(name);
        if (key is null) return;
        lock (_gate)
        {
            if (_data.Current == key) return;
            _data.Current = key;
            Save();
        }
    }

    /// <summary>계획에 쓸 장 사이 쉬는 시간(초): 이 카메라로 잰 최근 밤들의 중앙값, 없으면 프로필 기본값</summary>
    public double FrameOverhead(string? name = null)
    {
        var key = Key(name) ?? CurrentName;
        List<double> recent;
        lock (_gate)
            recent = key is not null && _data.Overheads.TryGetValue(key, out var list) ? [.. list.TakeLast(Nights).Select(n => n.Seconds)] : [];
        return recent.Count == 0 ? CameraCatalog.Match(key).FrameOverheadSeconds : Math.Round(PrepTimes.Median(recent), 1);
    }

    /// <summary>한 밤의 장 사이 쉬는 시간(그 밤 중앙값)을 남긴다</summary>
    public void RecordOverhead(string? name, double seconds, DateTimeOffset at)
    {
        var key = Key(name) ?? CurrentName;
        if (key is null || !double.IsFinite(seconds) || seconds < 0) return;
        lock (_gate)
        {
            if (!_data.Overheads.TryGetValue(key, out var list)) _data.Overheads[key] = list = [];
            list.Add(new Night(at, Math.Round(seconds, 1)));
            if (list.Count > Keep) list.RemoveRange(0, list.Count - Keep);
            Save();
        }
    }

    /// <summary>"X-T5 (gin_X-T5)" → "X-T5" — N.I.N.A.가 이름 뒤에 붙이는 괄호를 뗀다</summary>
    public static string? Key(string? name)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n) || n == "No_Device") return null;
        var paren = n.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? n[..paren] : n;
    }

    private Data Load()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_file)) ?? new Data() : new Data(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new Data(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_data));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
