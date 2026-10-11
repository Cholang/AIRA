using System.Net;
using System.Text;
using Astro.Nina;
using Astro.Server.Prepare.Real;

namespace Astro.Server.Tests.Prepare;

/// <summary>CX-32-03: 카메라 정보 답이 빠지면 "노출 끝"으로도 "후지 아님"으로도 보지 않는다 (메모리 안의 N.I.N.A., 실제 카메라 없음)</summary>
public class FujiStopTests
{
    /// <summary>카메라 정보는 늘 X-T5 노출 중. drop에 든 번째(1부터) 정보 요청은 답하지 않는다(실패 응답). 중단 요청 수를 센다</summary>
    private sealed class FakeNina(params int[] drop) : HttpMessageHandler
    {
        public int Info;
        public int Aborts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path.EndsWith("/camera/info"))
                body = drop.Contains(++Info) ? "{\"Success\":false,\"Error\":\"timeout\"}"
                    : "{\"Success\":true,\"Response\":{\"Name\":\"X-T5\",\"DeviceId\":\"Fujifilm Camera Plugin_X-T5\",\"IsExposing\":true}}";
            else
            {
                if (path.EndsWith("/abort-exposure")) Aborts++;
                body = "{\"Success\":true,\"Response\":\"ok\"}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static async Task<(bool? Stopped, FakeNina Fake)> Stop(params int[] drop)
    {
        var fake = new FakeNina(drop);
        var rig = new NinaRig(new NinaApiClient(new HttpClient(fake) { BaseAddress = new Uri("http://nina.test/v2/api/") }));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); // 준비 단계 정지 확인 한도처럼
        try { return (await rig.AbortExposureAsync(cts.Token), fake); }
        catch (OperationCanceledException) { return (null, fake); } // 끝까지 기다리다 한도 — 정지 확인 아님
    }

    [Fact]
    public async Task 첫_정보가_빠지면_정지로_보지_않는다()
    {
        var (stopped, fake) = await Stop(1);
        Assert.NotEqual(true, stopped);
        Assert.Equal(0, fake.Aborts);
    }

    [Fact]
    public async Task 기다리는_중_정보가_빠져도_끝난_것으로_보지_않고_중단도_보내지_않는다()
    {
        var (stopped, fake) = await Stop(2, 3);
        Assert.NotEqual(true, stopped);
        Assert.Equal(0, fake.Aborts); // X-T5에는 중단 명령을 보내지 않는다
    }
}
