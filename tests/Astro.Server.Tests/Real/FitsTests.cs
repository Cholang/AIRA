using System.Buffers.Binary;
using System.Text;
using Astro.Server.Prepare.Real;

namespace Astro.Server.Tests.Real;

/// <summary>시험 사진 화소 통계: 카메라 눈금(포화 값·바닥값), 배경, 히스토그램, 포화 비율 (2026-10-14~15)</summary>
public class FitsTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "aa-fits-" + Guid.NewGuid().ToString("N") + ".fits");
    public void Dispose() { try { File.Delete(_file); } catch (IOException) { } }

    /// <summary>16비트 FITS (BZERO 32768 — N.I.N.A.처럼 부호 없는 값을 담는 방식). instrument = INSTRUME</summary>
    private void Write(int w, int h, Func<int, int> value, string? instrument = null)
    {
        var cards = new List<string> { "SIMPLE  =                    T", "BITPIX  =                   16", "NAXIS   =                    2",
            $"NAXIS1  = {w,20}", $"NAXIS2  = {h,20}", "BZERO   =                32768" };
        if (instrument is not null) cards.Add($"INSTRUME= '{instrument}'");
        cards.Add("END");
        var header = new StringBuilder();
        foreach (var card in cards) header.Append(card.PadRight(80));
        while (header.Length % 2880 != 0) header.Append(' ');
        var data = new byte[(w * h * 2 + 2879) / 2880 * 2880];
        for (var i = 0; i < w * h; i++) BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(i * 2, 2), (short)(value(i) - 32768));
        File.WriteAllBytes(_file, [.. Encoding.ASCII.GetBytes(header.ToString()), .. data]);
    }

    [Fact]
    public void X_T5_원본은_14비트_눈금과_바닥_1024로_읽는다()
    {
        // 100×100 중 100개가 14비트 끝(16383)에 몰림 = 넘친 별, 나머지는 배경 1024 + 3072 (바닥~포화의 20%)
        Write(100, 100, i => i < 100 ? 16383 : 1024 + 3072, "Fujifilm X-T5");
        var px = Fits.Analyze(_file)!;
        Assert.Equal((16383, 1024), (px.White, px.Black));
        Assert.Equal(3072.0 / (16383 - 1024), px.Background, 3); // 65535로 나누던 때는 6%로 읽었다
        Assert.Equal(1.0, px.SaturatedPercent, 3);
        Assert.Equal(10_000, px.Histogram.Sum());
        Assert.Equal(9_900, px.Histogram[3072 * 256 / (16383 - 1024 + 1)]); // 가로 = 바닥~포화
    }

    [Fact]
    public void 넘친_곳이_없으면_표준_눈금이고_포화는_0()
    {
        // 가장 밝은 별 꼭대기 한 점뿐 → 넘치지 않음. 5000을 담는 표준 눈금 = 14비트
        Write(100, 100, i => i == 5 ? 5000 : 1000 + i % 50);
        var px = Fits.Analyze(_file)!;
        Assert.Equal(16383, px.White);
        Assert.Equal(0, px.SaturatedPercent);
    }

    [Fact]
    public void 십육비트_카메라는_65535가_포화_값()
    {
        Write(100, 100, i => i < 50 ? 65535 : 20000, "ZWO ASI2600MC Pro");
        var px = Fits.Analyze(_file)!;
        Assert.Equal((65535, 0), (px.White, px.Black));
        Assert.Equal(0.5, px.SaturatedPercent, 3);
        Assert.Equal(20000.0 / 65535, px.Background, 3);
    }

    [Fact]
    public void CX36_06_밝은_별이_없는_후지_사진도_14비트_눈금으로_읽는다()
    {
        // 최댓값 3000 하나, 배경 2024 — 12비트로 내려 읽으면 배경이 약 33%(실제 약 6.5%)
        Write(100, 100, i => i == 5 ? 3000 : 2024, "Fujifilm X-T5");
        var px = Fits.Analyze(_file)!;
        Assert.Equal((16383, 1024), (px.White, px.Black));
        Assert.Equal(1000.0 / (16383 - 1024), px.Background, 3);
    }

    [Fact]
    public void CX36_06_알려진_비트_수가_눈금의_바닥이고_넘으면_올린다()
    {
        Write(100, 100, i => i == 5 ? 3000 : 1000);
        Assert.Equal(16383, Fits.Analyze(_file, knownBits: 14)!.White);  // 어두워도 내리지 않는다
        Assert.Equal(4095, Fits.Analyze(_file)!.White);                  // 모르면 사진 값을 담는 표준 눈금 (그대로)
        Write(100, 100, i => i == 5 ? 30000 : 1000);
        Assert.Equal(65535, Fits.Analyze(_file, knownBits: 14)!.White);  // 설정보다 큰 값이 있으면 올린다
    }

    [Fact]
    public void CX36_06_눈금보다_한참_낮은_곳에_몰린_최댓값은_포화로_보지_않는다()
    {
        Write(100, 100, i => i < 3 ? 3000 : 2024, "Fujifilm X-T5");
        var px = Fits.Analyze(_file)!;
        Assert.Equal(16383, px.White);
        Assert.Equal(0, px.SaturatedPercent);
    }

    [Fact]
    public void 플랫_밝기는_바닥을_빼고_바닥에서_포화_사이로()
    {
        Assert.Equal(0.5, CameraScale.Fraction(1024 + (16383 - 1024) / 2.0, 16383, 1024), 6);
        Assert.Equal(0, CameraScale.Fraction(900, 16383, 1024));   // 바닥보다 어두우면 0
        Assert.Equal(1, CameraScale.Fraction(70000, 65535, 0));    // 넘으면 1
    }

    [Fact]
    public void 못_읽으면_null()
    {
        File.WriteAllText(_file, "not a fits");
        Assert.Null(Fits.Analyze(_file));
    }
}
