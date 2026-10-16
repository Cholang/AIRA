using System.Text;
using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Real;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.TestShot;

namespace Astro.Server.Tests.Prepare;

/// <summary>노출 맞추기 (2026-10-17 구현 — 2026-10-15 합의한 ⑦ 절차)</summary>
public class ExposureMatchTests
{
    [Theory]
    [InlineData(0.07, 0.4, 120, true, null)]   // 알맞음 → 그대로
    [InlineData(0.28, 0.4, 120, true, 30)]     // 달빛으로 4배 밝음 → 줄임
    [InlineData(0.02, 0.4, 60, true, 210)]     // 어두움 → 늘림 (가이드 있으면 300초까지)
    [InlineData(0.02, 0.4, 30, false, 60)]     // 가이드 없으면 60초까지
    [InlineData(0.02, 0.4, 60, false, null)]   // 이미 상한 → 더 늘리지 않음
    [InlineData(0.02, 0.4, 120, false, null)]  // 사용자가 상한보다 길게 정했으면 그대로 (늘리지도 줄이지도 않음)
    [InlineData(0.07, 2.0, 120, true, 90)]     // 배경은 알맞아도 포화가 많으면 줄임
    [InlineData(0.50, 0.4, 30, true, 10)]      // 아무리 밝아도 10초 아래로는 줄이지 않음
    [InlineData(0.50, 0.4, 10, true, null)]    // 이미 하한
    [InlineData(0.50, 0.4, 5, true, null)]     // 하한보다 짧게 정했어도 밝은데 늘리지는 않음
    public void 배경으로_노출을_정한다(double background, double saturated, int exposure, bool guided, int? expected) =>
        Assert.Equal(expected, ExposureMatch.Next(background, saturated, exposure, guided));

    [Fact]
    public void 노출을_바꾸면_총_노출이_같게_장수도_바꾼다()
    {
        var ctx = Harness.Context(plan: Harness.Plan() with { ExposureSeconds = 120, EstimatedFrames = 30 });
        ctx.ChangeExposure(60);
        Assert.Equal(60, ctx.ExposureSeconds);
        Assert.Equal(60, ctx.Plan.ExposureSeconds);
        Assert.Equal(60, ctx.Plan.EstimatedFrames);
    }

    /// <summary>화면 통로: 버튼은 늘 주 버튼</summary>
    private sealed class PrimaryRun(PrepContext ctx) : ITaskRun
    {
        public List<string> Guides { get; } = [];
        public PrepContext Context => ctx;
        public int RunId => 1;
        public void SubStep(int index) { }
        public void Guide(string title, string text) { lock (Guides) Guides.Add(title + " | " + text); }
        public void Readout(string kind, string big, string caption, Tone tone, IReadOnlyDictionary<string, double>? values = null, bool live = false, bool unverified = false, DateTimeOffset? observedAt = null) { }
        public void ClearReadout() { }
        public void Status(string? text, Tone tone = Tone.Busy) { }
        public void Live(string kind, string? url = null, object? data = null) { }
        public Task<string> AskAsync(IReadOnlyList<PrepAction> actions, CancellationToken ct) => Task.FromResult(actions.First(a => a.Primary).Id);
    }

    [Fact]
    public async Task 밝은_하늘이면_묻지_않고_노출을_줄여_한_장_더_찍는다()
    {
        var faults = new SimFaults();
        faults.Arm("test.bright");
        var ctx = Harness.Context(plan: Harness.Plan() with { EstimatedFrames = 40 });
        var run = new PrimaryRun(ctx);
        var outcome = await new TestShotTask(new SimulatedTestShotDevices(new SimOptions { Speed = 0 }, faults)).RunAsync(run, CancellationToken.None).WaitAsync(Harness.Timeout);
        var done = Assert.IsType<Completed>(outcome);
        Assert.Equal(30, ctx.ExposureSeconds);
        Assert.Equal(160, ctx.Plan.EstimatedFrames); // 120초 × 40장 = 30초 × 160장
        Assert.Contains(run.Guides, g => g.StartsWith("노출을 맞추고 있어요"));
        Assert.Contains("120초에서 30초로", done.GuideText);
    }

    /// <summary>X-T5 원본 모양의 작은 FITS (14비트 값, BZERO 32768) — 배경 4096에 14비트 위로 넘친 별 중심 몇 점</summary>
    private static string XT5Fits(int background, int strays)
    {
        const int w = 100, h = 100;
        var header = new StringBuilder();
        void Card(string s) => header.Append(s.PadRight(80));
        Card("SIMPLE  =                    T");
        Card("BITPIX  =                   16");
        Card("NAXIS   =                    2");
        Card($"NAXIS1  = {w,20}");
        Card($"NAXIS2  = {h,20}");
        Card("BZERO   =                32768");
        Card("INSTRUME= 'X-T5'");
        Card("END");
        var head = Encoding.ASCII.GetBytes(header.ToString().PadRight(2880));
        var data = new byte[(w * h * 2 + 2879) / 2880 * 2880];
        for (var i = 0; i < w * h; i++)
        {
            var v = i < strays ? 65535 : background;
            var stored = (short)(v - 32768);
            data[i * 2] = (byte)(stored >> 8);
            data[i * 2 + 1] = (byte)stored;
        }
        var path = Path.Combine(Path.GetTempPath(), $"aira-xt5-{Guid.NewGuid():N}.fits");
        File.WriteAllBytes(path, [.. head, .. data]);
        return path;
    }

    [Fact]
    public void 후지_원본에_14비트_위_값이_조금_있어도_눈금은_14비트()
    {
        var path = XT5Fits(4096, strays: 3);
        try
        {
            var px = Fits.Analyze(path)!;
            Assert.Equal(16383, px.White);
            Assert.Equal(1024, px.Black);
            Assert.InRange(px.Background, 0.19, 0.21); // (4096 − 1024) / (16383 − 1024) = 20% — 전에는 16비트·바닥 0으로 6%
        }
        finally { File.Delete(path); }
    }
}
