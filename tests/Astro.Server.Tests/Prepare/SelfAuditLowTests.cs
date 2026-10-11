using Astro.Server.Prepare.Flow;
using Astro.Server.Setup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>2026-10-11 자체 점검 Low: 다음 버튼 두 번(L1) · 설정 파일 안전하게 쓰기(L2)</summary>
public class SelfAuditLowTests
{
    private static readonly string[] Ids = ["polar", "calibration", "slew"];

    [Fact]
    public async Task L1_다음_버튼이_거의_동시에_두_번_와도_한_번만_받는다()
    {
        var log = new List<string>();
        var tasks = Ids.ToDictionary(id => id, id => new FakeTask(id, log));
        var runner = new PrepareRunner(Ids.Select(id => (IPrepTask)tasks[id]), NullLogger<PrepareRunner>.Instance);
        runner.Start(Harness.Context());
        await runner.Until(x => x.Has("next"), "첫 작업 끝");

        Assert.Null(runner.Act("next"));
        Assert.NotNull(runner.Act("next")); // 두 번째는 받지 않는다

        await runner.Until(x => x.Current?.TaskId == "calibration", "둘째 작업");
        for (var i = 0; i < 50; i++) await Task.Delay(10);
        lock (log) Assert.Single(log, l => l.StartsWith("run:calibration:"));
    }

    [Fact]
    public void L2_설정은_임시_파일에_쓴_뒤_바꾼다()
    {
        var root = Path.Combine(Path.GetTempPath(), "aira-settings-" + Guid.NewGuid().ToString("N"));
        var s = new UserSettings(root);
        s.SetSharpCapExe(@"C:\X\SharpCap.exe");
        Assert.True(File.Exists(Path.Combine(root, "settings.json")));
        Assert.False(File.Exists(Path.Combine(root, "settings.json.tmp")));
        Assert.Equal(@"C:\X\SharpCap.exe", new UserSettings(root).SharpCapExe);
    }
}
