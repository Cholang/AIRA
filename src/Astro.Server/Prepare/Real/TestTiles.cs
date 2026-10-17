namespace Astro.Server.Prepare.Real;

/// <summary>
/// 시험 사진 9칸 확대 보기의 조각 (2026-10-16, DESIGN.md "하늘 화면"): 사진의 네 귀퉁이 · 네 변 가운데 · 한가운데에서 같은 크기의 정사각형을
/// 원본 화소 그대로 잘라(줄이지 않음 — 별 모양이 보이게) PNG로. 칸 순서는 화면에 보이는 방향으로 왼쪽 위부터 가로로 0~8 (StarField와 같음).
/// 컬러 센서의 색 무늬는 카메라 모델의 칸 평균(ColorBlock)으로 지우고(X-Trans·모름 6×6 — 무늬 한 주기, 3×3은 자리에 따라 섞임이 달라 가로줄이 남았다. 베이어 2×2, 모노 1),
/// 밝기는 아홉 칸 모두 사진 전체에서 정한 같은 늘이기로 — 칸끼리 견줄 수 있게. 늘이기 = 배경(중앙값)이 화면 밝기 15%쯤 오도록 (PixInsight STF와 같은 방식: 바닥을 중앙값 − 2.8 × 퍼짐에, 중간 밝기 곡선)
/// </summary>
public static class TestTiles
{
    /// <summary>조각 한 변 (원본 화소). 사진이 작으면 한 칸(사진의 1/3)까지</summary>
    public const int Side = 256;

    /// <summary>배경이 놓일 화면 밝기 (0~1)</summary>
    private const double TargetBackground = 0.15;

    /// <summary>아홉 조각 PNG (왼쪽 위부터). 사진이 너무 작으면 null</summary>
    public static byte[][]? Make(Fits.Image img)
    {
        var s = Math.Min(Side, Math.Min(img.Width, img.Height) / 3);
        if (s < 16) return null;
        var block = Equipment.Camera.CameraCatalog.Match(img.Instrument).ColorBlock;
        var stretch = Stretch(img, block);
        var tiles = new byte[9][];
        for (var k = 0; k < 9; k++)
        {
            // 이미 늘린 밝기를 그대로 — Png.FromGray16은 조각마다 다시 늘려 칸끼리 밝기가 달라지고 잡음이 커졌다
            var gray = Crop(img, k, s, stretch, block);
            var rgb = new byte[gray.Length * 3];
            for (var i = 0; i < gray.Length; i++) rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = gray[i];
            tiles[k] = Png.Encode(s, s, rgb);
        }
        return tiles;
    }

    /// <summary>칸 k(화면 방향 왼쪽 위부터)의 조각 s×s — 늘인 밝기 0~255, 화면에 보이는 방향</summary>
    internal static byte[] Crop(Fits.Image img, int k, int s, (double Low, double High, double Mid) stretch, int block = 6)
    {
        int col = k % 3, row = k / 3;
        var x0 = col == 0 ? 0 : col == 1 ? (img.Width - s) / 2 : img.Width - s;
        var y0 = row == 0 ? 0 : row == 1 ? (img.Height - s) / 2 : img.Height - s; // 화면 방향의 위에서부터
        var px = new byte[s * s];
        for (var y = 0; y < s; y++)
        {
            var dy = y0 + y;
            var fy = img.BottomUp ? img.Height - 1 - dy : dy; // BottomUp 사진은 파일의 첫 줄이 화면 아래
            for (var x = 0; x < s; x++)
                px[y * s + x] = (byte)Math.Round(255 * Curve(Box(img, x0 + x, fy, block), stretch.Low, stretch.High, stretch.Mid));
        }
        return px;
    }

    /// <summary>block×block 평균 (6이면 −2~+3, 2면 0~+1, 1이면 그 화소만. 가장자리는 안쪽 화소만)</summary>
    private static double Box(Fits.Image img, int x, int y, int block)
    {
        double sum = 0;
        var n = 0;
        int from = -((block - 1) / 2), to = from + block - 1;
        for (var dy = from; dy <= to; dy++)
        {
            var yy = y + dy;
            if (yy < 0 || yy >= img.Height) continue;
            for (var dx = from; dx <= to; dx++)
            {
                var xx = x + dx;
                if (xx < 0 || xx >= img.Width) continue;
                sum += img.At(xx, yy);
                n++;
            }
        }
        return sum / n;
    }

    /// <summary>
    /// 늘이기 값 — 조각과 같은 칸 평균 값으로 잰다(평균 전 화소로 재면 뜨거운 화소가 위를 끌어올려 별이 어두워졌다):
    /// 바닥(low) = 중앙값 − 2.8 × 퍼짐(중앙 절대 편차 × 1.4826), 위(high) = 가장 밝은 값, 중간 밝기(mid) = 배경이 TargetBackground에 오게 하는 곡선 값.
    /// 사진에서 약 30만 곳을 고르게 골라 잰다
    /// </summary>
    internal static (double Low, double High, double Mid) Stretch(Fits.Image img, int block = 6)
    {
        var stepX = Math.Max(1, (int)Math.Sqrt((double)img.Width * img.Height / 300_000));
        var sample = new List<double>();
        for (var y = 0; y < img.Height; y += stepX)
            for (var x = 0; x < img.Width; x += stepX)
                sample.Add(Box(img, x, y, block));
        sample.Sort();
        var median = sample[sample.Count / 2];
        var dev = new double[sample.Count];
        for (var i = 0; i < dev.Length; i++) dev[i] = Math.Abs(sample[i] - median);
        Array.Sort(dev);
        var spread = 1.4826 * dev[dev.Length / 2];
        var low = Math.Max(sample[0], median - 2.8 * spread);
        var high = Math.Max(sample[^1], low + 1);
        var x0 = Math.Clamp((median - low) / (high - low), 1e-6, 1 - 1e-6);
        return (low, high, Mtf(x0, TargetBackground));
    }

    internal static double Curve(double v, double low, double high, double mid) => Mtf(Math.Clamp((v - low) / (high - low), 0, 1), mid);

    /// <summary>중간 밝기 곡선 (Midtones Transfer Function): x를 m만큼 휘게 — m = 0.5면 그대로, MTF(x, MTF(x, t)) 꼴로 x를 t로 보낸다</summary>
    internal static double Mtf(double x, double m) =>
        x <= 0 ? 0 : x >= 1 ? 1 : (m - 1) * x / ((2 * m - 1) * x - m);
}
