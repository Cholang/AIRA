using System.Net;
using System.Text;
using Astro.Nina;
using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Polar;
using Astro.Server.Prepare.Tasks.Slew;
using Astro.Server.Prepare.Tasks.Wrap;
using Astro.Server.Shoot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>Codex 36절 장비 안전 지적의 회귀 시험 (2026-10-16): 극 찾기 재시작 · 이어서 하기 이동 승인 · 잠금 뒤 고도 · 계획으로 돌아갈 때 추적 끄기</summary>
public class Review36Tests
{
    private static readonly SimOptions Fast = new() { Speed = 0 };

    /// <summary>가짜 화면 통로: pick이 ""을 돌려주면 누르지 않는다(작업이 질문을 취소할 때까지)</summary>
    private sealed class ScriptRun(PrepContext ctx, Func<IReadOnlyList<PrepAction>, string> pick) : ITaskRun
    {
        public PrepContext Context => ctx;
        public int RunId => 1;
        public List<string> Asked { get; } = [];
        public void SubStep(int index) { }
        public void Guide(string title, string text) { }
        public void Readout(string kind, string big, string caption, Tone tone, IReadOnlyDictionary<string, double>? values = null, bool live = false, bool unverified = false, DateTimeOffset? observedAt = null) { }
        public void ClearReadout() { }
        public void Status(string? text, Tone tone = Tone.Busy) { }
        public void Live(string kind, string? url = null, object? data = null) { }
        public Task<string> AskAsync(IReadOnlyList<PrepAction> actions, CancellationToken ct)
        {
            lock (Asked) Asked.Add(string.Join(",", actions.Select(a => a.Id)));
            var id = pick(actions);
            if (id == "") return Task.Delay(Timeout.Infinite, ct).ContinueWith<string>(_ => throw new OperationCanceledException(ct), TaskScheduler.Default);
            return Task.FromResult(id);
        }
    }

    /// <summary>극축 정렬 모의 장비에 극 찾기 결과·홈 결과를 차례로 끼워 넣고 부른 차례를 적는다</summary>
    private sealed class ScriptedPolar(Queue<PoleFindResult> finds, Func<PolarReturn> home) : IPolarDevices
    {
        private readonly SimulatedPolarDevices _inner = new(Fast, new SimFaults());
        public List<string> Calls { get; } = [];
        public Task<DeviceResult> SetSiderealTrackingAsync(CancellationToken ct) => _inner.SetSiderealTrackingAsync(ct);
        public Task<DeviceResult> HandOverGuideCameraAsync(CancellationToken ct) => _inner.HandOverGuideCameraAsync(ct);
        public Task<DeviceResult> StartSharpCapAsync(CancellationToken ct) => _inner.StartSharpCapAsync(ct);
        public Task<PoleFindResult> FindPoleAsync(Action<PoleProgress> progress, CancellationToken ct)
        {
            Calls.Add("find");
            return Task.FromResult(finds.Count > 0 ? finds.Dequeue() : new PoleFindResult(true, Rotated: true));
        }
        public IAsyncEnumerable<PolarOffset> WatchOffsetsAsync(CancellationToken ct) => _inner.WatchOffsetsAsync(ct);
        public Task<DeviceResult> GiveBackGuideCameraAsync(CancellationToken ct) => _inner.GiveBackGuideCameraAsync(ct);
        public Task<double?> GuidePixelScaleAsync(CancellationToken ct) => _inner.GuidePixelScaleAsync(ct);
        public bool OffsetUnitVerified => _inner.OffsetUnitVerified;
        public Task<bool> StopAsync(CancellationToken ct) => _inner.StopAsync(ct);
        public Task<bool> MountStillAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<PolarReturn> ReturnToStartAsync(CancellationToken ct)
        {
            Calls.Add("home");
            return Task.FromResult(home());
        }
        public Task<PolarEndState> ReadEndStateAsync(CancellationToken ct) => _inner.ReadEndStateAsync(ct);
    }

    [Fact]
    public async Task CX36_01_돌린_뒤_극축_정렬을_건너뛰고_다시_시작해도_먼저_시작_위치로_되돌린다()
    {
        var devices = new ScriptedPolar(new([new PoleFindResult(false, "둘째 사진에서 위치를 찾지 못함", Rotated: true)]), () => PolarReturn.AtStart);
        var ctx = Harness.Context();
        // 첫 실행: 돌린 뒤 실패 → 건너뛰기 (준비를 중단했다가 다시 시작하는 것처럼 실행이 끝남)
        await new PolarTask(devices).RunAsync(new ScriptRun(ctx, a => a.Any(x => x.Id == "skip") ? "skip" : a.First(x => x.Primary).Id), CancellationToken.None).WaitAsync(Harness.Timeout);
        Assert.Equal(["find"], devices.Calls);
        Assert.True(ctx.Memory.PolarNeedsReturn);
        // 새 실행(같은 밤): 다시 돌리기 전에 홈부터
        await new PolarTask(devices).RunAsync(new ScriptRun(ctx, a => a.First(x => x.Primary).Id), CancellationToken.None).WaitAsync(Harness.Timeout);
        Assert.Equal(["find", "home", "find"], devices.Calls);
    }

