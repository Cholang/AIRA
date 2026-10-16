using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Calibration;
using Astro.Server.Prepare.Tasks.Center;
using Astro.Server.Prepare.Tasks.Focus;
using Astro.Server.Prepare.Tasks.FocusCheck;
using Astro.Server.Prepare.Tasks.Guiding;
using Astro.Server.Prepare.Tasks.Polar;
using Astro.Server.Prepare.Tasks.Slew;
using Astro.Server.Prepare.Tasks.TestShot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>
/// 구도 회전 (2026-10-16 사용자 결정): 센터링 사진 위의 틀을 돌려 원하는 구도를 정하고, 카메라를 돌린 뒤 다시 찍어 2° 안이면 맞춘 것.
/// 각도는 화면 시계 방향 +
/// </summary>
public class FramingTests
{
    [Fact]
    public void 안내대로_돌렸으면_남은_각도는_0()
    {
        var goal = new FramingGoal("plan", 87, 30);
        Assert.Equal(0, Framing.Remaining(goal, 87 + Framing.AnglePerTurn * 30), 6);
        Assert.Equal(30, Framing.Remaining(goal, 87), 6); // 안 돌림
        Assert.Equal(10, Framing.Remaining(goal, 87 + Framing.AnglePerTurn * 20), 6); // 덜 돌림
        Assert.Equal(-5, Framing.Remaining(goal, 87 + Framing.AnglePerTurn * 35), 6); // 더 돌림 → 반대로 5°
    }

    [Fact]
    public void 각도는_반바퀴_안으로_접는다()
    {
        Assert.Equal(-170, Framing.Wrap(190), 6);
        Assert.Equal(180, Framing.Wrap(-180), 6);
        Assert.Equal(10, Framing.Remaining(new FramingGoal("x", 355, 20), (355 + Framing.AnglePerTurn * 10 + 360) % 360), 6);
    }

    private const string Evening = "2026-10-16";

    /// <summary>장비 준비를 마치고 대상의 센터링까지 (센터링이 끝나 "이 대상으로 확정"을 기다리는 곳)</summary>
    private static async Task<(PrepareFlow Flow, PrepView View)> AtCenterPhoto()
    {
        var o = new SimOptions { Speed = 0 };
        var f = new SimFaults();
        var focuser = new SimulatedFocusDevices(o, f);
        var center = new SimulatedCenterDevices(o, f);
        IPrepTask[] tasks =
        [
            new PolarTask(new SimulatedPolarDevices(o, f)), new CalibrationTask(new SimulatedCalibrationDevices(o, f)), new FocusTask(focuser),
            new SlewTask(new SimulatedSlewDevices(o, f)), new CenterTask(center), new FocusCheckTask(focuser),
            new GuidingTask(new SimulatedGuidingDevices(o, f)), new TestShotTask(new SimulatedTestShotDevices(o, f)),
        ];
        var flow = new PrepareFlow(tasks, NullLogger<PrepareRunner>.Instance, simulated: true, autoNextDelay: TimeSpan.Zero) { SimulateTurn = center.UserTurns };
        var evening = DateOnly.Parse(Evening);
        var results = flow.ResultsFor(evening);
        var memory = new InMemoryPrepMemory();
        flow.StartRig(Harness.RigContext(results, memory), evening);
        await flow.Rig.RunToReady();
        Assert.Null(flow.StartTarget(Harness.TargetContext(results, memory)));
        return (flow, await UntilCenterDone(flow));
    }

    /// <summary>센터링이 끝나 다음 버튼이 보일 때까지 (그 전 작업의 주 버튼은 누른다)</summary>
    private static async Task<PrepView> UntilCenterDone(PrepareFlow flow)
    {
        for (var i = 0; i < 40; i++)
        {
            var v = await flow.Target.Until(x => x.Current?.Center.Actions.Count > 0, "버튼");
            if (v.Current!.TaskId == "center" && v.Row("center") == PrepTaskStatus.Done) return v;
            // 보고 누르는 사이 저절로 다음으로 넘어갔으면(바로 넘어가는 작업) 거절된다 — 다시 본다 (가끔 실패하던 시험)
            _ = flow.Target.Act(v.Current.Center.Actions.First(a => a.Primary).Id);
            await Task.Delay(5);
        }
        throw new InvalidOperationException("센터링이 끝나지 않았습니다: " + Harness.Describe(flow.Target.View()));
    }

    [Fact]
    public async Task 센터링_사진에_카메라_방향과_틀_각도가_실린다()
    {
        var (_, v) = await AtCenterPhoto();
        var values = v.Current!.Center.Readout!.Values;
        Assert.Equal(87, values["cameraAngle"]);
        Assert.Equal(0, values["frameDeg"]);
        Assert.Equal(Framing.ToleranceDeg, values["frameTolerance"]);
        Assert.Contains("모서리를 끌어", v.Current.Guide.Text);
    }

    [Fact]
    public async Task 틀을_돌리고_카메라를_돌려_다시_찍으면_남은_만큼_다시_안내하고_맞으면_끝낸다()
    {
        var (flow, _) = await AtCenterPhoto();
        Assert.Null(flow.SetFraming(30));

        // 모의 손은 85%만 돌린다 → 4.5° 남음
        Assert.Null(flow.CheckFraming());
        await flow.Target.Until(x => x.Row("center") != PrepTaskStatus.Done, "다시 센터링 시작");
        var v = await UntilCenterDone(flow);
        Assert.Equal(4.5, v.Current!.Center.Readout!.Values["frameDeg"], 1);
        Assert.Equal("조금 더 돌려 주세요", v.Current.Guide.Title);

        Assert.Null(flow.CheckFraming());
        await flow.Target.Until(x => x.Row("center") != PrepTaskStatus.Done, "다시 센터링 시작");
        v = await UntilCenterDone(flow);
        Assert.Equal(0, v.Current!.Center.Readout!.Values["frameDeg"]);
        Assert.Equal("구도를 맞췄어요", v.Current.Guide.Title);
        Assert.Equal("돌릴 구도가 정해지지 않았습니다.", flow.CheckFraming());
    }

    [Fact]
    public async Task CX36_07_새_계획에서는_앞서_정한_구도를_쓰지_않는다()
    {
        var (flow, _) = await AtCenterPhoto();
        Assert.Null(flow.SetFraming(30));
        // 다른 대상을 골랐다가 같은 대상으로 돌아온 것처럼 — 새로 확정한 계획
        var results = flow.ResultsFor(DateOnly.Parse(Evening));
        Assert.Null(flow.StartTarget(Harness.TargetContext(results, new InMemoryPrepMemory(), Harness.Plan(DateTimeOffset.Now.AddSeconds(1)))));
        var v = await UntilCenterDone(flow);
        Assert.Equal(0, v.Current!.Center.Readout!.Values["frameDeg"]);
        Assert.NotEqual("조금 더 돌려 주세요", v.Current.Guide.Title);
        Assert.Equal("돌릴 구도가 정해지지 않았습니다.", flow.CheckFraming());
    }

    [Fact]
    public async Task 허용_오차_안으로_돌리면_구도를_지운다()
    {
        var (flow, _) = await AtCenterPhoto();
        Assert.Null(flow.SetFraming(1.5));
        Assert.Equal("돌릴 구도가 정해지지 않았습니다.", flow.CheckFraming());
    }

    [Fact]
    public void 센터링_사진이_아니면_돌릴_수_없다()
    {
        var (flow, _) = Harness.Flow();
        Assert.NotNull(flow.SetFraming(30));
        Assert.NotNull(flow.CheckFraming());
    }
}
