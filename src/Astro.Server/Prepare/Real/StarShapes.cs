using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Astro.Server.Prepare.Tasks.TestShot;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// 시험 사진의 별 모양 (2026-10-16, OPERATION_PROCEDURE.md ⑦). 별은 솔빙에 쓰는 ASTAP의 `astap_cli -extract`로 찾고(위치·HFD·SNR —
/// X-T5 원본 FITS를 그대로 읽는다, 80MB 한 장 약 1초), 별마다 길쭉함(이심률)·방향은 사진 화소의 2차 모멘트로 잰다.
/// 모멘트 전에 3×3 평균(모노 센서는 그대로 — 카메라 내부 프로필 ShapeSmoothing) — 컬러 센서의 색 무늬가 모양을 흔들지 않게 (X-Trans는 3×3 안에 R·G·B가 고르게, 베이어도 거의 고르게 든다).
/// 일부러 늘인 가상 별(긴 축 1.2~2배)에서 이심률 오차 0.02 안, 방향 1° 안. X-T5 별자리 사진(56mm f/2.8, 1.3초): 별 99개, 가운데 칸 0.34, 귀퉁이 0.5~0.6, 방향 제각각.
/// </summary>
public static class StarShapes
{
    /// <summary>ASTAP 별 하나: 위치(1부터, 파일 순서), 크기(HFD, px), 신호 대 잡음</summary>
    public sealed record Star(double X, double Y, double Hfd, double Snr);

    /// <summary>
    /// Eccentricity = 가운데 칸 별들의 이심률 중앙값(가운데 별이 적으면 전체) — 귀퉁이는 광학 때문에 길쭉할 수 있어
    /// 흐름(가이드·추적) 판정은 가운데로 하고, 귀퉁이는 아홉 칸 판정에서 본다. 잰 별이 없으면 null
    /// </summary>
    public sealed record Result(int Stars, double? Eccentricity, StarField Field);

