using System.Buffers.Binary;
using System.Text;
using Astro.Server.Prepare.Real;

namespace Astro.Server.Tests.Real;

/// <summary>시험 사진 화소 통계: 히스토그램과 포화 비율 (2026-10-14)</summary>
public class FitsTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "aa-fits-" + Guid.NewGuid().ToString("N") + ".fits");
    public void Dispose() { try { File.Delete(_file); } catch (IOException) { } }

    /// <summary>16비트 FITS (BZERO 32768 — N.I.N.A.처럼 부호 없는 값을 담는 방식)</summary>
    private void Write(int w, int h, Func<int, int> value)
    {
        var header = new StringBuilder();
        foreach (var card in new[] { "SIMPLE  =                    T", "BITPIX  =                   16", "NAXIS   =                    2",
                     $"NAXIS1  = {w,20}", $"NAXIS2  = {h,20}", "BZERO   =                32768", "END" })
            header.Append(card.PadRight(80));
        while (header.Length % 2880 != 0) header.Append(' ');
        var data = new byte[(w * h * 2 + 2879) / 2880 * 2880];
        for (var i = 0; i < w * h; i++) BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(i * 2, 2), (short)(value(i) - 32768));
        File.WriteAllBytes(_file, [.. Encoding.ASCII.GetBytes(header.ToString()), .. data]);
    }

    [Fact]
    public void 넘친_화소_비율을_센다_14비트_카메라도()
    {
        // 100×100 중 100개가 14비트 최대값(16383)에 몰림 = 1%
        Write(100, 100, i => i < 100 ? 16383 : 1000);
        var px = Fits.Analyze(_file)!;
        Assert.Equal(1.0, px.SaturatedPercent, 3);
        Assert.Equal(10_000, px.Histogram.Sum());
        Assert.Equal(9_900, px.Histogram[1000 * 256 / 65536]);
    }

    [Fact]
    public void 넘친_곳이_없으면_0에_가깝다()
    {
        // 가장 밝은 별 꼭대기 한 점뿐
        Write(100, 100, i => i == 5 ? 5000 : 1000 + i % 50);
        Assert.True(Fits.Analyze(_file)!.SaturatedPercent < 0.05);
    }

    [Fact]
    public void 못_읽으면_null()
    {
        File.WriteAllText(_file, "not a fits");
        Assert.Null(Fits.Analyze(_file));
    }
}
