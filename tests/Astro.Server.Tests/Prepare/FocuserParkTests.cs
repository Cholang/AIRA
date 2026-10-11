using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Focus;
using Astro.Server.Shoot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>포커서를 0에 두고 끝내기 → 다음 점검에서 "포커서 0점"을 묻지 않음 (2026-10-09 사용자 요청)</summary>
public class FocuserParkTests
{
    private static readonly SimOptions Fast = new() { Speed = 0 };

    private static (FocuserPark Park, FocuserParkStore Store, SimulatedFocusDevices Devices) Make(string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "aira-park-" + Guid.NewGuid().ToString("N"));
        var devices = new SimulatedFocusDevices(Fast, new SimFaults());
        var store = new FocuserParkStore(root);
        var services = new ServiceCollection()
            .AddSingleton(new PrepareFlow([], NullLogger<PrepareRunner>.Instance, simulated: true))
            .BuildServiceProvider();
        var shoot = new ShootSession(new SimulatedShootDevices(Fast, new SimFaults()), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        // 모의 장비로 동작을 보되 모드는 실장비 (모의 모드에서는 기록하지 않음)
        var park = new FocuserPark(new PrepareMode(false), devices, store, services, shoot, NullLogger<FocuserPark>.Instance);
        return (park, store, devices);
    }

    [Fact]
    public async Task 끝낼_때_0으로_보내고_기록하면_다음_점검에서_묻지_않는다()
    {
        var (park, store, devices) = Make();
        Assert.False(await park.ReadyAsync(CancellationToken.None)); // 기록 없음 → 지금처럼 묻는다
        Assert.Null(await park.ParkAsync(CancellationToken.None));
        Assert.Equal(0, await devices.PositionAsync(CancellationToken.None));
        Assert.NotNull(store.ParkedAt);
        Assert.True(await park.ReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 기록이_있어도_포커서가_0이_아니면_묻는다()
    {
        var (park, _, devices) = Make();
        await park.ParkAsync(CancellationToken.None);
        await devices.MoveAsync(800, CancellationToken.None); // 예: N.I.N.A.에서 직접 움직임
        Assert.False(await park.ReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 기록은_껐다_켜도_남고_지우면_없어진다()
    {
        var root = Path.Combine(Path.GetTempPath(), "aira-park-" + Guid.NewGuid().ToString("N"));
        var (park, store, _) = Make(root);
        await park.ParkAsync(CancellationToken.None);
        Assert.NotNull(new FocuserParkStore(root).ParkedAt);
        store.Clear(); // 포커서가 0을 떠남 (RealFocusDevices 이동·자동초점)
        Assert.Null(new FocuserParkStore(root).ParkedAt);
    }

    [Fact]
    public async Task 위치를_읽지_못하면_0에_두었다고_기록하지_않는다_CX_20_05()
    {
        var root = Path.Combine(Path.GetTempPath(), "aira-park-" + Guid.NewGuid().ToString("N"));
        var (park, store, devices) = Make(root);
        await park.ParkAsync(CancellationToken.None);
        Assert.NotNull(store.ParkedAt); // 전에 0에 두고 끝냄
        var blind = new FocuserPark(new PrepareMode(false), new Blind(devices), store,
            new ServiceCollection().AddSingleton(new PrepareFlow([], NullLogger<PrepareRunner>.Instance, simulated: true)).BuildServiceProvider(),
            new ShootSession(new SimulatedShootDevices(Fast, new SimFaults()), new PrepareMode(true), NullLogger<ShootSession>.Instance), NullLogger<FocuserPark>.Instance);
        Assert.NotNull(await blind.ParkAsync(CancellationToken.None)); // 문제로 돌려준다
        Assert.Null(store.ParkedAt); // 지난 기록도 더는 믿지 않는다
        Assert.False(await blind.ReadyAsync(CancellationToken.None));
    }

    /// <summary>연결은 되는데 상태(위치)를 읽지 못하는 포커서</summary>
    private sealed class Blind(SimulatedFocusDevices inner) : IFocusDevices
    {
        public Task<(int Min, int Max)> LimitsAsync(CancellationToken ct) => inner.LimitsAsync(ct);
        public Task<int> PositionAsync(CancellationToken ct) => Task.FromResult(0); // 예전 "?? 0"처럼 0을 돌려줘도
        public Task<double?> TemperatureAsync(CancellationToken ct) => inner.TemperatureAsync(ct);
        public Task<FocuserMove> MoveAsync(int position, CancellationToken ct) => inner.MoveAsync(position, ct);
        public Task<AutofocusRun> AutofocusAsync(Action<int, double> point, CancellationToken ct) => inner.AutofocusAsync(point, ct);
        public Task<int?> CoarseSearchAsync(int min, int max, int from, Action<int> visiting, CancellationToken ct) => inner.CoarseSearchAsync(min, max, from, visiting, ct);
        public Task<bool> StopAsync(CancellationToken ct) => inner.StopAsync(ct);
        public Task<FocusEndState> ReadEndStateAsync(CancellationToken ct) => Task.FromResult(new FocusEndState(false, -1, true));
    }
}
