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

    [Fact]
    public async Task CX30_03_다시_하기도_한_번만_받는다()
    {
        var log = new List<string>();
        var tasks = Ids.ToDictionary(id => id, id => new FakeTask(id, log));
        tasks["polar"].EndStateOk = false; // 끝 상태가 어긋나 실패 → 이 작업 다시 하기
        var runner = new PrepareRunner(Ids.Select(id => (IPrepTask)tasks[id]), NullLogger<PrepareRunner>.Instance);
        runner.Start(Harness.Context());
        await runner.Until(x => x.Has("redo:polar"), "이 작업 다시 하기 버튼");

        Assert.Null(runner.Act("redo:polar"));
        Assert.NotNull(runner.Act("redo:polar")); // 정지 확인을 기다리는 동안 한 번 더 눌러도 받지 않는다
        for (var i = 0; i < 100; i++) await Task.Delay(10);
        lock (log) Assert.Equal(2, log.Count(l => l.StartsWith("run:polar:"))); // 처음 1번 + 다시 시도 1번
    }

    [Fact]
    public void PHD2_카메라_자리_번호를_읽는다()
    {
        var m = Astro.Server.Engine.Phd2CameraRepoint.UsbId.Match(@"\\?\usb#vid_0547&pid_15b3#7&2a0686fb&0&1#{a5dcbf10-6530-11d2-901f-00c04fb951ed}");
        Assert.True(m.Success);
        Assert.Equal("7&2a0686fb&0&1", m.Groups[3].Value);
        Assert.False(Astro.Server.Engine.Phd2CameraRepoint.UsbId.Match("ZWO ASI120MM Mini").Success); // USB 자리 번호가 없는 이름 (CX-32-02)
    }

    [Fact]
    public void CX30_08_설정_저장이_실패하면_앱_안의_값도_그대로()
    {
        var root = Path.Combine(Path.GetTempPath(), "aira-settings-" + Guid.NewGuid().ToString("N"));
        var s = new UserSettings(root);
        s.SetSharpCapExe(@"C:\A\SharpCap.exe");
        Directory.CreateDirectory(Path.Combine(root, "settings.json.tmp")); // 임시 파일 자리를 막아 저장 실패를 만든다
        Assert.ThrowsAny<Exception>(() => s.SetSharpCapExe(@"C:\B\SharpCap.exe"));
        Assert.Equal(@"C:\A\SharpCap.exe", s.SharpCapExe);
    }
}
