using Astro.Server.Engine;
using Astro.Server.Prepare.Flow;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>Codex 21절: 종료·적도의 정지 미확인·알림 묶음 (CX-21-03·05·06)</summary>
public class QuitSafetyTests
{
    private static readonly string[] Ids = ["polar", "calibration", "slew"];

    [Fact]
    public async Task CX21_03_적도의_정지_미확인이면_적도의_잠금을_주지_않는다()
    {
        var mount = new MountLock { Unconfirmed = "적도의가 멈췄는지 확인하지 못했습니다" };
        var e = await Assert.ThrowsAsync<MountUnconfirmedException>(() => mount.AcquireAsync(CancellationToken.None));
        Assert.Contains("확인하지 못했습니다", e.Message);
        Assert.False(mount.IsHeld); // 이유만 돌려주고 잠금을 쥐고 있지 않는다

        mount.Unconfirmed = null; // 사용자가 직접 확인
        await using (await mount.AcquireAsync(CancellationToken.None)) Assert.True(mount.IsHeld);
    }

    [Fact]
    public async Task CX21_05_종료를_취소하고_정지를_다시_확인하면_다시_시작_버튼이_남는다()
    {
        var log = new List<string>();
        var tasks = Ids.ToDictionary(id => id, id => new FakeTask(id, log));
        var runner = new PrepareRunner(Ids.Select(id => (IPrepTask)tasks[id]), NullLogger<PrepareRunner>.Instance);
        tasks["polar"].Hang = true;
        tasks["polar"].StopConfirms = false;
        runner.Start(Harness.Context());
        await runner.Until(x => x.Current?.TaskId == "polar" && x.Current.Readout() is not null, "① 진행 중");

        Assert.False(await runner.StopForQuitAsync());
        Assert.True(runner.View().Has("device-check"));

        tasks["polar"].StopConfirms = true; // 종료를 취소하고 장비 상태 다시 확인
        await runner.Press("device-check");
        var v = await runner.Until(x => x.Has("restart"), "다시 시작 버튼");
        Assert.Equal(PrepTaskStatus.NeedsRecheck, v.Row("polar"));
    }

    [Fact]
    public void CX21_06_앞부분이_같아도_원문이_다르면_따로_전한다()
    {
        var notices = new NinaNotices();
        var t = DateTimeOffset.Now;
        var head = new string('x', 45);
        notices.NoteUnknown(t, head + " camera lost");
        notices.NoteUnknown(t.AddSeconds(1), head + " mount lost");
        Assert.True(notices.AddFallback(t));
        var items = notices.Since(0);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, n => n.Raw.EndsWith("camera lost"));
        Assert.Contains(items, n => n.Raw.EndsWith("mount lost"));
    }
}