    [Fact]
    public async Task CX36_01_돌리기_전에_실패하면_되돌리지_않는다()
    {
        var devices = new ScriptedPolar(new([new PoleFindResult(false, "별이 모자람")]), () => PolarReturn.AtStart);
        var ctx = Harness.Context();
        await new PolarTask(devices).RunAsync(new ScriptRun(ctx, a => a.First(x => x.Primary).Id), CancellationToken.None).WaitAsync(Harness.Timeout);
        Assert.Equal(["find", "find"], devices.Calls);
    }

    [Fact]
    public async Task CX36_09_홈이_계속_실패하면_손으로_옮겼다고_하고_극_찾기로_간다()
    {
        var devices = new ScriptedPolar(new([new PoleFindResult(false, "둘째 사진 실패", Rotated: true)]), () => PolarReturn.Stopped);
        var ctx = Harness.Context();
        await new PolarTask(devices).RunAsync(new ScriptRun(ctx, a => a.Any(x => x.Id == "manual-home") ? "manual-home" : a.First(x => x.Primary).Id), CancellationToken.None)
            .WaitAsync(Harness.Timeout);
        Assert.Equal(["find", "home", "find"], devices.Calls); // 실패하는 홈을 되풀이하지 않는다
    }

    [Fact]
    public async Task CX36_02_확정_직후_이어서_하기는_이동을_다시_묻는다()
    {
        var (flow, _) = Harness.Flow();
        var ctx = Harness.TargetContext(new PrepResults(), new InMemoryPrepMemory(), Harness.Plan(DateTimeOffset.Now.AddSeconds(-30)));
        await flow.ResumeTargetAsync(ctx, new HashSet<string>());
        var v = await flow.Target.Until(x => x.Has("move") || x.Has("stop"), "이동 묻기 또는 이동");
        Assert.True(v.Has("move"), "누르지 않았는데 이동을 시작했다");
    }

    /// <summary>고도를 바꿀 수 있고 이동 명령 때의 고도를 적는 이동 장비</summary>
    private sealed class AltitudeSlew : ISlewDevices
    {
        public double Altitude = 15.1;
        public List<double> SlewedAt { get; } = [];
        public double TargetAltitude(PrepContext ctx) => Altitude;
        public int? MinutesUntilUsable(PrepContext ctx) => Altitude < SlewTask.LowestAltitude ? 30 : 0;
        public Task WaitTickAsync(CancellationToken ct) => Task.Delay(50, ct);
        public Task<SlewMove> SlewToTargetAsync(double raDeg, double decDeg, Action<double> remainingDeg, CancellationToken ct)
        {
            lock (SlewedAt) SlewedAt.Add(Altitude);
            return Task.FromResult(new SlewMove(true, 0.3));
        }
        public Task<bool> StopAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<SlewEndState> ReadEndStateAsync(PrepContext ctx, CancellationToken ct) => Task.FromResult(new SlewEndState(0.3, false, true));
    }

    [Fact]
    public async Task CX36_03_홈을_기다리는_사이_15도_아래로_내려가면_이동하지_않는다()
    {
        var devices = new AltitudeSlew();
        var ctx = Harness.Context(); // 방금 확정 = 이동 승인
        var held = await ctx.Mount.AcquireAsync(CancellationToken.None); // 홈 이동이 적도의를 쥔 것처럼
        var run = new ScriptRun(ctx, _ => "");
        using var cts = new CancellationTokenSource();
        var task = new SlewTask(devices).RunAsync(run, cts.Token);
        await Task.Delay(300);
        devices.Altitude = 14.9;
        await held.DisposeAsync();
        var deadline = DateTime.UtcNow + Harness.Timeout;
        while (!run.Asked.Contains("wait,move") && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.Contains("wait,move", run.Asked); // 다시 기다리기로
        Assert.Empty(devices.SlewedAt);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    /// <summary>적도의 정보 요청은 늘 실패하는 N.I.N.A., 추적 요청 수를 센다</summary>
    private sealed class UnreadableMount : HttpMessageHandler
    {
        public int Tracking;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/mount/tracking")) Tracking++;
            var body = path.EndsWith("/mount/info") ? "{\"Success\":false,\"Error\":\"timeout\"}" : "{\"Success\":true,\"Response\":\"ok\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class CountingWrap : IWrapDevices
    {
        public int TrackingOff;
        public Task<bool> PointUpAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<FlatExposure> FindFlatExposureAsync(int iso, Action<double, double> trying, CancellationToken ct) => Task.FromResult(new FlatExposure(true, 1, 50));
        public Task<bool> CaptureAsync(string imageType, double seconds, int iso, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> HomeAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> TrackingOffAsync(CancellationToken ct) { TrackingOff++; return Task.FromResult(true); }
        public Task<bool> DisconnectAllAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> CloseProgramsAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> StopAsync(CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task CX36_04_계획으로_돌아갈_때_적도의_상태를_못_읽으면_조용히_넘기지_않는다()
    {
        var nina = new NinaApiClient(new HttpClient(new UnreadableMount()) { BaseAddress = new Uri("http://nina.test/v2/api/") });
        var (flow, _) = Harness.Flow();
        var shoot = new ShootSession(new SimulatedShootDevices(Fast, new SimFaults()), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        var wrap = new CountingWrap();
        var home = new MountHome(new PrepareMode(false), nina, flow, shoot, new MountLock(), wrap, NullLogger<MountHome>.Instance);
        var problem = await home.RestForPlanAsync(CancellationToken.None);
        Assert.NotNull(problem); // 전에는 "미연결"로 보고 null(경고 없음)
        Assert.Contains("추적", problem);
    }
}
