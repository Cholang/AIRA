using System.Text.Json;
using Astro.Core;
using Astro.Server.Prepare.Tasks.Focus;

namespace Astro.Server.Prepare;

/// <summary>
/// "포커서를 0에 두고 끝냄" 기록 (데이터 폴더의 focuser-parked.json). 0 = 노브를 끝까지 넣은 기계 끝.
/// 끝낼 때 0으로 보내 도착을 확인하면 남기고, 포커서가 0이 아닌 곳으로 움직이기 시작하면 지운다
/// </summary>
public sealed class FocuserParkStore
{
    private readonly string _file;
    private readonly Lock _gate = new();
    private DateTimeOffset? _at;

    public FocuserParkStore(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        _file = Path.Combine(root, "focuser-parked.json");
        try { _at = File.Exists(_file) ? JsonSerializer.Deserialize<DateTimeOffset?>(File.ReadAllText(_file)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _at = null; }
    }

    public DateTimeOffset? ParkedAt
    {
        get { lock (_gate) return _at; }
    }

    public void Mark() => Save(DateTimeOffset.Now);

    public void Clear()
    {
        if (ParkedAt is not null) Save(null);
    }

    private void Save(DateTimeOffset? at)
    {
        lock (_gate)
        {
            _at = at;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                if (at is null) File.Delete(_file);
                else File.WriteAllText(_file, JsonSerializer.Serialize(at));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// 포커서를 0에 두고 끝내기 (2026-10-09 사용자 요청 — 포커서를 다시 달 때마다 노브를 손으로 0에 맞추는 일을 없애려고).
/// 0은 노브를 끝까지 넣은 기계 끝이라, 떼었다 다시 달며 기어가 맞물려 노브가 돌아가도 오차는 바깥쪽으로만 생긴다 —
/// 그 위치를 0으로 알아도 끝에 부딪히게 밀지 않고, 오차(많아야 수백 걸음)는 자동초점이 훑는 거리(±500) 안이다.
/// 마무리의 장비 정리와 아이라 종료가 부른다. 출발 전 점검은 2026-10-12부터 "포커서 0점"을 늘 묻는다(사용자는 포커서를 떼어 보관 — 노브가 돌면 0이 어긋남) —
/// 끝낼 때 0에 두는 것은 그대로(드로튜브가 들어간 채 보관, 다시 달 때 노브를 끝까지 넣기 쉬움). <see cref="ReadyAsync"/>는 기록 확인용
/// </summary>
// PrepareFlow는 필요할 때 꺼낸다 — 장비 정리 작업(PackTask)이 이것을 받아서, 생성자로 받으면 서로를 기다린다
public sealed class FocuserPark(PrepareMode mode, IFocusDevices devices, FocuserParkStore store, IServiceProvider services, Shoot.ShootSession shoot, ILogger<FocuserPark> log)
{
    /// <summary>끝 → 0 이동이 오래 걸려도 종료가 여기서 멈춰 있지 않게</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);

    /// <summary>포커서를 0으로 보내고 도착하면 기록. 보내지 않았거나 못 했으면 이유 (null = 0에 둠)</summary>
    public async Task<string?> ParkAsync(CancellationToken ct)
    {
        if (mode.Simulate) return "모의 장비";
        if (shoot.Active) return "촬영 중";
        var flow = services.GetRequiredService<Flow.PrepareFlow>();
        foreach (var runner in new[] { flow.Rig, flow.Target, flow.Wrap })
            if (runner.ActiveTaskTitle is { } task && task != "장비 정리") return $"{task} 작업 중";
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);
        try
        {
            if (!await devices.ConnectedAsync(limit.Token)) return "포커서가 연결되지 않음";
            // 위치는 실제로 읽은 값으로만 판단한다 — 읽지 못한 것을 0으로 치면 "0에 두고 끝냄"을 잘못 기록한다 (CX-20-05)
            var before = await devices.ReadEndStateAsync(limit.Token);
            if (before.Error) return Unsure("포커서 위치를 읽지 못함");
            if (before.Position != 0)
            {
                var moved = await devices.MoveAsync(0, limit.Token);
                if (!moved.Ok) return Unsure(moved.Problem ?? "포커서를 0으로 보내지 못함");
            }
            var after = await devices.ReadEndStateAsync(limit.Token);
            if (after.Error || after.Moving || after.Position != 0) return Unsure("포커서가 0에 멈춰 있는 것을 확인하지 못함");
            store.Mark();
            log.LogInformation("포커서를 0에 두고 끝냄");
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await devices.StopAsync(CancellationToken.None);
            return "포커서가 제시간에 0에 닿지 않음";
        }
    }

    /// <summary>0에 두지 못했거나 확인하지 못함: 지난 "0에 두고 끝냄" 기록을 더는 믿지 않는다 (다음 점검이 0점 카드를 묻게)</summary>
    private string Unsure(string why)
    {
        store.Clear();
        return why;
    }

    /// <summary>
    /// 지난번 0에 두고 끝냈고 포커서가 지금도 0을 가리키는가 → 노브를 손으로 맞추지 않아도 된다.
    /// 기록이 없으면(앱이 갑자기 꺼졌거나 전원을 먼저 뽑음) 또는 포커서가 0이 아니면 false — 지금처럼 묻는다
    /// </summary>
    public async Task<bool> ReadyAsync(CancellationToken ct)
    {
        if (mode.Simulate || store.ParkedAt is null) return false;
        // 연결됨 · 위치 0 · 멈춤을 실제로 읽었을 때만 (읽지 못하면 0점 확인을 건너뛰지 않는다, CX-20-05)
        try { return await devices.ConnectedAsync(ct) && await devices.ReadEndStateAsync(ct) is { Error: false, Moving: false, Position: 0 }; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return false; }
    }
}
