using Astro.Server.Prepare.Real;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>CX-32-01: 회전 시작 명령이 시간 한도를 넘겨 늦게 풀려도 적도의가 돈 채로 남지 않는다 (가짜 드라이버, 실제 적도의 없음)</summary>
public class AscomAxisLateStartTests
{
    /// <summary>가짜 드라이버: 연결 확인 또는 회전 시작(0이 아닌 속도)이 문이 열릴 때까지 막힌다</summary>
    public sealed class SlowDriver(bool blockConnect)
    {
        public readonly ManualResetEventSlim Gate = new(false);
        public readonly List<double> Rates = [];
        public bool Connected
        {
            get { if (blockConnect) Gate.Wait(); return true; }
            set { }
        }
        public bool CanMoveAxis(int axis) => true;
        public void MoveAxis(int axis, double rate)
        {
            if (rate != 0 && !blockConnect) Gate.Wait();
            lock (Rates) Rates.Add(rate);
        }
        public void AbortSlew() { }
        public double[] Seen() { lock (Rates) return [.. Rates]; }
    }

    private static AscomAxis Axis(SlowDriver d) => new(NullLogger<AscomAxis>.Instance) { CommandLimit = TimeSpan.FromMilliseconds(300), Factory = () => d };

    private static async Task Until(Func<bool> cond)
    {
        for (var i = 0; i < 200 && !cond(); i++) await Task.Delay(20);
    }

    [Fact]
    public async Task 연결에서_막혔다_늦게_풀리면_돌리지_않는다()
    {
        var d = new SlowDriver(blockConnect: true);
        var error = await Axis(d).RotateRaAsync(0.5, 5, _ => { }, CancellationToken.None);
        Assert.NotNull(error);
        d.Gate.Set();
        await Until(() => d.Seen().Length > 0);
        await Task.Delay(200);
        Assert.DoesNotContain(0.5, d.Seen()); // 늦게 풀린 시작 명령은 돌리지 않는다
    }

    [Fact]
    public async Task 회전_시작이_늦게_끝나면_바로_멈춘다()
    {
        var d = new SlowDriver(blockConnect: false);
        var error = await Axis(d).RotateRaAsync(0.5, 5, _ => { }, CancellationToken.None);
        Assert.NotNull(error);
        Assert.Contains(0.0, d.Seen()); // 막혀 있어도 멈춤은 따로 보낸다
        d.Gate.Set();
        await Until(() => d.Seen().Contains(0.5) && d.Seen()[^1] == 0.0);
        var seen = d.Seen();
        Assert.Contains(0.5, seen);
        Assert.Equal(0.0, seen[^1]); // 늦게 돈 회전은 시작 명령 쪽이 바로 멈춘다
    }

    /// <summary>가짜 드라이버: 멈춤(속도 0)이 문이 열릴 때까지 막힌다</summary>
    public sealed class StuckStopDriver
    {
        public readonly ManualResetEventSlim Gate = new(false);
        public readonly List<double> Rates = [];
        public bool Connected { get => true; set { } }
        public bool CanMoveAxis(int axis) => true;
        public void MoveAxis(int axis, double rate)
        {
            if (rate == 0) Gate.Wait();
            lock (Rates) Rates.Add(rate);
        }
        public void AbortSlew() { }
        public double[] Seen() { lock (Rates) return [.. Rates]; }
    }

    [Fact]
    public async Task CX34_01_멈춤이_끝나지_않았으면_다음_회전을_보내지_않는다()
    {
        var d = new StuckStopDriver();
        var axis = new AscomAxis(NullLogger<AscomAxis>.Instance) { CommandLimit = TimeSpan.FromMilliseconds(300), Factory = () => d };
        // 회전은 됐지만 끝의 멈춤이 막힘 → 성공이 아니다 (2026-10-14: 멈춤이 안 들어갔으면 부른 쪽이 비상 정지·확인을 하게)
        Assert.Equal(AscomAxis.StopNotSent, await axis.RotateRaAsync(0.5, 0.1, _ => { }, CancellationToken.None));
        var next = await axis.RotateRaAsync(0.5, 0.1, _ => { }, CancellationToken.None);
        Assert.NotNull(next); // 예전 멈춤이 남아 있으면 새 회전은 보내지 않는다 (새 회전 중에 늦게 멈추지 않게)
        Assert.Equal([0.5], d.Seen());
        d.Gate.Set();
        await Until(() => d.Seen().Length >= 2);
        await Task.Delay(100);
        Assert.Null(await axis.RotateRaAsync(0.5, 0.1, _ => { }, CancellationToken.None)); // 멈춤이 끝나면 다시 된다
        await Until(() => d.Seen().Length >= 4);
        Assert.Equal(0.0, d.Seen()[^1]);
    }
}
