using System.Runtime.InteropServices;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// RA축만 돌리기 (① 극 찾기 — SharpCap은 안내만 하고 직접 돌리지 않음). goto는 극 근처에서 Dec축까지 움직여서 ASCOM MoveAxis를 직접 쓴다.
/// OnStep 드라이버는 로컬 서버라 N.I.N.A.와 동시에 연결해도 된다 (2026-10-04 실기, 최대 3.55°/s).
/// 주의: 이 적도의는 홈에서 한쪽으로 약 60°까지만 돈다(원인 미해결 — 다음에 확인). 그래서 회전은 60°로 하고 홈 근처에서 시작한다.
/// 회전 방향(부호)은 실기에서 아직 확인하지 않았다.
/// 드라이버 객체는 쓰고 나면 바로 놓는다(COM 해제), 이 객체가 연결했으면 연결도 풀고 놓는다 — N.I.N.A.의 연결은 건드리지 않는다 (2026-10-11 자체 점검 M2)
/// </summary>
public sealed class AscomAxis(ILogger<AscomAxis> log)
{
    public string DriverId { get; set; } = "ASCOM.OnStep.Telescope";

    /// <summary>드라이버 명령이 이보다 오래 걸리면 기다리지 않는다 (직렬 통신이 멈춘 드라이버 — 요청 스레드를 붙잡지 않게)</summary>
    private static readonly TimeSpan CommandLimit = TimeSpan.FromSeconds(15);

    /// <summary>RA축을 rate(°/s)로 seconds 동안 돌리고 멈춘다. 진행 각도를 알린다. 실패면 이유</summary>
    public async Task<string?> RotateRaAsync(double rate, double seconds, Action<double> progress, CancellationToken ct)
    {
        dynamic? scope = null;
        var connectedHere = false;
        try
        {
            scope = Create();
            if (!(bool)scope.Connected)
            {
                scope.Connected = true;
                connectedHere = true;
            }
            if (!(bool)scope.CanMoveAxis(0)) return "적도의가 축 돌리기를 지원하지 않습니다";
            scope.MoveAxis(0, rate);
            var t0 = DateTime.UtcNow;
            while ((DateTime.UtcNow - t0).TotalSeconds < seconds)
            {
                progress(Math.Abs(rate) * (DateTime.UtcNow - t0).TotalSeconds);
                await Task.Delay(250, ct);
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            log.LogWarning(e, "RA 회전 실패");
            return $"적도의 축을 돌리지 못했습니다 ({e.Message})";
        }
        finally
        {
            // 취소·오류여도 반드시 멈춤 명령
            try { scope?.MoveAxis(0, 0.0); } catch (Exception e) { log.LogWarning(e, "RA 회전 멈춤 실패"); }
            Release(scope, connectedHere);
        }
    }

    /// <summary>축 돌리기 멈춤. 멈춤 명령을 실제로 보냈으면 true (멈춤 확인은 NinaRig.ConfirmStillAsync)</summary>
    public bool Stop() => WithDriver("RA 축 멈춤", scope =>
    {
        scope.MoveAxis(0, 0.0);
        return true;
    });

    /// <summary>
    /// 비상 정지: N.I.N.A.를 거치지 않고 드라이버에 직접 — 이동 중단(AbortSlew) + 두 축 돌리기 멈춤 (M2: N.I.N.A.가 답하지 않으면 멈출 길이 없었다).
    /// 드라이버가 답하지 않으면 15초 뒤 포기. 명령을 보냈으면 true
    /// </summary>
    public async Task<bool> EmergencyStopAsync()
    {
        var work = Task.Run(() => WithDriver("비상 정지", scope =>
        {
            var sent = false;
            try { scope.AbortSlew(); sent = true; } catch (Exception e) { log.LogWarning(e, "비상 정지: 이동 중단 실패"); }
            for (var axis = 0; axis < 2; axis++)
                try { scope.MoveAxis(axis, 0.0); sent = true; } catch (Exception e) { log.LogWarning(e, "비상 정지: 축 {Axis} 멈춤 실패", axis); }
            return sent;
        }));
        try { return await work.WaitAsync(CommandLimit); }
        catch (TimeoutException)
        {
            log.LogWarning("비상 정지: 드라이버가 {Seconds}초 안에 답하지 않음", CommandLimit.TotalSeconds);
            return false;
        }
    }

    /// <summary>드라이버를 열어(연결 안 돼 있으면 연결) 명령 하나 하고 놓는다. 실패면 false</summary>
    private bool WithDriver(string what, Func<dynamic, bool> command)
    {
        dynamic? scope = null;
        var connectedHere = false;
        try
        {
            scope = Create();
            if (!(bool)scope.Connected)
            {
                scope.Connected = true;
                connectedHere = true;
            }
            return command(scope);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "{What} 실패", what);
            return false;
        }
        finally { Release(scope, connectedHere); }
    }

    /// <summary>이 객체가 연결했으면 연결을 풀고, COM 객체를 바로 놓는다 (가비지 수집까지 드라이버 연결이 남지 않게)</summary>
    private void Release(object? scope, bool connectedHere)
    {
        if (scope is null) return;
        if (connectedHere)
            try { ((dynamic)scope).Connected = false; } catch (Exception e) { log.LogDebug(e, "ASCOM 연결 풀기 실패"); }
        try { if (Marshal.IsComObject(scope)) Marshal.FinalReleaseComObject(scope); } catch (Exception e) { log.LogDebug(e, "ASCOM 객체 놓기 실패"); }
    }

    private dynamic Create()
    {
        var type = Type.GetTypeFromProgID(DriverId) ?? throw new InvalidOperationException($"ASCOM 드라이버 {DriverId}를 찾지 못했습니다");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("ASCOM 드라이버를 만들지 못했습니다");
    }
}
