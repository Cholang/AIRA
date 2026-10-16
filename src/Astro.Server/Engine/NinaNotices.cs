using System.Text;

namespace Astro.Server.Engine;

/// <summary>
/// N.I.N.A. 오류를 아이라 말로 (2026-10-09 사용자 요청). N.I.N.A. 알림(토스트)은 영어·번역 누락("MISSING LABEL")이 섞여 있고,
/// Advanced API는 알림을 주지 않는다 → N.I.N.A. 로그 파일(%LOCALAPPDATA%\NINA\Logs, 같은 내용이 원인까지 남음)을 따라 읽어
/// 오류·경고를 진단 카드 형식(무슨 일 / 왜 / 이렇게, 원본은 접어서 — DESIGN.md "공통 부품" 진단 카드)으로 바꾼다.
/// </summary>
public sealed record NinaNotice(long Id, DateTimeOffset At, string Category, string Title, IReadOnlyList<string> Why, string Fix, string Raw, int Count)
{
    /// <summary>같은 알림인지 가르는 열쇠 (보통 제목, 해석 못 한 알림은 원문 전체 — CX-21-06: 화면용 짧은 제목이 같아도 원문이 다르면 따로)</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Key { get; init; } = "";
}

public sealed class NinaNotices
{
    private readonly Lock _gate = new();
    private readonly List<NinaNotice> _items = [];
    private long _next = 1;

    /// <summary>after보다 뒤의 알림 (화면이 2초마다 묻는다)</summary>
    public IReadOnlyList<NinaNotice> Since(long after)
    {
        lock (_gate) return _items.Where(n => n.Id > after).ToList();
    }

    private readonly Dictionary<long, DateTimeOffset> _sentAt = []; // 화면에 새 번호로 전한 시각 (반복 알림을 30초에 한 번만 다시 전하려고)

    /// <summary>아이라가 마지막으로 알림을 만든 시각 — 이때만 N.I.N.A. 알림 창을 숨긴다 (Codex L09: 해석하지 못한 알림은 그대로 보이게)</summary>
    public DateTimeOffset LastAddedAt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>
    /// 해석 규칙이 없는 N.I.N.A. 오류·경고 — 하나씩 보관해 아직 전하지 않은 것을 원문으로 알린다 (2026-10-10, CX-20-10: 마지막 한 건만 두면
    /// 여러 경고 중 앞의 것이 사라지고, 다른 알림이 있었다는 것만으로 전한 것으로 쳤다). 오래된 것은 50건까지만
    /// </summary>
    private readonly List<(DateTimeOffset At, string Raw, bool Sent)> _unknown = [];
    public void NoteUnknown(DateTimeOffset at, string raw)
    {
        lock (_gate)
        {
            _unknown.Add((at, raw, false));
            if (_unknown.Count > 50) _unknown.RemoveAt(0);
        }
    }

    /// <summary>
    /// 숨긴 N.I.N.A. 알림 창을 돌려주지 않고 아이라 알림으로 (해석 규칙이 없으면 원문 그대로). since 뒤의 해석 못 한 로그가 없으면 false — 그때만 창을 돌려준다
    /// </summary>
    public bool AddFallback(DateTimeOffset since)
    {
        List<(DateTimeOffset At, string Raw)> due;
        lock (_gate)
        {
            due = _unknown.Where(u => !u.Sent && u.At >= since).Select(u => (u.At, u.Raw)).ToList();
            for (var i = 0; i < _unknown.Count; i++)
                if (!_unknown[i].Sent && _unknown[i].At >= since) _unknown[i] = _unknown[i] with { Sent = true };
        }
        // 원문마다 따로 — 제목은 원문 앞부분(화면용), 같은 알림인지는 원문 전체로 (CX-21-06)
        foreach (var u in due)
            Add("nina", $"N.I.N.A. 알림: {Short(u.Raw)}", [], "아래 원문이 N.I.N.A.가 띄운 알림입니다. 계속 되풀이되면 N.I.N.A. 창에서 자세히 확인해 주세요.", u.Raw, u.At, key: "raw:" + u.Raw);
        return due.Count > 0;
    }

    private static string Short(string raw)
    {
        var line = raw.Split('\n')[0].Trim();
        return line.Length > 40 ? line[..40] + "…" : line;
    }

