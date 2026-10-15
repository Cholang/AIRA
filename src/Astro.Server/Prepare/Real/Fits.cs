using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Astro.Server.Prepare.Real;

/// <summary>FITS 사진 읽기: 가이드 카메라 사진(PHD2 save_image, 16비트 회색)을 하늘 화면용 PNG로, 시험 사진(N.I.N.A. 저장 FITS)의 히스토그램·포화 비율·배경·카메라 눈금</summary>
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

    /// <summary>
    /// 시험 사진 화소 통계: 히스토그램(바닥~포화를 칸으로 나눈 픽셀 수), 포화 비율(%), 배경 밝기(바닥 위 0~1), 카메라 눈금(포화되는 값·바닥값)
    /// </summary>
    public sealed record Pixels(int[] Histogram, double SaturatedPercent, double Background, int White, int Black);

    /// <summary>
    /// 한 번 읽어 함께 센다 (N.I.N.A. API는 히스토그램·포화를 주지 않고, 통계의 밝기는 N.I.N.A. "비트 수" 설정에 기대 틀릴 수 있다 — CameraScale).
    /// 눈금은 사진에서 알아낸다: 가장 밝은 값에 여러 점이 몰려 있으면 센서가 넘친 자리라 그 값이 포화 값(White), 아니면 그 값을 담는 표준 눈금(12·14·16비트).
    /// 바닥값(Black)은 카메라별 — 후지 14비트 원본은 1024(2026-10-15 X-T5 원본 FITS로 확인), 모르는 카메라는 0 [임시].
    /// 포화 = 포화 값 바로 아래(바닥~포화의 1%)까지의 화소 비율, 넘친 곳이 없으면 0. 4천만 화소 사진은 약 200만 점만 골라 센다 (모양은 같고 빠름). 못 읽으면 null
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
            if (seen == 0) return null;
            var max = Array.FindLastIndex(each, c => c > 0);
            var clipped = max > 0 && each[max] >= 3; // 같은 최댓값에 여러 점 = 센서가 넘친 자리
            var white = clipped ? max : max <= 4095 ? 4095 : max <= 16383 ? 16383 : 65535;
            var black = BlackLevel(Instrument(bytes), white);

            var counts = new int[bins];
            long below = 0, saturated = 0;
            var median = -1;
            var satFrom = white - (white - black) / 100;
            for (var v = 0; v <= max; v++)
            {
                if (each[v] == 0) continue;
                counts[Math.Clamp((int)((long)(v - black) * bins / (white - black + 1)), 0, bins - 1)] += each[v];
                below += each[v];
                if (median < 0 && below * 2 >= seen) median = v;
                if (clipped && v >= satFrom) saturated += each[v];
            }
            return new Pixels(counts, saturated * 100.0 / seen, CameraScale.Fraction(median, white, black), white, black);
        }
        catch (Exception e) when (e is IOException or ArgumentOutOfRangeException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>바닥값(블랙 레벨). 후지 14비트 원본 = 1024 (빛이 거의 없는 X-T5 사진의 중앙값 1075·최솟값 942). 그 밖에는 모름 → 0 [임시] (16비트 카메라는 바닥이 작아 영향이 작다)</summary>
    private static int BlackLevel(string instrument, int white) =>
        white <= 16383 && (instrument.Contains("fuji", StringComparison.OrdinalIgnoreCase) || instrument.StartsWith("X-", StringComparison.OrdinalIgnoreCase)
            || instrument.StartsWith("GFX", StringComparison.OrdinalIgnoreCase)) ? 1024 : 0;

    /// <summary>머리글의 카메라 이름 (INSTRUME). 없으면 빈 문자열</summary>
    private static string Instrument(byte[] b)
    {
        for (var o = 0; o + 80 <= Math.Min(b.Length, 2880 * 10); o += 80)
        {
            var line = Encoding.ASCII.GetString(b, o, 80);
            if (line.StartsWith("END", StringComparison.Ordinal)) break;
            if (line.StartsWith("INSTRUME", StringComparison.Ordinal)) return line[10..].Split('/')[0].Trim().Trim('\'').Trim();
        }
        return "";
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
