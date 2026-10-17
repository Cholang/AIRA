using System.Globalization;
using System.Net;
using System.Text;
using Astro.Nina;
using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Real;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Wrap;
using Astro.Server.Shoot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>
/// 2026-10-17 실기 뒤: OnStep 홈 이동 중 "멈춤" 판정 · 아이라 종료 때 홈으로 보내기 · 추적 끄기를 상태로 판단 (메모리 안의 N.I.N.A., 실제 장비 없음)
/// </summary>
public class QuitHomeTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>적도의 정보를 정해 둔 대로 답하는 N.I.N.A. 항성시는 읽을 때마다 바뀐다(서로 다른 갱신). 추적 요청은 실패로 답할 수 있다</summary>
    private sealed class FakeMount : HttpMessageHandler
    {
        public bool Connected = true, AtHome, Slewing, Tracking = true, TrackingRequestFails;
        public double Dec = 45, Ra = 120;
        public string Pier = "pierEast";
        public int TrackingRequests;
        private double _sidereal = 1;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path.EndsWith("/mount/info"))
            {
                _sidereal += 0.001;
                static string B(bool v) => v ? "true" : "false";
                body = "{\"Success\":true,\"Response\":{\"Connected\":" + B(Connected) + ",\"Slewing\":" + B(Slewing) + ",\"TrackingEnabled\":" + B(Tracking)
                    + ",\"AtHome\":" + B(AtHome) + ",\"SideOfPier\":\"" + Pier + "\",\"SiderealTime\":" + _sidereal.ToString(Inv)
                    + ",\"SiteLatitude\":37.3,\"SiteLongitude\":127.1,\"Coordinates\":{\"RADegrees\":" + Ra.ToString(Inv) + ",\"Dec\":" + Dec.ToString(Inv) + "}}}";
            }
            else if (path.EndsWith("/mount/tracking"))
            {
                TrackingRequests++;
                // 실패로 답해도 실제로는 반영됐다 (2026-10-17 실기 — 종료 때 끄기)
                Tracking = request.RequestUri.Query.Contains("mode=0");
                body = TrackingRequestFails ? """{"Success":false,"Error":"busy"}""" : """{"Success":true,"Response":"ok"}""";
            }
            else if (path.EndsWith("/guider/info")) body = """{"Success":true,"Response":{"State":"Stopped"}}""";
            else body = """{"Success":true,"Response":"ok"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class CountingWrap : IWrapDevices
    {
        public int Homes, TrackingOff;
        public Task<bool> PointUpAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<FlatExposure> FindFlatExposureAsync(int iso, Action<double, double> trying, CancellationToken ct) => Task.FromResult(new FlatExposure(true, 1, 50));
        public Task<bool> CaptureAsync(string imageType, double seconds, int iso, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> HomeAsync(CancellationToken ct) { Homes++; return Task.FromResult(true); }
        public Task<bool> TrackingOffAsync(CancellationToken ct) { TrackingOff++; return Task.FromResult(true); }
        public Task<bool> DisconnectAllAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> CloseProgramsAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> StopAsync(CancellationToken ct) => Task.FromResult(true);
    }

    private static NinaApiClient Api(FakeMount fake) => new(new HttpClient(fake) { BaseAddress = new Uri("http://nina.test/v2/api/") });

    private static (MountHome Home, CountingWrap Wrap) Home(FakeMount fake)
    {
        var (flow, _) = Harness.Flow();
        var shoot = new ShootSession(new SimulatedShootDevices(new SimOptions { Speed = 0 }, new SimFaults()), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        var wrap = new CountingWrap();
        return (new MountHome(new PrepareMode(false), Api(fake), flow, shoot, new MountLock(), wrap, NullLogger<MountHome>.Instance), wrap);
    }

    [Fact]
    public void OnStep_홈_이동_중의_모습을_알아본다()
    {
        Assert.True(MountTiming.HomingLikely(atHome: false, decDeg: 90, pier: "pierUnknown"));
        Assert.False(MountTiming.HomingLikely(atHome: true, decDeg: 90, pier: "pierUnknown"));   // 도착함
        Assert.False(MountTiming.HomingLikely(atHome: false, decDeg: 88.65, pier: "pierUnknown")); // 극 근처지만 홈 값이 아님
        Assert.False(MountTiming.HomingLikely(atHome: false, decDeg: 90, pier: "pierEast"));
        Assert.False(MountTiming.HomingLikely(atHome: false, decDeg: double.NaN, pier: "pierUnknown"));
    }

    [Fact]
    public async Task 홈으로_가는_중이면_좌표가_그대로여도_멈춘_것으로_보지_않는다()
    {
        var homing = new FakeMount { Dec = 90, Pier = "pierUnknown", Tracking = false };
        Assert.False(await new NinaRig(Api(homing)).WaitStillAsync(TimeSpan.FromSeconds(3), CancellationToken.None));

        var still = new FakeMount { Dec = 45, Pier = "pierEast", Tracking = true };
        Assert.True(await new NinaRig(Api(still)).WaitStillAsync(TimeSpan.FromSeconds(6), CancellationToken.None));
    }

    [Fact]
    public async Task 추적_끄기_요청이_실패로_와도_꺼졌으면_끈_것으로_본다()
    {
        var fake = new FakeMount { TrackingRequestFails = true };
        Assert.True(await new NinaRig(Api(fake)).SetTrackingAsync(false, CancellationToken.None));
        Assert.Equal(1, fake.TrackingRequests); // 1초 뒤 꺼진 것을 봤으니 다시 보내지 않음
    }

    [Fact]
    public async Task 종료_창은_연결돼_있고_홈이_아닐_때만_홈으로_보내기를_보인다()
    {
        Assert.True(await Home(new FakeMount()).Home.OfferForQuitAsync(CancellationToken.None));
        Assert.False(await Home(new FakeMount { AtHome = true }).Home.OfferForQuitAsync(CancellationToken.None));
        Assert.False(await Home(new FakeMount { Connected = false }).Home.OfferForQuitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 종료_때_홈으로_보내고_추적을_끈다()
    {
        var (home, wrap) = Home(new FakeMount());
        Assert.Null(await home.HomeForQuitAsync(CancellationToken.None));
        Assert.Equal(1, wrap.Homes);
        Assert.Equal(1, wrap.TrackingOff);
    }

    [Fact]
    public async Task 종료_때_적도의가_연결되지_않았거나_이미_홈이면_보내지_않는다()
    {
        var (off, offWrap) = Home(new FakeMount { Connected = false });
        Assert.Null(await off.HomeForQuitAsync(CancellationToken.None));
        Assert.Equal(0, offWrap.Homes);

        var (at, atWrap) = Home(new FakeMount { AtHome = true });
        Assert.Null(await at.HomeForQuitAsync(CancellationToken.None));
        Assert.Equal(0, atWrap.Homes);
    }

    [Fact]
    public async Task 종료_때_움직이는_중이면_홈으로_보내지_못했다고_알린다()
    {
        var (home, wrap) = Home(new FakeMount { Slewing = true });
        Assert.NotNull(await home.HomeForQuitAsync(CancellationToken.None));
        Assert.Equal(0, wrap.Homes);
    }
}
