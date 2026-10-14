using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Astro.Server.Prepare.Real;

/// <summary>FITS 사진 읽기: 가이드 카메라 사진(PHD2 save_image, 16비트 회색)을 하늘 화면용 PNG로, 시험 사진(N.I.N.A. 저장 FITS)의 히스토그램·포화 비율</summary>
public static class Fits
{
    public static byte[]? ToPng(string path, int maxWidth = 960)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var (w, h, bitpix, bzero, dataStart) = Header(bytes);
            if (w <= 0 || h <= 0 || bitpix != 16 && bitpix != 8) return null;
            var step = Math.Max(1, (w + maxWidth - 1) / maxWidth);
            int ow = w / step, oh = h / step;
            var px = new ushort[ow * oh];
            for (var y = 0; y < oh; y++)
                for (var x = 0; x < ow; x++)
                {
                    var i = (y * step) * w + x * step;
                    px[y * ow + x] = bitpix == 16
                        ? (ushort)Math.Clamp(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(dataStart + i * 2, 2)) + bzero, 0, 65535)
                        : (ushort)(bytes[dataStart + i] * 257);
                }
            return Png.FromGray16(ow, oh, px);
        }
        catch (Exception e) when (e is IOException or ArgumentOutOfRangeException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>시험 사진 화소 통계: 히스토그램(0~65535를 칸으로 나눈 픽셀 수)과 포화 비율(%)</summary>
    public sealed record Pixels(int[] Histogram, double SaturatedPercent);

    /// <summary>
    /// 히스토그램과 포화 비율을 한 번 읽어 함께 (N.I.N.A. API는 히스토그램·포화를 주지 않음 — 통계만, 2026-10-09 확인).
    /// 포화 = 가장 밝은 값의 99% 이상인 화소 비율: 센서가 넘치면 같은 값에 몰리고, 넘친 곳이 없으면 가장 밝은 별 꼭대기 몇 점뿐이라 0에 가깝다.
    /// 센서 최대값(14비트 카메라가 16비트 칸에 담기는지 등)을 몰라도 된다. 4천만 화소 사진은 약 200만 점만 골라 센다 (모양은 같고 빠름). 못 읽으면 null
    /// </summary>
    public static Pixels? Analyze(string path, int bins = 256)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var (w, h, bitpix, bzero, dataStart) = Header(bytes);
            if (w <= 0 || h <= 0 || bitpix != 16 && bitpix != 8) return null;
            var n = (long)w * h;
            var step = (int)Math.Max(1, n / 2_000_000);
            if (step % 2 == 0) step++; // 컬러 센서(RGGB)에서 한 색만 고르지 않게 홀수 간격
            var each = new int[65536];
            long seen = 0;
            for (long i = 0; i < n; i += step, seen++)
            {
                var v = bitpix == 16
                    ? (int)Math.Clamp(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(dataStart + (int)(i * 2), 2)) + bzero, 0, 65535)
                    : bytes[dataStart + i] * 257;
                each[v]++;
            }
            var counts = new int[bins];
            var max = 0;
            for (var v = 0; v < each.Length; v++)
            {
                if (each[v] == 0) continue;
                counts[(int)((long)v * bins / 65536)] += each[v];
                max = v;
            }
            long saturated = 0;
            for (var v = (int)Math.Ceiling(max * 0.99); v <= max; v++) saturated += each[v];
            return new Pixels(counts, seen == 0 || max == 0 ? 0 : saturated * 100.0 / seen);
        }
        catch (Exception e) when (e is IOException or ArgumentOutOfRangeException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (int W, int H, int Bitpix, double Bzero, int DataStart) Header(byte[] b)
    {
        int w = 0, h = 0, bitpix = 0;
        double bzero = 0;
        for (var block = 0; block * 2880 < b.Length; block++)
        {
            for (var card = 0; card < 36; card++)
            {
                var o = block * 2880 + card * 80;
                var line = Encoding.ASCII.GetString(b, o, 80);
                var key = line[..8].Trim();
                var value = line.Length > 10 ? line[10..].Split('/')[0].Trim() : "";
                switch (key)
                {
                    case "BITPIX": bitpix = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "NAXIS1": w = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "NAXIS2": h = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "BZERO": bzero = double.Parse(value, CultureInfo.InvariantCulture); break;
                    case "END": return (w, h, bitpix, bzero, (block + 1) * 2880);
                }
            }
        }
        return (0, 0, 0, 0, 0);
    }
}