    private const double SnrMin = 10;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>astap_cli.exe 위치: N.I.N.A. 프로필의 ASTAP(astap.exe)과 같은 폴더, 없으면 기본 설치 폴더. 못 찾으면 null</summary>
    public static string? CliPath(string? astapExe)
    {
        if (!string.IsNullOrWhiteSpace(astapExe) && Path.GetDirectoryName(astapExe) is { Length: > 0 } dir && File.Exists(Path.Combine(dir, "astap_cli.exe")))
            return Path.Combine(dir, "astap_cli.exe");
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "astap", "astap_cli.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// 별 목록. ASTAP은 사진 옆에 같은 이름의 .csv를 쓴다(-o로 바꿀 수 없음) → 실행 전에 없던 것만, 이번에 쓴 것만 읽고 지운다.
    /// 실행 실패·시간 초과(60초)·별 목록이 없으면 null
    /// </summary>
    public static async Task<IReadOnlyList<Star>?> ExtractAsync(string cli, string fits, CancellationToken ct)
    {
        var csv = Path.ChangeExtension(fits, ".csv");
        // 같은 이름의 CSV가 이미 있으면 사용자 것일 수 있다 — 읽지도 지우지도 않고 별 모양을 건너뛴다 (CX-38-04)
        if (File.Exists(csv)) return null;
        var started = DateTime.UtcNow.AddSeconds(-2); // 파일 시각의 거친 눈금
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(cli)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(fits) ?? "",
            },
        };
        foreach (var a in new[] { "-f", fits, "-extract", SnrMin.ToString(Inv) }) p.StartInfo.ArgumentList.Add(a);
        try
        {
            p.Start();
            // 출력은 읽어 버린다 (버퍼가 차서 멈추지 않게)
            _ = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
            _ = p.StandardError.ReadToEndAsync(CancellationToken.None);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Limit);
            await p.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(p);
            ct.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return null;
        }
        try
        {
            if (!File.Exists(csv) || File.GetLastWriteTimeUtc(csv) < started) return null;
            var stars = Parse(File.ReadLines(csv));
            File.Delete(csv);
            return stars;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>ASTAP CSV: 머리줄 "x,y,hfd,snr,flux,…" 뒤로 별마다 한 줄 (소수점은 점)</summary>
    public static List<Star> Parse(IEnumerable<string> lines)
    {
        var stars = new List<Star>();
        foreach (var line in lines.Skip(1))
        {
            var f = line.Split(',');
            if (f.Length >= 4 && Num(f[0]) is { } x && Num(f[1]) is { } y && Num(f[2]) is { } hfd && Num(f[3]) is { } snr && hfd > 0)
                stars.Add(new(x, y, hfd, snr));
        }
        return stars;
    }

    /// <summary>별마다 모양을 재 아홉 칸으로 모은다 (별 수·크기는 ASTAP이 찾은 모든 별, 길쭉함은 잴 수 있는 별 — 가장자리에 붙은 별은 뺀다)</summary>
    public static Result Measure(Fits.Image img, IReadOnlyList<Star> stars)
    {
        var all = stars.Select(s => (Tile: TileOf(img, s), s.Hfd, Shape: Shape(img, s))).ToList();
        var tiles = Enumerable.Range(0, 9).Select(k =>
        {
            var t = all.Where(q => q.Tile == k).ToList();
            var ecc = t.Where(q => q.Shape is not null).Select(q => q.Shape!.Value.Ecc).ToList();
            return new StarTile(t.Count, t.Count > 0 ? PrepTimes.Median([.. t.Select(q => q.Hfd)]) : null, ecc.Count > 0 ? PrepTimes.Median(ecc) : null);
        }).ToArray();
        var shaped = all.Where(q => q.Shape is not null).ToList();
        var center = shaped.Where(q => q.Tile == 4).ToList();
        var core = (center.Count >= StarFieldRules.MinStars ? center : shaped).Select(q => q.Shape!.Value).ToList();
        double? eccentricity = core.Count > 0 ? PrepTimes.Median([.. core.Select(q => q.Ecc)]) : null;
        return new Result(stars.Count, eccentricity, new StarField(tiles, Alignment(core.Select(q => q.Angle))));
    }

    /// <summary>화면에 보이는 방향의 칸 (0 왼쪽 위 ~ 8 오른쪽 아래). BottomUp 사진은 파일의 첫 줄이 화면 아래</summary>
    private static int TileOf(Fits.Image img, Star s)
    {
        var col = Math.Clamp((int)((s.X - 1) * 3 / img.Width), 0, 2);
        var row = Math.Clamp((int)((s.Y - 1) * 3 / img.Height), 0, 2);
        return (img.BottomUp ? 2 - row : row) * 3 + col;
    }

    /// <summary>
    /// 별 하나의 이심률(0 둥긂 ~ 1)과 긴 축 방향(라디안). 별 둘레 반지름 r = HFD × 2(5~48px — 늘어난 별의 긴 축 끝까지) 안의 화소를 배경 위 밝기로 가중한 2차 모멘트.
    /// 배경·잡음은 그 바깥 6px 고리의 중앙값·중앙 절대 편차, 잡음의 3배 아래 화소는 뺀다. 가장자리에 붙었거나 잴 화소가 모자라면 null
    /// </summary>
    private static (double Ecc, double Angle)? Shape(Fits.Image img, Star s)
    {
        var r = (int)Math.Clamp(Math.Round(2 * s.Hfd), 5, 48);
        var k = Camera.CameraCatalog.Match(img.Instrument).ShapeSmoothing; // 컬러 1(3×3), 모노 0
        var area = (2 * k + 1) * (2 * k + 1);
        var R = r + 6;
        int cx = (int)Math.Round(s.X - 1), cy = (int)Math.Round(s.Y - 1);
        if (cx - R - 1 < 0 || cy - R - 1 < 0 || cx + R + 1 >= img.Width || cy + R + 1 >= img.Height) return null;
        var n = 2 * R + 1;
        var v = new double[n * n];
        var ring = new List<double>();
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                double sum = 0;
                for (var dy = -k; dy <= k; dy++)
                    for (var dx = -k; dx <= k; dx++) sum += img.At(cx - R + i + dx, cy - R + j + dy);
                v[j * n + i] = sum / area;
                var d2 = (i - R) * (i - R) + (j - R) * (j - R);
                if (d2 > r * r && d2 <= R * R) ring.Add(v[j * n + i]);
            }
        var bg = PrepTimes.Median(ring);
        var noise = 1.4826 * PrepTimes.Median([.. ring.Select(q => Math.Abs(q - bg))]);
        double sw = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        var used = 0;
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                if ((i - R) * (i - R) + (j - R) * (j - R) > r * r) continue;
                var w = v[j * n + i] - bg;
                if (w <= 3 * noise || w <= 0) continue;
                double x = i - R, y = j - R;
                sw += w; sx += w * x; sy += w * y; sxx += w * x * x; syy += w * y * y; sxy += w * x * y;
                used++;
            }
        if (used < 5 || sw <= 0) return null;
        double mx = sx / sw, my = sy / sw;
        double cxx = sxx / sw - mx * mx, cyy = syy / sw - my * my, cxy = sxy / sw - mx * my;
        double t = (cxx + cyy) / 2, d = Math.Sqrt((cxx - cyy) * (cxx - cyy) / 4 + cxy * cxy);
        double l1 = t + d, l2 = t - d;
        if (l1 <= 0) return null;
        return (Math.Sqrt(Math.Max(0, 1 - l2 / l1)), 0.5 * Math.Atan2(2 * cxy, cxx - cyy));
    }

    /// <summary>방향이 한쪽으로 모인 정도 0~1 (긴 축은 앞뒤가 없어 각도를 두 배로 해서 평균 — 원형 통계의 축 방향 평균 길이)</summary>
    private static double Alignment(IEnumerable<double> angles)
    {
        double c = 0, s = 0;
        var n = 0;
        foreach (var a in angles) { c += Math.Cos(2 * a); s += Math.Sin(2 * a); n++; }
        return n == 0 ? 0 : Math.Sqrt(c * c + s * s) / n;
    }

    private static double? Num(string s) => double.TryParse(s, NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : null;

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
    }
}
