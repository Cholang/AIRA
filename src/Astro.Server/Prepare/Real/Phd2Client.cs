using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// PHD2 JSON-RPC (기본 localhost:4400) 상시 연결. 명령은 id로 답을 맞추고, 이벤트(GuideStep·Calibrating·StarLost …)는 구독자에게 나눠 준다.
/// 끊기면 다음 요청 때 다시 연결한다. 2026-10-04~06 실기: stop_capture → Stopped → set_connected false로 카메라를 놓고,
/// set_connected true로 돌려받음. N.I.N.A.도 같은 PHD2에 붙어 있어도 된다(PHD2는 여러 클라이언트를 받음).
/// </summary>
public sealed class Phd2Client(ILogger<Phd2Client> log) : IAsyncDisposable
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    // 보내기는 한 번에 하나 — 촬영 감시·디더링·상태 조회가 겹쳐도 명령 줄이 섞이지 않게 (NetworkStream은 동시 쓰기를 보장하지 않음)
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = [];
    private readonly List<Channel<JsonElement>> _subscribers = [];
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readerStop;
    private int _id;

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4400;

    /// <summary>명령 하나. PHD2 오류면 Phd2Exception(이유 문장)</summary>
    public async Task<JsonElement> CallAsync(string method, object? @params = null, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        // 이번 호출이 쓸 연결을 받아 둔다 — 그사이 다른 호출이 끊고 다시 연결해도 비어 있는 연결을 쓰지 않게 (자체 점검 M1)
        var stream = await EnsureConnectedAsync(ct);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        int id;
        lock (_gate)
        {
            id = ++_id;
            _pending[id] = tcs;
        }
        var line = JsonSerializer.Serialize(@params is null ? new { method, id } : (object)new { method, @params, id }) + "\r\n";
        try
        {
            await _writeGate.WaitAsync(ct);
            try { await stream.WriteAsync(Encoding.UTF8.GetBytes(line), ct); }
            finally { _writeGate.Release(); }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? CallTimeout);
            var reply = await tcs.Task.WaitAsync(cts.Token);
            if (reply.TryGetProperty("error", out var err))
                throw new Phd2Exception(err.TryGetProperty("message", out var m) ? m.GetString() ?? "PHD2 오류" : "PHD2 오류");
            return reply.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            Drop(stream);
            throw new Phd2Exception("PHD2에 연결하지 못했습니다. PHD2가 켜져 있는지 확인해 주세요.");
        }
        // 답을 기다리다 시간 초과 (부른 쪽이 취소한 것이 아님): 취소 예외 그대로 두면 PHD2 오류만 처리하는 곳을 지나쳐
        // 촬영 전체가 "예상하지 못한 오류"로 끝났다 (2026-10-11 자체 점검 H1) → PHD2 오류로 알려 그 자리에서 처리하게
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("PHD2가 {Method}에 {Seconds}초 동안 답하지 않음", method, (timeout ?? CallTimeout).TotalSeconds);
            throw new Phd2Exception("PHD2가 답하지 않습니다. PHD2 창에 열린 대화 상자가 있는지 확인해 주세요.");
        }
        finally
        {
            lock (_gate) _pending.Remove(id);
        }
    }

    public async Task<string> AppStateAsync(CancellationToken ct) => (await CallAsync("get_app_state", ct: ct)).GetString() ?? "";

    /// <summary>
    /// 이벤트를 받는다. 다 쓰면 Dispose (구독 해제). 연결이 끊기면 이 구독은 끝난다(읽기가 false) — 받는 쪽이 다시 구독하면 다시 연결된다 (M1)
    /// </summary>
    public async Task<Phd2Events> SubscribeAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        var ch = Channel.CreateUnbounded<JsonElement>();
        lock (_gate) _subscribers.Add(ch);
        return new Phd2Events(ch.Reader, () => { lock (_gate) _subscribers.Remove(ch); ch.Writer.TryComplete(); });
    }

    /// <summary>연결해 두고 그 연결을 돌려준다 (이미 연결돼 있으면 그것)</summary>
    private async Task<NetworkStream> EnsureConnectedAsync(CancellationToken ct)
    {
        lock (_gate) if (_tcp is { Connected: true } && _stream is { } open) return open;
        await _connectGate.WaitAsync(ct);
        try
        {
            lock (_gate) if (_tcp is { Connected: true } && _stream is { } open) return open;
            Drop(null);
            var tcp = new TcpClient();
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                try { await tcp.ConnectAsync(Host, Port, cts.Token); }
                catch (Exception e) when (e is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
                {
                    tcp.Dispose();
                    throw new Phd2Exception("PHD2에 연결하지 못했습니다. PHD2가 켜져 있는지 확인해 주세요.");
                }
            }
            var stream = tcp.GetStream();
            var stop = new CancellationTokenSource();
            lock (_gate)
            {
                _tcp = tcp;
                _stream = stream;
                _readerStop = stop;
            }
            _ = Task.Run(() => ReadLoopAsync(stream, stop.Token));
            return stream;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        try
        {
            while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length == 0) continue;
                JsonElement msg;
                try { using var doc = JsonDocument.Parse(line); msg = doc.RootElement.Clone(); }
                catch (JsonException) { continue; }
                if (msg.TryGetProperty("id", out var id) && id.TryGetInt32(out var n))
                {
                    TaskCompletionSource<JsonElement>? tcs;
                    lock (_gate) _pending.TryGetValue(n, out tcs);
                    tcs?.TrySetResult(msg);
                }
                else if (msg.TryGetProperty("Event", out _))
                {
                    Channel<JsonElement>[] subs;
                    lock (_gate) subs = [.. _subscribers];
                    foreach (var s in subs) s.Writer.TryWrite(msg);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            log.LogInformation("PHD2 연결 끊김: {Message}", e.Message);
        }
        // 이 읽기 루프의 연결만 끊는다 — 그사이 새로 연결됐으면 새 연결은 그대로 (M1: 늦게 끝난 옛 루프가 새 연결을 끊던 것)
        Drop(stream);
    }

    /// <summary>
    /// 연결을 끊는다. which가 있으면 그 연결이 지금 연결일 때만 (옛 연결의 정리가 새 연결을 건드리지 않게).
    /// 기다리던 답은 실패로, 이벤트 구독은 끝낸다 — 구독한 쪽이 다시 구독하며 다시 연결하게 (M1: 끊긴 뒤 이벤트가 조용히 멈추던 것)
    /// </summary>
    private void Drop(NetworkStream? which)
    {
        Channel<JsonElement>[] subs;
        lock (_gate)
        {
            if (which is not null && !ReferenceEquals(which, _stream)) return;
            _readerStop?.Cancel();
            _readerStop?.Dispose();
            _readerStop = null;
            _stream?.Dispose();
            _tcp?.Dispose();
            _stream = null;
            _tcp = null;
            foreach (var p in _pending.Values) p.TrySetException(new IOException("PHD2 연결 끊김"));
            _pending.Clear();
            subs = [.. _subscribers];
            _subscribers.Clear();
        }
        foreach (var s in subs) s.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        Drop(null);
        return ValueTask.CompletedTask;
    }
}

public sealed class Phd2Exception(string message) : Exception(message);

/// <summary>PHD2 이벤트 구독. Event 이름으로 고르거나 다음 것을 기다린다</summary>
public sealed class Phd2Events(ChannelReader<JsonElement> reader, Action unsubscribe) : IDisposable
{
    public ChannelReader<JsonElement> Reader => reader;

    /// <summary>이 이름들 중 하나가 올 때까지 (시간 초과면 null)</summary>
    public async Task<JsonElement?> WaitAsync(string[] names, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (await reader.WaitToReadAsync(cts.Token))
                while (reader.TryRead(out var e))
                    if (names.Contains(Name(e))) return e;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        return null;
    }

    public static string Name(JsonElement e) => e.TryGetProperty("Event", out var n) ? n.GetString() ?? "" : "";

    public void Dispose() => unsubscribe();
}
