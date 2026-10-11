using System.Runtime.InteropServices;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// RA축만 돌리기 (① 극 찾기 — SharpCap은 안내만 하고 직접 돌리지 않음). goto는 극 근처에서 Dec축까지 움직여서 ASCOM MoveAxis를 직접 쓴다.
/// OnStep 드라이버는 로컬 서버라 N.I.N.A.와 동시에 연결해도 된다 (2026-10-04 실기, 최대 3.55°/s).
/// 주의: 이 적도의는 홈에서 한쪽으로 약 60°까지만 돈다(원인 미해결 — 다음에 확인). 그래서 회전은 60°로 하고 홈 근처에서 시작한다.
/// 회전 방향(부호)은 실기에서 아직 확인하지 않았다.
/// 드라이버 객체는 쓰고 나면 바로 놓는다(COM 해제), 이 객체가 연결했으면 연결도 풀고 놓는다 — N.I.N.A.의 연결은 건드리지 않는다 (자체 점검 M2).
/// 드라이버 명령은 모두 뒤에서 하나씩, 시간 한도와 함께 — 드라이버가 답하지 않아도 부른 쪽(멈추기·종료)이 붙잡히지 않는다.
/// 앞 명령이 아직 안 끝났으면 새 명령은 바로 실패로(막힌 작업이 쌓이지 않게) (2026-10-11 Codex CX-30-04)
/// </summary>
public sealed class AscomAxis(ILogger<AscomAxis> log)
{
    public string DriverId { get; set; } = "ASCOM.OnStep.Telescope";

