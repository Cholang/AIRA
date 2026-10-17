using System.Net;
using System.Text;
using Astro.Nina;
using Astro.Server.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astro.Server.Tests.Engine;

/// <summary>CX-25-01: 답을 못 받은 연결 요청은 중단을 다시 해도 "끊겨 보임"만으로 확인한 것이 되지 않는다</summary>
public class AbortUnansweredTests
{
    /// <summary>메모리 안의 N.I.N.A.: 장비 정보(Connected)와 끊기만</summary>
    private sealed class FakeNina : HttpMessageHandler
    {
        public bool Connected;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/info") ? $"{{\"Success\":true,\"Response\":{{\"Connected\":{(Connected ? "true" : "false")}}}}}"
                : path.EndsWith("/disconnect") ? "{\"Success\":true,\"Response\":\"ok\"}"
                : "{\"Success\":false,\"Error\":\"?\"}";
            if (path.EndsWith("/disconnect")) Connected = false;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class NoHost : DevicePrecheck.IHostDevices
    {
        public IReadOnlyList<string> ComPorts() => [];
        public IReadOnlyList<(string Port, string Name)> UsbSerialPorts() => [];
        public bool UsbPresent(string vid) => false;
        public string? DriverComPort(string kind, string id) => null;
        public DateTimeOffset? HubAppStartedAt() => null;
        public DateTimeOffset? LastUsbInsertedAt() => null;
        public void CloseHubApp() { }
    }

    [Fact]
    public async Task 답_없는_연결은_다시_중단해도_미확인으로_남고_연결된_뒤_끊김을_보면_풀린다()
    {
        var root = Path.Combine(Path.GetTempPath(), "aira-abort-" + Guid.NewGuid().ToString("N"));
        var fake = new FakeNina();
        var nina = new NinaApiClient(new HttpClient(fake) { BaseAddress = new Uri("http://nina.test/v2/api/") });
        var state = new EquipmentRun();
        var connector = new EquipmentConnector(nina, Options.Create(new EquipmentOptions { Simulate = false }), new EquipmentSimulation(), new EquipmentChoices(),
            new RigOverrides(root), new LiveDevices(), new OpticsStore(root), state, new NoHost(), new CameraPowerStore(root), NullLogger<EquipmentConnector>.Instance);
        // 연결 요청이 답 없이 끝남 (시간 초과·통신 오류)
        state.InFlight["mount"] = Task.FromResult<bool?>(null);

        var first = await connector.AbortAsync(CancellationToken.None);
        Assert.Single(first, f => f.Contains("연결 요청이 끝나지 않음"));

        var again = await connector.AbortAsync(CancellationToken.None); // 그동안 계속 끊겨 보여도
        Assert.Single(again, f => f.Contains("연결 요청이 끝나지 않음"));

        fake.Connected = true; // N.I.N.A.가 늦게 연결을 마침 → 중단이 끊고 끊김을 확인
        var last = await connector.AbortAsync(CancellationToken.None);
        Assert.Empty(last);
        Assert.Empty(state.Unanswered);
        Assert.Empty(await connector.AbortAsync(CancellationToken.None)); // 더 지켜볼 것 없음
    }
}
