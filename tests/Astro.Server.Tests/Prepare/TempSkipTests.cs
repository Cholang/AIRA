using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Shoot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>[임시] 건너뛰기-성공·실패 (2026-10-14 — 모든 작업, 더미 결과로 채우고 다음으로)</summary>
public class TempSkipTests
{
    private static readonly string[] Ids = ["polar", "calibration", "slew", "focus", "center", "guiding", "test"];

    private static (PrepareRunner Runner, Dictionary<string, FakeTask> Tasks, List<string> Log, PrepContext Ctx) Make(params string[] ids)
    {
        var log = new List<string>();
        var use = ids.Length > 0 ? ids : Ids;
        var tasks = use.ToDictionary(id => id, id => new FakeTask(id, log));
        var runner = new PrepareRunner(use.Select(id => (IPrepTask)tasks[id]), NullLogger<PrepareRunner>.Instance);
        var ctx = Harness.Context();
        return (runner, tasks, log, ctx);
    }

    [Fact]
    public async Task 진행_중인_작업을_성공으로_넘기면_멈춘_뒤_더미_결과와_다음_버튼()
    {
        var (runner, tasks, log, ctx) = Make();
        tasks["polar"].Hang = true;
        runner.Start(ctx);
        var running = await runner.Until(v => v.Row("polar") == PrepTaskStatus.Running, "극축 정렬 중");
        Assert.Equal(new TempSkipView("polar", "polar", true), running.TempSkip);

        Assert.Null(await runner.TempSkipAsync(true));
        Assert.Contains("stop:polar:True", log);
        Assert.False(ctx.Results.Get<PolarResult>()!.Skipped);
        var v = runner.View();
        Assert.Equal(PrepTaskStatus.Done, v.Row("polar"));
        Assert.Equal("calibration 시작", v.Current!.Center.Actions.First(a => a.Primary).Label);
        Assert.DoesNotContain(log, l => l.StartsWith("run:calibration"));
    }

    [Fact]
    public async Task 끝난_뒤에는_다음_작업을_시작하지_않고_실패로_넘긴다()
    {
        var (runner, _, log, ctx) = Make();
        runner.Start(ctx);
        var done = await runner.Until(x => x.Has("next"), "극축 정렬 끝");
        Assert.Equal("calibration", done.TempSkip!.TaskId); // 다음 작업이 대상

        Assert.Null(await runner.TempSkipAsync(false));
        Assert.DoesNotContain(log, l => l.StartsWith("run:calibration")); // 적도의를 움직이는 작업을 시작하지 않는다
        Assert.True(ctx.Results.Get<CalibrationResult>()!.Skipped);
        Assert.False(ctx.HasGuider); // 실패 = 그날 밤 가이딩 없이
        var v = runner.View();
        Assert.Equal(PrepTaskStatus.Skipped, v.Row("calibration"));
        Assert.Equal("calibration", v.Current!.TaskId);
        Assert.Equal("slew 시작", v.Current.Center.Actions.First(a => a.Primary).Label);
    }

    [Fact]
    public async Task 멈춘_것을_확인하지_못하면_넘기지_않는다()
    {
        var (runner, tasks, _, ctx) = Make();
        tasks["polar"].Hang = true;
        tasks["polar"].StopConfirms = false;
        runner.Start(ctx);
        await runner.Until(v => v.Row("polar") == PrepTaskStatus.Running, "극축 정렬 중");

        Assert.Equal("장비가 멈췄는지 확인하지 못했습니다", await runner.TempSkipAsync(true));
        Assert.Null(ctx.Results.Get<PolarResult>());
        Assert.NotEqual(PrepTaskStatus.Done, runner.View().Row("polar"));
    }

    [Fact]
    public async Task 초점_확인은_성공으로만_넘긴다()
    {
        var (runner, tasks, _, ctx) = Make("focuscheck");
        tasks["focuscheck"].Hang = true;
        runner.Start(ctx);
        var v = await runner.Until(x => x.Row("focuscheck") == PrepTaskStatus.Running, "초점 확인 중");
        Assert.False(v.TempSkip!.CanFail);
        Assert.Contains("성공으로만", await runner.TempSkipAsync(false));
        Assert.Null(await runner.TempSkipAsync(true));
        Assert.NotNull(ctx.Results.Get<FocusCheckResult>());
    }

    private static (ShootSession Session, PrepResults Results) Shooting(int frames)
    {
        // 실제 속도(Speed 1)라 첫 사진 노출 중에 넘긴다
        var session = new ShootSession(new SimulatedShootDevices(new SimOptions { Speed = 1 }, new SimFaults()), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        var results = new PrepResults();
        session.Start(Harness.TargetContext(results, new InMemoryPrepMemory(), Harness.Plan() with { EstimatedFrames = frames }));
        return (session, results);
    }

    [Fact]
    public async Task 촬영을_성공으로_넘기면_계획_장수를_다_찍은_것으로_끝난다()
    {
        var (session, results) = Shooting(40);
        await Task.Delay(200);
        Assert.Null(await session.TempSkipAsync(true));
        var v = session.View();
        Assert.Equal(ShootMode.Ended, v.Mode);
        Assert.Equal("frames", v.Ended);
        Assert.Equal(40, v.Good);
        Assert.Equal(40, results.Get<NightShootResult>()!.Targets.Single().Good);
        Assert.Null(session.BlocksNext()); // 가이딩·적도의 정지를 확인했으니 마무리로 갈 수 있다
    }

    [Fact]
    public async Task 촬영을_실패로_넘기면_일부만_찍고_오류로_끝난다()
    {
        var (session, results) = Shooting(30);
        await Task.Delay(200);
        Assert.Null(await session.TempSkipAsync(false));
        var v = session.View();
        Assert.Equal("error", v.Ended);
        Assert.Equal(10, v.Good);
        Assert.Equal(2, v.Excluded);
        Assert.Equal(10, results.Get<NightShootResult>()!.Targets.Single().Good);
    }

    [Fact]
    public async Task 찍고_있지_않으면_촬영을_넘기지_않는다()
    {
        var session = new ShootSession(new SimulatedShootDevices(new SimOptions { Speed = 0 }, new SimFaults()), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        Assert.Equal("찍고 있지 않습니다.", await session.TempSkipAsync(true));
    }

    [Fact]
    public async Task 모두_넘기면_묶음을_끝낼_수_있고_포커서_위치는_채우지_않는다()
    {
        var (runner, tasks, _, ctx) = Make();
        foreach (var t in tasks.Values) t.Hang = true;
        runner.Start(ctx);
        await runner.Until(v => v.Row("polar") == PrepTaskStatus.Running, "극축 정렬 중");
        for (var i = 0; i < Ids.Length; i++) Assert.Null(await runner.TempSkipAsync(i % 2 == 0));
        Assert.Equal("지금은 넘길 작업이 없습니다", await runner.TempSkipAsync(true));
        Assert.Null(ctx.Results.Get<FocusResult>()!.Position); // 뒤 작업이 가짜 위치로 포커서를 옮기지 않게
        Assert.True(ctx.Results.Get<SlewResult>()!.AcceptedHere is false);
        var v = runner.View();
        Assert.Null(v.TempSkip);
        Assert.Null(runner.Act(v.Current!.Center.Actions.Single(a => a.Primary).Id));
        Assert.True(runner.View().Ready);
    }
}
