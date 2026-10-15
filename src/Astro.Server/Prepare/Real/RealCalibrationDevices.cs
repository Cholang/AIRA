using System.Text.Json;
using Astro.Server.Prepare.Tasks.Calibration;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// ② 캘리브레이션 실장비: 위치(시간각·적위)로 이동은 N.I.N.A. slew(ra는 도 — 지금 항성시 − 시간각), 별 선택·캘리브레이션은 PHD2 직접.
/// 2026-10-05 실기: 위치 A·B 도착 오차 0.38°. 캘리브레이션 자체(별 필요)는 맑은 날 확인.
/// 끝 상태 약속: 보정값 있음 + PHD2 가이딩·루프 아님 → 캘리브레이션이 끝나면 PHD2를 멈춘다.
/// </summary>
public sealed class RealCalibrationDevices(NinaRig rig, Phd2Client phd2) : ICalibrationDevices
{
    private static readonly int[] ExposuresMs = [2000, 3000, 4000];

    public async Task<SlewOutcome> SlewToHourAngleAsync(double hourAngle, double decDeg, CancellationToken ct)
    {
        if (await rig.MountAsync(ct) is not { } m) return new(false, 0, "적도의가 응답하지 않습니다");
        var ra = ((m.SiderealHours - hourAngle) * 15 % 360 + 360) % 360;
        // 시작 요청 실패·도착 확인 실패는 멈춤을 뜻하지 않는다 → 멈추고 멈춘 것을 확인 (2026-10-14 자체 리뷰: 움직이는 채로 다음 위치로 이동 명령을 보냈다)
        if (await rig.StartSlewAsync(ra, decDeg, ct) is { } problem)
            return new(false, 0, problem, StopUnconfirmed: !await rig.StopSlewAndConfirmAsync(ct));
        var arrived = await rig.WaitSlewAsync(ra, decDeg, null, TimeSpan.FromMinutes(4), ct);
        return arrived is { } d ? new(true, d) : new(false, 0, "이동이 끝나지 않았습니다", StopUnconfirmed: !await rig.StopSlewAndConfirmAsync(ct));
    }

    public async Task<StarPick> SelectStarAsync(CancellationToken ct)
    {
        try
        {
            foreach (var ms in ExposuresMs)
            {
                await phd2.CallAsync("set_exposure", new object[] { ms }, ct);
                await phd2.CallAsync("loop", ct: ct);
                await Task.Delay(ms * 2 + 1500, ct); // 사진 두 장쯤
                try
                {
                    await phd2.CallAsync("find_star", ct: ct, timeout: TimeSpan.FromSeconds(30));
                    return new StarPick(true, double.NaN, ms / 1000.0); // SNR은 가이드 중에만 나온다
                }
                catch (Phd2Exception) { /* 별 없음 → 노출 늘려 다시 */ }
            }
            await StopPhd2Async(ct); // 멈춘 것까지 확인 (stop_capture는 곧바로 Stopped가 되지 않음)
            return new StarPick(false, 0, ExposuresMs[^1] / 1000.0);
        }
        catch (Phd2Exception)
        {
            return new StarPick(false, 0, 0);
        }
    }

    public async Task<CalibrationData> CalibrateAsync(Action<string, int> step, CancellationToken ct)
    {
        using var events = await phd2.SubscribeAsync(ct);
        var warnings = new List<string>();
        try
        {
            await phd2.CallAsync("clear_calibration", new object[] { "mount" }, ct);
            await phd2.CallAsync("guide", new { settle = new { pixels = 1.5, time = 8, timeout = 60 }, recalibrate = true }, ct);
        }
        catch (Phd2Exception e)
        {
            return new CalibrationData(false, 0, 0, 0, [], e.Message);
        }
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(8);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                if (await events.WaitAsync(["Calibrating", "CalibrationComplete", "CalibrationFailed", "Alert"], deadline - DateTime.UtcNow, ct) is not { } e) break;
                switch (Phd2Events.Name(e))
                {
                    case "Calibrating":
                        var dir = e.TryGetProperty("Direction", out var d) ? d.GetString() ?? "" : "";
                        step(dir.StartsWith("N", StringComparison.OrdinalIgnoreCase) || dir.StartsWith("S", StringComparison.OrdinalIgnoreCase) ? "north" : "west",
                            e.TryGetProperty("Step", out var s) ? s.GetInt32() : 0);
                        break;
                    case "Alert":
                        if (e.TryGetProperty("Msg", out var msg) && msg.GetString() is { } text) warnings.Add(text);
                        break;
                    case "CalibrationFailed":
                        await StopPhd2Async(ct);
                        return new CalibrationData(false, 0, 0, 0, warnings, e.TryGetProperty("Reason", out var r) ? r.GetString() : "캘리브레이션에 실패했습니다");
                    case "CalibrationComplete":
                        await StopPhd2Async(ct); // 끝 상태 약속: 가이드·루프 아님
                        var cal = await phd2.CallAsync("get_calibration_data", new object[] { "Mount" }, ct);
                        double xa = Num(cal, "xAngle"), ya = Num(cal, "yAngle");
                        var ortho = Math.Abs(90 - Math.Abs(((xa - ya) % 180 + 180) % 180));
                        return new CalibrationData(true, ortho, Num(cal, "xRate"), Num(cal, "yRate"), warnings);
                }
            }
        }
        catch (Phd2Exception e)
        {
            return new CalibrationData(false, 0, 0, 0, warnings, e.Message);
        }
        await StopPhd2Async(ct);
        return new CalibrationData(false, 0, 0, 0, warnings, "캘리브레이션이 8분 안에 끝나지 않았습니다");
    }

    public async Task<bool> HasCalibrationAsync(CancellationToken ct)
    {
        try { return (await phd2.CallAsync("get_calibrated", ct: ct)).GetBoolean(); }
        catch (Exception e) when (e is Phd2Exception or InvalidOperationException) { return false; }
    }

    /// <summary>적도의 멈춤과 PHD2 멈춤을 함께 보낸다 — PHD2가 답하지 않아도 적도의 멈춤이 뒤로 밀리지 않게 (2026-10-14 자체 리뷰)</summary>
    public async Task<bool> StopAsync(CancellationToken ct)
    {
        var mount = rig.StopSlewAndConfirmAsync(ct);
        var phd = StopPhd2Async(ct);
        return await mount & await phd;
    }

    public async Task<CalibrationEndState> ReadEndStateAsync(CancellationToken ct)
    {
        string? state;
        try { state = await phd2.AppStateAsync(ct); } catch (Phd2Exception) { state = null; } // 읽지 못함 ≠ 멈춤 (CX-20-08)
        var m = await rig.MountAsync(ct);
        return new CalibrationEndState(await HasCalibrationAsync(ct), state is "Guiding" or "Calibrating", state == "Looping",
            m is null || m.Slewing, m?.Tracking ?? false, Phd2Unknown: string.IsNullOrEmpty(state));
    }

    private async Task<bool> StopPhd2Async(CancellationToken ct)
    {
        try
        {
            await phd2.CallAsync("stop_capture", ct: ct);
            for (var i = 0; i < 30; i++)
            {
                if (await phd2.AppStateAsync(ct) == "Stopped") return true;
                await Task.Delay(200, ct);
            }
        }
        catch (Phd2Exception) { }
        return false;
    }

    private static double Num(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
