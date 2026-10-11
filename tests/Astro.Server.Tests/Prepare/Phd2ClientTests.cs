using System.Net;
using System.Net.Sockets;
using Astro.Server.Prepare.Real;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>자체 점검 H1: PHD2가 답하지 않으면 취소 예외가 아니라 PHD2 오류로 — 받는 쪽이 그 자리에서 처리하고 촬영이 끝나지 않게</summary>
public class Phd2ClientTests
{
    [Fact]
    public async Task 답이_없으면_PHD2_오류로_알린다()
    {
        // 연결은 받지만 아무 답도 하지 않는 가짜 PHD2
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            var accept = server.AcceptTcpClientAsync();
            await using var phd2 = new Phd2Client(NullLogger<Phd2Client>.Instance) { Host = "127.0.0.1", Port = ((IPEndPoint)server.LocalEndpoint).Port };
            var e = await Assert.ThrowsAsync<Phd2Exception>(() => phd2.CallAsync("get_app_state", ct: CancellationToken.None, timeout: TimeSpan.FromMilliseconds(300)));
            Assert.Contains("답하지 않습니다", e.Message);
            (await accept).Dispose();
        }
        finally { server.Stop(); }
    }

    [Fact]
    public async Task 부른_쪽이_취소하면_취소_예외_그대로()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            var accept = server.AcceptTcpClientAsync();
            await using var phd2 = new Phd2Client(NullLogger<Phd2Client>.Instance) { Host = "127.0.0.1", Port = ((IPEndPoint)server.LocalEndpoint).Port };
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => phd2.CallAsync("get_app_state", ct: cts.Token, timeout: TimeSpan.FromSeconds(5)));
            (await accept).Dispose();
        }
        finally { server.Stop(); }
    }

    /// <summary>답하는 가짜 PHD2 한 연결: 줄마다 id를 읽어 result로 답한다</summary>
    private static async Task ServeAsync(TcpClient client, string result)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync() is { } line)
            {
                var id = System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("id").GetInt32();
                var reply = System.Text.Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"result\":\"{result}\",\"id\":{id}}}\r\n");
                await stream.WriteAsync(reply);
            }
        }
    }

    [Fact]
    public async Task M1_끊기면_구독이_끝나고_다음_호출은_새로_연결한다()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            await using var phd2 = new Phd2Client(NullLogger<Phd2Client>.Instance) { Host = "127.0.0.1", Port = ((IPEndPoint)server.LocalEndpoint).Port };
            var first = server.AcceptTcpClientAsync();
            using var events = await phd2.SubscribeAsync(CancellationToken.None);
            (await first).Dispose(); // PHD2가 꺼짐

            // 구독은 끝난다 (예전에는 끝나지 않고 조용히 기다리기만 했다)
            using (var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                Assert.False(await events.Reader.WaitToReadAsync(wait.Token));

            // PHD2가 다시 켜짐 → 다음 호출이 새로 연결해 답을 받는다
            var second = server.AcceptTcpClientAsync();
            var call = phd2.AppStateAsync(CancellationToken.None);
            _ = ServeAsync(await second, "Stopped");
            Assert.Equal("Stopped", await call);
            Assert.Equal("Stopped", await phd2.AppStateAsync(CancellationToken.None)); // 옛 연결 정리가 새 연결을 끊지 않음
        }
        finally { server.Stop(); }
    }

    [Fact]
    public async Task CX30_06_객체가_아닌_답이_와도_이벤트를_계속_받는다()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            await using var phd2 = new Phd2Client(NullLogger<Phd2Client>.Instance) { Host = "127.0.0.1", Port = ((IPEndPoint)server.LocalEndpoint).Port };
            var accept = server.AcceptTcpClientAsync();
            using var events = await phd2.SubscribeAsync(CancellationToken.None);
            using var client = await accept;
            var stream = client.GetStream();
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("[]\r\n{\"Event\":\"GuideStep\"}\r\n"));
            var e = await events.WaitAsync(["GuideStep"], TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(e);
        }
        finally { server.Stop(); }
    }
}
