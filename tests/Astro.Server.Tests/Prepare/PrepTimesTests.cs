using Astro.Core.Sky;
using Astro.Server.Assistant;
using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Center;
using Astro.Server.Prepare.Tasks.Focus;
using Astro.Server.Prepare.Tasks.FocusCheck;
using Astro.Server.Prepare.Tasks.Guiding;
using Astro.Server.Prepare.Tasks.Slew;
using Astro.Server.Prepare.Tasks.TestShot;

namespace Astro.Server.Tests.Prepare;

/// <summary>
/// 준비 시간 (2026-10-16 사용자 결정): 대상으로 이동을 시작한 때 → 시험 사진을 마친 때, 프로필마다 최근 5번의 중앙값, 기록이 없으면 20분.
/// 이동 없음(지금 위치를 목표로)·건너뛴 작업이 있는 밤은 남기지 않는다
/// </summary>
public class PrepTimesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aira-prep-times-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T = new(2026, 10, 16, 21, 0, 0, TimeSpan.FromHours(9));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    private static void Add(PrepTimes times, string? profile, params double[] minutes)
    {
        foreach (var (m, i) in minutes.Select((m, i) => (m, i))) times.Record(profile, TimeSpan.FromMinutes(m), T.AddDays(i));
    }

    [Fact]
    public void 기록이_없으면_기본_20분()
    {
        Assert.Equal(TimeSpan.FromMinutes(20), new PrepTimes(_dir).Lead("p1"));
    }

    [Fact]
    public void 최근_5번의_중앙값을_분_단위로_올려_쓴다()
    {
        var times = new PrepTimes(_dir);
        Add(times, "p1", 10, 12, 50, 14, 16, 13.2); // 처음 10분은 빠지고 12·50·14·16·13.2 → 중앙값 14
        Assert.Equal(TimeSpan.FromMinutes(14), times.Lead("p1"));
        Add(times, "p2", 10, 15); // 둘이면 가운데 둘의 평균 12.5 → 13분
        Assert.Equal(TimeSpan.FromMinutes(13), times.Lead("p2"));
    }

    [Fact]
    public void 프로필마다_따로이고_다시_켜도_남아_있다()
    {
        Add(new PrepTimes(_dir), "p1", 30);
        var again = new PrepTimes(_dir);
        Assert.Equal(TimeSpan.FromMinutes(30), again.Lead("p1"));
        Assert.Equal(TimeSpan.FromMinutes(20), again.Lead("p2"));
    }

    [Fact]
    public void 일분보다_짧으면_남기지_않는다()
    {
        var times = new PrepTimes(_dir);
        times.Record("p1", TimeSpan.FromSeconds(20), T);
        Assert.Equal(TimeSpan.FromMinutes(20), times.Lead("p1"));
    }

    // ── 기록하는 쪽 (대상 묶음의 작업이 실제로 끝날 때마다) ─────────

    private sealed class Night
    {
        public DateTimeOffset Now = T;
        public readonly PrepTimes Times;
        public readonly PrepTimeRecorder Recorder;
        public readonly PrepContext Ctx;
        public readonly IReadOnlyList<IPrepTask> Tasks;

        public Night(string dir, bool hasGuider = true)
        {
            var o = new SimOptions { Speed = 0 };
            var f = new SimFaults();
            Tasks =
            [
                new SlewTask(new SimulatedSlewDevices(o, f)), new CenterTask(new SimulatedCenterDevices(o, f)),
                new FocusCheckTask(new SimulatedFocusDevices(o, f)), new GuidingTask(new SimulatedGuidingDevices(o, f)),
                new TestShotTask(new SimulatedTestShotDevices(o, f)),
            ];
            Times = new PrepTimes(dir);
            Recorder = new PrepTimeRecorder(Times, () => "p1");
            Ctx = new PrepContext(Harness.Plan(), new Site(37.82, 127.14), 2.41, hasGuider, true, new PrepResults(), new MountLock(),
                new InMemoryPrepMemory(), () => Now);
        }

        public void Done(string id, object? result = null, double atMinutes = 0)
        {
            Now = T.AddMinutes(atMinutes);
            Recorder.Finished(Tasks.Single(t => t.Id == id), result ?? new object(), Ctx, Tasks);
        }

        public SlewResult Moved(double atMinutes) => new(0.1, 40, T.AddMinutes(atMinutes + 2), MovedAt: T.AddMinutes(atMinutes));
    }

    [Fact]
    public void 이동부터_시험_사진까지_다_끝나면_걸린_시간을_남긴다()
    {
        var n = new Night(_dir);
        n.Done("slew", n.Moved(0), 3);
        n.Done("center", atMinutes: 8);
        n.Done("focuscheck", atMinutes: 10);
        n.Done("guiding", atMinutes: 17);
        n.Done("test", atMinutes: 26);
        Assert.Equal(TimeSpan.FromMinutes(26), n.Times.Lead("p1"));
    }

    [Fact]
    public void 가이더가_없으면_가이딩_없이도_남긴다()
    {
        var n = new Night(_dir, hasGuider: false);
        n.Done("slew", n.Moved(0), 3);
        n.Done("center", atMinutes: 8);
        n.Done("focuscheck", atMinutes: 10);
        n.Done("test", atMinutes: 15);
        Assert.Equal(TimeSpan.FromMinutes(15), n.Times.Lead("p1"));
    }

    [Fact]
    public void 건너뛴_작업이_있으면_남기지_않는다()
    {
        // [임시] 건너뛰기·이어서 하기로 넘긴 작업은 TaskFinished가 오지 않는다 → 가이딩이 빠짐
        var n = new Night(_dir);
        n.Done("slew", n.Moved(0), 3);
        n.Done("center", atMinutes: 8);
        n.Done("focuscheck", atMinutes: 10);
        n.Done("test", atMinutes: 26);
        Assert.Equal(TimeSpan.FromMinutes(20), n.Times.Lead("p1"));
    }

    [Fact]
    public void 지금_위치를_목표로_하면_남기지_않는다()
    {
        var n = new Night(_dir);
        n.Done("slew", new SlewResult(0, 40, T, AcceptedHere: true), 0);
        n.Done("center", atMinutes: 1);
        n.Done("focuscheck", atMinutes: 3);
        n.Done("guiding", atMinutes: 9);
        n.Done("test", atMinutes: 30);
        Assert.Equal(TimeSpan.FromMinutes(20), n.Times.Lead("p1"));
    }

    [Fact]
    public void 이동을_다시_하면_그때부터_다시_잰다()
    {
        // 극축 정렬을 다시 해 대상 작업을 처음부터 다시 하는 경우 등
        var n = new Night(_dir);
        n.Done("slew", n.Moved(0), 3);
        n.Done("center", atMinutes: 8);
        n.Done("slew", n.Moved(40), 43);
        n.Done("center", atMinutes: 47);
        n.Done("focuscheck", atMinutes: 49);
        n.Done("guiding", atMinutes: 55);
        n.Done("test", atMinutes: 64);
        Assert.Equal(TimeSpan.FromMinutes(24), n.Times.Lead("p1"));
    }

    [Fact]
    public async Task 러너는_실제로_끝난_작업을_차례로_알린다()
    {
        var evening = new DateOnly(2026, 10, 16);
        var (flow, _) = Harness.Flow();
        var results = flow.ResultsFor(evening);
        var memory = new InMemoryPrepMemory();
        flow.StartRig(Harness.RigContext(results, memory), evening);
        await flow.Rig.RunToReady();
        var finished = new List<string>();
        flow.Target.TaskFinished = (task, result, _) =>
        {
            finished.Add(task.Id);
            if (task.Id == "slew") Assert.NotNull(Assert.IsType<SlewResult>(result).MovedAt);
        };
        Assert.Null(flow.StartTarget(Harness.TargetContext(results, memory)));
        await flow.Target.RunToReady();
        Assert.Equal(["slew", "center", "focuscheck", "guiding", "test"], finished);
    }
}
