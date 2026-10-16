using System.Text.Json;
using Astro.Server.Prepare.Tasks.TestShot;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// ⑦ 시험 사진 실장비: N.I.N.A.로 계획 노출 한 장(저장함 — image-history에 남음) → 통계(별 수·HFR·밝기)와 가이딩 오차.
/// 2026-10-06 실기(X-T5): 1초 노출에 결과까지 약 25초(RAW 84MB). 카메라 화질이 JPEG면 모든 값 0인 사진이 온다 → 받지 못한 것으로 본다.
/// 실패해도 이전 사진이 올 수 있어 NinaRig가 이벤트로 이번 사진인지 확인한다.
/// 히스토그램·포화 비율·배경 밝기는 저장된 FITS를 직접 읽어 센다 — 카메라의 실제 눈금(포화 값·바닥값)으로 (CameraScale, 그날 밤 플랫도 씀).
/// 별 모양(길쭉함·아홉 칸)은 N.I.N.A.가 주지 않아 ASTAP으로 별을 찾아 직접 잰다 (StarShapes — ASTAP이 없거나 실패하면 null, 판정·문구에서 빠짐).
/// 받은 사진은 같은 날짜 폴더의 "시험" 폴더로 옮긴다 (2026-10-14, 못 옮기면 SNAPSHOT 폴더에 그대로).
/// </summary>
public sealed class RealTestShotDevices(NinaRig rig, LiveImages live, ILogger<RealTestShotDevices>? log = null, CameraScale? scale = null) : ITestShotDevices
{
    private NinaRig.Shot? _shot;
    private DateTimeOffset _started = DateTimeOffset.MinValue;

    public async Task<bool> ExposeAsync(int exposureSeconds, int iso, Action<int> remaining, CancellationToken ct)
    {
        _started = DateTimeOffset.Now;
        // 시험 사진은 SNAPSHOT 폴더로 — 촬영 사진(LIGHT)과 섞여 스태킹에 들어가지 않게 (2026-10-08 실기: LIGHT 폴더에 시험 사진 두 장이 남음)
        _shot = await rig.CaptureAsync(exposureSeconds, solve: false, save: true, remaining, ct, gain: iso, imageType: "SNAPSHOT");
        // 카메라가 촬영을 받지 않았으면 노출 실패. 받았지만 내려받기에서 실패한 것은 DownloadAndAnalyzeAsync가 null로
        return _shot.Started;
    }

    public async Task<ShotStats?> DownloadAndAnalyzeAsync(DateTimeOffset since, CancellationToken ct)
    {
        if (_shot is not { Ok: true, Result: { } res } shot || shot.At < since) return null;
        if (await rig.LastStatsAsync(ct) is not { } s || s.Max <= 0) return null; // 전부 0 = JPEG 설정 등으로 사진 데이터가 빔
        if (res.TryGetProperty("Image", out var img) && img.ValueKind == JsonValueKind.String && img.GetBytesFromBase64() is { Length: > 0 } jpg) live.Set("test", jpg, "image/jpeg"); // 중간 문자열 없이 바로 바이트로
        var saved = await rig.LastSavedAsync(ct);
        var file = saved is { } f && f.Date >= since ? f.File : "";
        var rms = await rig.GuideRmsArcsecAsync(ct) ?? 0;
        // 히스토그램·포화·배경: 저장된 FITS를 직접 읽어 센다 (N.I.N.A.는 통계만 주고, 그 밝기는 "비트 수" 설정에 기대 틀릴 수 있다). 80MB 읽기라 따로
        var photo = file.Length > 0 && File.Exists(file) ? await Task.Run(() => Fits.Open(file), ct) : null;
        var px = photo is null ? null : await Task.Run(() => Fits.Analyze(photo), ct);
        if (px is not null) scale?.Learn(px.White, px.Black);
        // 별 모양: 시험 폴더로 옮기기 전에 (ASTAP이 사진 옆에 별 목록을 쓴다)
        var shape = photo is null ? null : await StarShapesAsync(file, photo, ct);
        if (file.Length > 0 && File.Exists(file)) file = MoveToTestFolder(file);
        // 파일을 못 읽었으면 N.I.N.A. 중앙값을 아는 눈금(없으면 16비트)으로
        var background = px?.Background ?? (scale?.Current is { } c ? CameraScale.Fraction(s.Median, c.White, c.Black) : s.Median / 65535.0);
        return new ShotStats(s.Hfr, shape?.Eccentricity, background, px?.SaturatedPercent ?? 0, rms, file, px?.Histogram, shape?.Field);
    }

    private async Task<StarShapes.Result?> StarShapesAsync(string file, Fits.Image photo, CancellationToken ct)
    {
        if (StarShapes.CliPath(await rig.AstapPathAsync(ct)) is not { } cli)
        {
            log?.LogWarning("ASTAP(astap_cli.exe)을 찾지 못해 시험 사진의 별 모양을 재지 않음");
            return null;
        }
        if (await StarShapes.ExtractAsync(cli, file, ct) is not { Count: > 0 } stars)
        {
            log?.LogWarning("ASTAP이 시험 사진에서 별을 찾지 못함: {File}", file);
            return null;
        }
        var r = await Task.Run(() => StarShapes.Measure(photo, stars), ct);
        log?.LogInformation("시험 사진 별 모양: 별 {Stars}개, 이심률 {Ecc:F2}, 방향 모임 {Align:F2}, 칸별 HFD {Tiles}", r.Stars, r.Eccentricity, r.Field.Alignment,
            string.Join(" ", r.Field.Tiles.Select(t => t.Hfd is { } h ? h.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "-")));
        return r;
    }

    private string MoveToTestFolder(string file)
    {
        try { return ImageFolders.Move(file, "SNAPSHOT", ImageFolders.Test); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.LogWarning(e, "시험 사진을 시험 폴더로 옮기지 못해 SNAPSHOT 폴더에 둠: {File}", file);
            return file;
        }
    }

    public Task<bool> StopAsync(CancellationToken ct) => rig.AbortExposureAsync(ct);

    public async Task<TestShotEndState> ReadEndStateAsync(CancellationToken ct)
    {
        var saved = await rig.LastSavedAsync(ct);
        return new TestShotEndState(await rig.CameraExposingAsync(ct), await GuidingAsync(ct), saved is { } f && f.Date >= _started);
    }

    private async Task<bool> GuidingAsync(CancellationToken ct) => await rig.GuideRmsArcsecAsync(ct) is not null;
}