    /// <summary>
    /// 같은 알림이 1분 안에 다시 오면 새로 띄우지 않고 횟수만 (적도의 통신 오류는 속성마다 줄줄이 남는다).
    /// 반복이 이어지면 30초에 한 번 새 번호를 붙여 화면에 다시 전한다 — 화면은 after=번호로 묻기 때문에 번호가 그대로면 횟수 변화를 모른다 (Codex L06)
    /// </summary>
    public void Add(string category, string title, IReadOnlyList<string> why, string fix, string raw, DateTimeOffset at, string? key = null)
    {
        key ??= title;
        lock (_gate)
        {
            LastAddedAt = DateTimeOffset.Now;
            var i = _items.FindLastIndex(n => n.Key == key && at - n.At < TimeSpan.FromMinutes(1));
            if (i >= 0)
            {
                var old = _items[i];
                var bump = !_sentAt.TryGetValue(old.Id, out var sent) || at - sent >= TimeSpan.FromSeconds(30);
                var id = bump ? _next++ : old.Id;
                _items[i] = old with { Id = id, Count = old.Count + 1, At = at, Raw = raw };
                if (bump)
                {
                    _sentAt.Remove(old.Id);
                    _sentAt[id] = at;
                    // 번호 순서를 지키려고 맨 뒤로
                    var item = _items[i];
                    _items.RemoveAt(i);
                    _items.Add(item);
                }
                return;
            }
            FileLogProvider.Write("WARN", "N.I.N.A. 알림", $"{title} ({category}) — {raw.Split('\n')[0]}"); // 기록 파일에도 (2026-10-10)
            var n = new NinaNotice(_next++, at, category, title, why, fix, raw, 1) { Key = key };
            _items.Add(n);
            _sentAt[n.Id] = at;
            if (_items.Count > 50)
            {
                _sentAt.Remove(_items[0].Id);
                _items.RemoveAt(0);
            }
        }
    }
}

/// <summary>N.I.N.A. 로그를 1초마다 따라 읽는다. 켤 때는 지금 파일의 끝에서 시작(지난 오류를 다시 알리지 않음), 새 로그 파일(N.I.N.A.를 다시 켬)은 처음부터</summary>
public sealed class NinaLogWatcher(NinaNotices notices, ILogger<NinaLogWatcher> log) : BackgroundService
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Logs");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string? file = null;
        long pos = 0;
        var first = true;
        var pending = "";
        var idle = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var newest = Directory.Exists(Folder) ? new DirectoryInfo(Folder).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
                if (newest is not null)
                {
                    if (newest.FullName != file)
                    {
                        file = newest.FullName;
                        pos = first ? newest.Length : 0;
                        pending = "";
                    }
                    first = false;
                    if (newest.Length < pos) pos = 0;
                    // 마지막 항목을 기다렸는데 2초 동안 더 써지지 않으면 그대로 읽는다 (오류 뒤에 다른 로그가 없을 때)
                    if (newest.Length == pos && pending.Length > 0 && ++idle >= 2)
                    {
                        Report(pending.Split('\n'));
                        pending = "";
                    }
                    if (newest.Length > pos)
                    {
                        idle = 0;
                        using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        s.Seek(pos, SeekOrigin.Begin);
                        using var r = new StreamReader(s, Encoding.UTF8);
                        var text = pending + await r.ReadToEndAsync(ct);
                        pos = s.Position;
                        // 마지막 항목은 예외 줄이 아직 덜 써졌을 수 있다 → 다음 머리 줄이 올 때까지 남겨 둔다
                        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                        var lastHead = lines.FindLastIndex(l => l.Length > 10 && char.IsDigit(l[0]) && l[4] == '-' && l.Contains('|'));
                        if (lastHead > 0)
                        {
                            pending = string.Join('\n', lines.Skip(lastHead));
                            lines = lines.Take(lastHead).ToList();
                        }
                        else { pending = text; lines = []; }
                        Report(lines);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogDebug(e, "N.I.N.A. 로그를 읽지 못함");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // 해석 규칙의 예상 못 한 오류 등 — 이 줄들은 건너뛰고 계속 따라 읽는다 (H2)
                log.LogWarning(e, "N.I.N.A. 로그를 해석하지 못함");
                pending = "";
            }
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private void Report(IEnumerable<string> lines)
    {
        foreach (var e in NinaLogRules.Parse(lines.Select(l => l.TrimEnd('\r'))))
        {
            if (e.Level is not ("ERROR" or "WARNING")) continue;
            var raw = (e.Message + "\n" + e.Body).Trim();
            if (raw.Length > 600) raw = raw[..600] + "…";
            if (NinaLogRules.Interpret(e) is not { } n)
            {
                notices.NoteUnknown(e.At, raw); // 알림 창이 떴다면 이 원문으로 알린다 (NinaToastHider)
                continue;
            }
            notices.Add(n.Category, n.Title, n.Why, n.Fix, raw, e.At);
        }
    }
}
