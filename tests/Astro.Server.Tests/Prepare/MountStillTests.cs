using System.Net;
using System.Text;
using Astro.Nina;
using Astro.Server.Prepare.Real;

namespace Astro.Server.Tests.Prepare;

/// <summary>
/// 정지 확인과 "연결 안 됨" (2026-10-16 실행 — 적도의가 끊긴 채 [임시] 건너뛰기를 누르자 좌표 칸이 없어 예외, 정지 확인이 끝내 통과하지 않았다).
/// 이번 실행에서 연결된 적이 없으면 멈춤, 연결돼 있다가 끊겼으면 모름
/// </summary>
public class MountStillTests
{
    /// <summary>적도의 정보: 차례대로 답한다 (마지막 답을 계속)</summary>
    private sealed class FakeMount(params string[] infos) : HttpMessageHandler
    {
        private int _i;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("/mount/info")
                ? "{\"Success\":true,\"Response\":" + infos[Math.Min(_i++, infos.Length - 1)] + "}"
                : "{\"Success\":true,\"Response\":\"ok\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private const string Disconnected = "{\"Connected\":false}";
    private const string Connected = "{\"Connected\":true,\"Slewing\":false,\"TrackingEnabled\":false,\"AtHome\":true,\"SiderealTime\":1.5,\"Coordinates\":{\"RADegrees\":10,\"Dec\":20}}";

    private static NinaRig Rig(params string[] infos) => new(new NinaApiClient(new HttpClient(new FakeMount(infos)) { BaseAddress = new Uri("http://nina.test/v2/api/") }));

    [Fact]
    public async Task 좌표_칸이_없어도_예외_없이_읽는다()
    {
        var m = await Rig(Disconnected).MountAsync(CancellationToken.None);
        Assert.NotNull(m);
        Assert.False(m.Connected);
        Assert.True(double.IsNaN(m.RaDeg));
    }

    [Fact]
    public async Task 이번_실행에서_연결된_적이_없으면_멈춘_것으로_본다()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await Rig(Disconnected).ConfirmStillAsync(cts.Token));
    }

    [Fact]
    public async Task 연결돼_있다가_끊겼으면_멈췄는지_모른다()
    {
        var rig = Rig(Connected, Disconnected);
        Assert.True((await rig.MountAsync(CancellationToken.None))!.Connected);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.False(await rig.ConfirmStillAsync(cts.Token));
    }
}
