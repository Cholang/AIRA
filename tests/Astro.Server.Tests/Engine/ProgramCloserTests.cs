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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(path[(path.IndexOf("equipment/", StringComparison.Ordinal))..]);
            if (failOn is not null && path.Contains(failOn)) throw new HttpRequestException("connection refused");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Success\":true,\"Response\":\"ok\"}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static NinaApiClient Client(HttpMessageHandler h) => new(new HttpClient(h) { BaseAddress = new Uri("http://nina.test/v2/api/") });

    [Fact]
    public async Task N_I_N_A_장비를_모두_끊고_적도의는_마지막()
    {
        var h = new RecordingNina();
        await ProgramCloser.DisconnectNinaAsync(Client(h), CancellationToken.None);
        Assert.Equal(8, h.Paths.Count);
        Assert.All(h.Paths, p => Assert.EndsWith("/disconnect", p));
        Assert.Equal("equipment/guider/disconnect", h.Paths[0]); // PHD2를 닫기 전에 가이더부터
        Assert.Equal("equipment/mount/disconnect", h.Paths[^1]);
    }

    [Fact]
    public async Task 하나가_실패해도_나머지는_끊는다()
    {
        var h = new RecordingNina(failOn: "camera");
        await ProgramCloser.DisconnectNinaAsync(Client(h), CancellationToken.None);
        Assert.Equal(8, h.Paths.Count);
    }
}
