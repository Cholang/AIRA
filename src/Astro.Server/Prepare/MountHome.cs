using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Prepare;

/// <summary>
/// 적도의를 홈에 두고 추적을 끄기 — 진행 표시 아래 "적도의 홈" 버튼(2026-10-09 사용자 요청)과 장비 준비 끝의 쉬게 하기(rig/rest)가 함께 쓴다.
/// 움직이면 안 되는 때(촬영 중, 적도의를 쓰는 작업 중, 실제로 움직이는 중, 가이딩 중, 적도의 미연결)는 이유와 함께 막는다.
/// 홈 도착을 확인하지 못하면 마무리(PackTask)와 같이 움직임부터 멈추고 멈춘 것을 확인한다. 그것도 확인하지 못하면
/// "확인 필요"로 남겨 사용자가 직접 확인했다고 할 때까지 새 홈을 막는다 (CX-20-03).
/// Set Home은 쓰지 않는다 (Go Home만)
/// </summary>
public sealed class MountHome(PrepareMode mode, NinaApiClient nina, Flow.PrepareFlow flow, Shoot.ShootSession shoot, Flow.MountLock mount,
    Tasks.Wrap.IWrapDevices wrap, ILogger<MountHome> log)
{
    private volatile bool _homing;
    /// <summary>마지막 홈이 실패했으면 그 안내. NeedsCheck면 멈춘 것도 확인하지 못함 — 사용자 확인 전에는 새 홈을 막는다</summary>
    private volatile string? _problem;
    private volatile bool _needsCheck;

    public sealed record State(bool Available, bool Homing, string? Reason, string? Problem = null, bool NeedsCheck = false);

    public async Task<State> StateAsync(CancellationToken ct)
    {
        if (_homing) return new(false, true, "적도의를 홈으로 보내는 중입니다");
        if (_needsCheck) return new(false, false, "적도의가 멈췄는지 확인하지 못했습니다. 직접 확인한 뒤 눌러 주세요", _problem, true);
        if (await WhyNotAsync(ct) is { } why) return new(false, false, why, _problem);
        return new(true, false, null, _problem);
    }

    private async Task<string?> WhyNotAsync(CancellationToken ct)
    {
        // 아이라 안에서 알 수 있는 것부터 (N.I.N.A.에 묻지 않음) — 2초마다 묻는 버튼이라 N.I.N.A. 요청은 적도의 1번 + 필요할 때 가이더 1번만
        if (shoot.Active) return "촬영 중에는 홈으로 보낼 수 없습니다. 촬영을 멈춘 뒤 눌러 주세요";
        foreach (var runner in new[] { flow.Rig, flow.Target, flow.Wrap })
            if (runner.ActiveTaskTitle is { } task) return $"{task} 작업 중에는 홈으로 보낼 수 없습니다";
        if (mount.IsHeld) return "적도의가 다른 작업에 쓰이는 중입니다";
        if (mode.Simulate) return null;
        // 연결 여부와 실제 이동을 한 번에 — N.I.N.A.에서 직접 시작한 이동은 아이라의 잠금을 잡지 않으므로 실제 상태로도 본다 (CX-20-03)
        if (await nina.GetInfoAsync("mount", ct) is not { ValueKind: System.Text.Json.JsonValueKind.Object } info
            || !info.TryGetProperty("Connected", out var c) || c.ValueKind != System.Text.Json.JsonValueKind.True)
            return "적도의가 연결되지 않았습니다";
        if (info.TryGetProperty("Slewing", out var sl) && sl.ValueKind == System.Text.Json.JsonValueKind.True)
            return "적도의가 움직이는 중입니다. 멈춘 뒤 눌러 주세요";
        if (await nina.GetInfoAsync("guider", ct) is { ValueKind: System.Text.Json.JsonValueKind.Object } g
            && g.TryGetProperty("State", out var st) && st.GetString() is { } state
            && (state.Contains("Guiding", StringComparison.OrdinalIgnoreCase) || state.Contains("Calibrating", StringComparison.OrdinalIgnoreCase)))
            return "가이딩 중에는 홈으로 보낼 수 없습니다. 가이딩이 멈춘 뒤 눌러 주세요";
        return null;
    }

    /// <summary>"적도의 홈" 버튼: 지금 보낼 수 있으면 시작하고 null, 아니면 이유. 홈·추적 끔은 뒤에서 (적도의 잠금 안에서)</summary>
    public async Task<string?> StartAsync(CancellationToken ct)
    {
        var s = await StateAsync(ct);
        if (!s.Available) return s.Reason;
        Begin("홈 버튼");
        return null;
    }

    /// <summary>장비 준비 끝(rig/rest): 확인 필요 상태가 아니면 홈에 두고 추적을 끈다 (막힌 이유가 있으면 로그만)</summary>
    public async Task RestAfterRigAsync(CancellationToken ct)
    {
        var s = await StateAsync(ct);
        if (!s.Available)
        {
            log.LogInformation("장비 준비 뒤 쉬게 하기를 하지 않음: {Reason}", s.Reason);
            return;
        }
        Begin("장비 준비 뒤");
    }

    /// <summary>
    /// 아이라를 끄기 전: 적도의 추적을 끈다 (2026-10-11 실기 — 극축 정렬을 시작해 추적이 켜진 채 아이라를 끄자 몇 시간 뒤 RA축이 90° 돌아가 있었다:
    /// 케이블이 감기거나 경통이 삼각대에 닿을 수 있다). 촬영 중이면 그대로(촬영은 N.I.N.A.가 이어 감). 적도의가 연결돼 있지 않거나 모의 장비면 할 일 없음(null).
    /// 끄지 못했으면 false
    /// </summary>
    public async Task<bool?> TrackingOffForQuitAsync(CancellationToken ct)
    {
        if (mode.Simulate || shoot.Active) return null;
        if (await nina.GetInfoAsync("mount", ct) is not { ValueKind: System.Text.Json.JsonValueKind.Object } info
            || !info.TryGetProperty("Connected", out var c) || c.ValueKind != System.Text.Json.JsonValueKind.True) return null;
        if (info.TryGetProperty("TrackingEnabled", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.False) return true;
        var off = await wrap.TrackingOffAsync(ct);
        log.LogInformation("종료 전 적도의 추적 끄기: {Result}", off ? "끔" : "실패");
        return off;
    }

    /// <summary>사용자가 적도의가 멈춘 것을 직접 확인함 — 확인 필요 상태를 푼다</summary>
    public void Confirm()
    {
        _needsCheck = false;
        _problem = null;
        log.LogInformation("적도의 멈춤을 사용자가 직접 확인함");
    }

    private void Begin(string who)
    {
        _homing = true;
        _problem = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await using (await mount.AcquireAsync(CancellationToken.None))
                {
                    var homed = await wrap.HomeAsync(CancellationToken.None);
                    if (!homed)
                    {
                        // 홈 시간 초과·실패는 정지를 뜻하지 않는다 → 멈추고 멈춘 것을 확인 (마무리 PackTask와 같은 규칙)
                        log.LogWarning("{Who}: 홈 도착을 확인하지 못해 멈춤", who);
                        if (!await wrap.StopAsync(CancellationToken.None))
                        {
                            _needsCheck = true;
                            _problem = "홈 위치에 도착한 것도, 적도의가 멈춘 것도 확인하지 못했습니다. 적도의를 직접 보고 멈춰 있는지, 케이블이 걸리지 않았는지 확인해 주세요.";
                            return;
                        }
                        _problem = "홈 위치에 도착한 것을 확인하지 못해 적도의를 멈췄습니다. 적도의가 멈춰 있는지 확인해 주세요.";
                    }
                    if (!await wrap.TrackingOffAsync(CancellationToken.None))
                    {
                        log.LogWarning("{Who}: 추적을 끄지 못함", who);
                        _problem ??= "추적을 끄지 못했습니다. N.I.N.A.에서 추적이 꺼졌는지 확인해 주세요.";
                    }
                }
            }
            catch (Exception e)
            {
                log.LogWarning(e, "{Who}: 홈 실패", who);
                _needsCheck = true;
                _problem = "적도의를 홈으로 보내다 문제가 생겼습니다. 적도의가 멈춰 있는지 직접 확인해 주세요.";
            }
            finally { _homing = false; }
        });
    }
}
