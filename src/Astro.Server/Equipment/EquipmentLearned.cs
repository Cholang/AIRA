using System.Text.Json;
using Astro.Core;
using Astro.Server.Prepare;

namespace Astro.Server.Equipment;

/// <summary>
/// 배운 값과 지금 장비 (데이터 폴더의 equipment.json) — 모든 장비 종류가 함께 쓴다. 모델 값(카탈로그) 위에 얹는다.
/// - 지금 장비: 종류마다 N.I.N.A.에서 마지막으로 읽은 장비 이름. 다른 장비로 바꿔 끼우면 그 장비의 모델·배운 값으로 넘어간다.
/// - 배운 값: 종류 · 장비 이름 · 속성마다 밤마다 한 값(그 밤의 중앙값)을 남기고, 쓸 때는 최근 <see cref="Nights"/>밤의 중앙값
///   (실패해 오래 걸린 한 밤에 끌려가지 않게). 종류마다 쓰는 쪽은 그 종류 폴더의 도우미(예: Camera/CameraLearned)
/// </summary>
public sealed class EquipmentLearned
{
    /// <summary>계산에 쓰는 최근 밤 수</summary>
    public const int Nights = 5;
    private const int Keep = 20;

    private sealed record Night(DateTimeOffset At, double Value);
    private sealed class Data
    {
        /// <summary>종류 → 지금 장비 이름</summary>
        public Dictionary<string, string> Current { get; set; } = [];
        /// <summary>"종류/장비 이름/속성" → 밤마다 값</summary>
        public Dictionary<string, List<Night>> Values { get; set; } = [];
    }

    private readonly string _file;
    private readonly Lock _gate = new();
    private readonly Data _data;

    public EquipmentLearned(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        _file = Path.Combine(root, "equipment.json");
        _data = Load();
    }

    /// <summary>"없음" 표시 (N.I.N.A. 프로필에 그 장비가 등록 안 됨)</summary>
    private const string AbsentMark = "";

    /// <summary>그 종류의 지금 장비 이름. 없음(등록 안 됨)·아직 모름이면 null — 둘 다 "없는 것으로" 다룬다 (2026-10-17 사용자)</summary>
    public string? Current(string kind)
    {
        lock (_gate) return _data.Current.TryGetValue(kind, out var n) && n != AbsentMark ? n : null;
    }

    /// <summary>그 종류가 없다고 확인됨 (N.I.N.A. 프로필에 등록 안 됨). 아직 모르는 것과 구분해 기록에 남긴다</summary>
    public bool IsAbsent(string kind)
    {
        lock (_gate) return _data.Current.TryGetValue(kind, out var n) && n == AbsentMark;
    }

    /// <summary>그 종류의 장비가 없다(등록 안 됨)고 남긴다 — 전에 쓰던 장비를 계속 들고 있지 않게</summary>
    public void SetAbsent(string kind)
    {
        lock (_gate)
        {
            if (_data.Current.TryGetValue(kind, out var now) && now == AbsentMark) return;
            _data.Current[kind] = AbsentMark;
            Save();
        }
    }

    public void SetCurrent(string kind, string? name)
    {
        var key = Key(name);
        if (key is null) return;
        lock (_gate)
        {
            if (_data.Current.TryGetValue(kind, out var now) && now == key) return;
            _data.Current[kind] = key;
            Save();
        }
    }

    /// <summary>배운 값 (최근 밤들의 중앙값). 장비 이름이 없으면 그 종류의 지금 장비. 잰 적이 없으면 null — 모델 값을 쓴다</summary>
    public double? Value(string kind, string? name, string property)
    {
        var key = Key(name) ?? Current(kind);
        if (key is null) return null;
        List<double> recent;
        lock (_gate)
            recent = _data.Values.TryGetValue(Slot(kind, key, property), out var list) ? [.. list.TakeLast(Nights).Select(n => n.Value)] : [];
        return recent.Count == 0 ? null : Math.Round(PrepTimes.Median(recent), 1);
    }

    /// <summary>한 밤의 값을 남긴다. 장비 이름이 없으면 그 종류의 지금 장비(모르면 남기지 않음)</summary>
    public void Record(string kind, string? name, string property, double value, DateTimeOffset at)
    {
        var key = Key(name) ?? Current(kind);
        if (key is null || !double.IsFinite(value)) return;
        lock (_gate)
        {
            var slot = Slot(kind, key, property);
            if (!_data.Values.TryGetValue(slot, out var list)) _data.Values[slot] = list = [];
            list.Add(new Night(at, Math.Round(value, 1)));
            if (list.Count > Keep) list.RemoveRange(0, list.Count - Keep);
            Save();
        }
    }

    /// <summary>"X-T5 (gin_X-T5)" → "X-T5" — N.I.N.A.가 장치 이름 뒤에 붙이는 괄호를 뗀다. 비었거나 "No_Device"면 null</summary>
    public static string? Key(string? name)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n) || n == "No_Device") return null;
        var paren = n.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? n[..paren] : n;
    }

    private static string Slot(string kind, string name, string property) => $"{kind}/{name}/{property}";

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
