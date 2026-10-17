using System.Net;
using System.Text;
using Astro.Nina;
using Astro.Server.Engine;

namespace Astro.Server.Tests.Engine;

/// <summary>종료 창에서 N.I.N.A.를 남기고 나머지를 닫기 전 장비 연결 끊기 (2026-10-16)</summary>
public class ProgramCloserTests
{
    /// <summary>요청 경로를 적고, failOn이 들어간 경로는 연결 오류로 답하는 N.I.N.A.</summary>
    private sealed class RecordingNina(string? failOn = null) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string[] StillConnected { get; init; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(path[(path.IndexOf("equipment/", StringComparison.Ordinal))..]);
            if (failOn is not null && path.Contains(failOn)) throw new HttpRequestException("connection refused");
            // 끊기 뒤 상태 읽기: Connected = stillConnected에 든 종류만 true
            var body = path.EndsWith("/info")
                ? "{\"Success\":true,\"Response\":{\"Connected\":" + (StillConnected.Any(path.Contains) ? "true" : "false") + "}}"
                : "{\"Success\":true,\"Response\":\"ok\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static NinaApiClient Client(HttpMessageHandler h) => new(new HttpClient(h) { BaseAddress = new Uri("http://nina.test/v2/api/") });

    [Fact]
    public async Task N_I_N_A_장비를_모두_끊고_적도의는_마지막()
    {
        var h = new RecordingNina();
        var left = await ProgramCloser.DisconnectNinaAsync(Client(h), CancellationToken.None);
        var disconnects = h.Paths.Where(p => p.EndsWith("/disconnect")).ToList();
        Assert.Equal(8, disconnects.Count);
        Assert.Equal("equipment/guider/disconnect", disconnects[0]); // PHD2를 닫기 전에 가이더부터
        Assert.Equal("equipment/mount/disconnect", disconnects[^1]);
        Assert.Empty(left); // 끊은 뒤 상태를 읽어 모두 끊김을 확인
    }

    [Fact]
    public async Task 하나가_실패해도_나머지는_끊는다()
    {
        var h = new RecordingNina(failOn: "camera");
        var left = await ProgramCloser.DisconnectNinaAsync(Client(h), CancellationToken.None);
        Assert.Equal(8, h.Paths.Count(p => p.EndsWith("/disconnect")));
        Assert.Equal(["camera"], left); // 카메라는 끊었는지 모른다 — 숨기지 않는다 (CX-40-02)
    }

    [Fact]
    public async Task CX40_02_끊기를_거절해도_연결돼_있으면_알린다()
    {
        var h = new RecordingNina { StillConnected = ["mount", "guider"] };
        var left = await ProgramCloser.DisconnectNinaAsync(Client(h), CancellationToken.None);
        Assert.Equal(["guider", "mount"], left);
        Assert.Equal("가이더, 적도의", ProgramCloser.KindNames(left));
    }
}