    /// <summary>드라이버 명령 하나에 주는 시간 (직렬 통신이 멈춘 드라이버 — 이보다 오래 기다리지 않는다)</summary>
    internal TimeSpan CommandLimit { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>시험용: 드라이버 대신 쓸 객체를 만든다 (null이면 ASCOM 드라이버)</summary>
    internal Func<object>? Factory { get; init; }

    private readonly Lock _gate = new();
    private Task _busy = Task.CompletedTask;

    /// <summary>RA축을 rate(°/s)로 seconds 동안 돌리고 멈춘다. 진행 각도를 알린다. 실패면 이유</summary>
    public async Task<string?> RotateRaAsync(double rate, double seconds, Action<double> progress, CancellationToken ct)
    {
        object? scope = null;
        var connectedHere = false;
        // 시작 명령을 기다리다 포기했는가 (CX-32-01: 15초 뒤 "회전 실패"로 넘어간 다음 늦게 풀린 시작 명령이 적도의를 돌리고, 멈춤 명령은 "앞 명령 실행 중"으로 빠졌다).
        // 포기한 뒤에는 시작 명령이 스스로 돌리지 않고, 이미 돌렸으면 스스로 멈추고 드라이버를 놓는다
        var late = new LateStart();
        var mine = false; // 드라이버를 놓는 일을 이 함수가 맡는가 (포기했으면 시작 명령 쪽이 맡는다)
        try
        {
            var start = await CommandAsync("RA 회전 시작", () =>
            {
                dynamic s = Create();
                var here = false;
                try
                {
                    if (!(bool)s.Connected)
                    {
                        s.Connected = true;
                        here = true;
                    }
                    if (!(bool)s.CanMoveAxis(0)) { Release(s, here); return "적도의가 축 돌리기를 지원하지 않습니다"; }
                    lock (late)
                    {
                        if (late.GaveUp) { Release(s, here); return "포기함"; }
                        late.Sent = true;
                    }
                    s.MoveAxis(0, rate);
                    lock (late)
                    {
                        if (late.GaveUp)
                        {
                            log.LogWarning("RA 회전 시작이 늦게 실행됨 — 바로 멈춤");
                            try { s.MoveAxis(0, 0.0); } finally { Release(s, here); }
                            return "포기함";
                        }
                        scope = s;
                        connectedHere = here;
                    }
                    return (string?)null;
                }
                catch
                {
                    Release(s, here);
                    throw;
                }
            });
            lock (late)
            {
                if (!start.Done)
                {
                    late.GaveUp = true;
                    if (late.Sent) log.LogWarning("RA 회전 시작 명령이 드라이버에 들어간 뒤 답이 없음 — 늦게 돌면 시작 명령 쪽이 바로 멈춤, 따로 멈춤도 보냄");
                }
                mine = scope is not null;
            }
            if (!start.Done)
            {
                // 이미 들어갔을 수 있으니 멈춤도 따로 보낸다 (앞 명령이 막혀 있어도 거절하지 않는 멈춤)
                await StopAsync();
                return start.Error ?? "적도의 축을 돌리지 못했습니다 (드라이버가 답하지 않음)";
            }
            if (start.Value is { } refused) return refused;
            var t0 = DateTime.UtcNow;
            while ((DateTime.UtcNow - t0).TotalSeconds < seconds)
            {
                progress(Math.Abs(rate) * (DateTime.UtcNow - t0).TotalSeconds);
                await Task.Delay(250, ct);
            }
            return null;
        }
        finally
        {
            // 취소·오류여도 반드시 멈춤 명령 (뒤에서, 시간 한도 — 막혀도 여기서 붙잡히지 않는다)
            if (mine && scope is { } held)
            {
                var stop = await CommandAsync("RA 회전 멈춤", () =>
                {
                    try { ((dynamic)held).MoveAxis(0, 0.0); }
                    finally { Release(held, connectedHere); }
                    return true;
                }, stop: true);
                if (!stop.Done) log.LogWarning("RA 회전 멈춤 명령을 보내지 못함: {Why}", stop.Error ?? "드라이버가 답하지 않음");
            }
        }
    }

    private sealed class LateStart
    {
        public bool GaveUp;
        public bool Sent;
    }

    /// <summary>축 돌리기 멈춤. 멈춤 명령을 실제로 보냈으면 true (멈춤 확인은 NinaRig.ConfirmStillAsync). 드라이버가 답하지 않으면 15초 뒤 false</summary>
    public async Task<bool> StopAsync() =>
        (await CommandAsync("RA 축 멈춤", () => WithDriver(scope =>
        {
            scope.MoveAxis(0, 0.0);
            return true;
        }), stop: true)) is { Done: true, Value: true };

    /// <summary>
    /// 비상 정지: N.I.N.A.를 거치지 않고 드라이버에 직접 — 이동 중단(AbortSlew) + 두 축 돌리기 멈춤 (M2: N.I.N.A.가 답하지 않으면 멈출 길이 없었다).
    /// 드라이버가 답하지 않으면 15초 뒤 포기. 명령을 보냈으면 true
    /// </summary>
    public async Task<bool> EmergencyStopAsync() =>
        (await CommandAsync("비상 정지", () => WithDriver(scope =>
        {
            var sent = false;
            try { scope.AbortSlew(); sent = true; } catch (Exception e) { log.LogWarning(e, "비상 정지: 이동 중단 실패"); }
            for (var axis = 0; axis < 2; axis++)
                try { scope.MoveAxis(axis, 0.0); sent = true; } catch (Exception e) { log.LogWarning(e, "비상 정지: 축 {Axis} 멈춤 실패", axis); }
            return sent;
        }), stop: true)) is { Done: true, Value: true };

    private readonly record struct Outcome<T>(bool Done, T? Value, string? Error);

    /// <summary>
    /// 드라이버 명령 하나를 뒤에서, 시간 한도 안에. 앞 명령이 아직 돌고 있으면 바로 실패(Done=false).
    /// stop = 멈춤 명령: 앞 명령이 막혀 있어도 거절하지 않고 따로 보낸다 (CX-32-01 — 멈춤이 "앞 명령 실행 중"으로 빠지면 늦게 시작된 회전을 멈출 길이 없다)
    /// </summary>
    private async Task<Outcome<T>> CommandAsync<T>(string what, Func<T> command, bool stop = false)
    {
        Task<T> work;
        lock (_gate)
        {
            if (stop) work = Task.Run(command);
            else if (!_busy.IsCompleted)
            {
                log.LogWarning("{What}: 드라이버가 앞 명령에 아직 답하지 않아 보내지 않음", what);
                return new(false, default, "적도의 드라이버가 앞 명령에 아직 답하지 않습니다");
            }
            else
            {
                work = Task.Run(command);
                _busy = work;
            }
        }
        try { return new(true, await work.WaitAsync(CommandLimit), null); }
        catch (TimeoutException)
        {
            log.LogWarning("{What}: 드라이버가 {Seconds}초 안에 답하지 않음", what, CommandLimit.TotalSeconds);
            return new(false, default, null);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "{What} 실패", what);
            return new(false, default, $"적도의 축을 돌리지 못했습니다 ({e.Message})");
        }
    }

    /// <summary>드라이버를 열어(연결 안 돼 있으면 연결) 명령 하나 하고 놓는다</summary>
    private bool WithDriver(Func<dynamic, bool> command)
    {
        object? scope = null;
        var connectedHere = false;
        try
        {
            dynamic s = Create();
            scope = s;
            if (!(bool)s.Connected)
            {
                s.Connected = true;
                connectedHere = true;
            }
            return command(s);
        }
        finally { if (scope is not null) Release(scope, connectedHere); }
    }

    /// <summary>이 객체가 연결했으면 연결을 풀고, COM 객체를 바로 놓는다 (가비지 수집까지 드라이버 연결이 남지 않게)</summary>
    private void Release(object scope, bool connectedHere)
    {
        if (connectedHere)
            try { ((dynamic)scope).Connected = false; } catch (Exception e) { log.LogDebug(e, "ASCOM 연결 풀기 실패"); }
        try { if (Marshal.IsComObject(scope)) Marshal.FinalReleaseComObject(scope); } catch (Exception e) { log.LogDebug(e, "ASCOM 객체 놓기 실패"); }
    }

    private dynamic Create()
    {
        if (Factory is { } make) return make();
        var type = Type.GetTypeFromProgID(DriverId) ?? throw new InvalidOperationException($"ASCOM 드라이버 {DriverId}를 찾지 못했습니다");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("ASCOM 드라이버를 만들지 못했습니다");
    }
}
