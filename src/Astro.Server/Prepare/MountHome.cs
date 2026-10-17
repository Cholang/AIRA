using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Prepare;

/// <summary>
/// 적도의를 홈에 두고 추적을 끄기 — 진행 표시 아래 "적도의 홈" 버튼(2026-10-09 사용자 요청)과 장비 준비 끝의 쉬게 하기(rig/rest)가 함께 쓴다.
/// 움직이면 안 되는 때(촬영 중, 적도의를 쓰는 작업 중, 실제로 움직이는 중, 가이딩 중, 적도의 미연결)는 이유와 함께 막는다.
/// 홈 도착을 확인하지 못하면 마무리(PackTask)와 같이 움직임부터 멈추고 멈춘 것을 확인한다. 그것도 확인하지 못하면
/// "확인 필요"로 남겨 사용자가 직접 확인했다고 할 때까지 적도의를 움직이는 모든 작업을 막는다 (CX-20-03, CX-21-03 — MountLock.Unconfirmed).
/// 아이라를 끌 때 홈 이동 중이면 멈추고 멈춘 것을 확인한다 (CX-21-02).
/// Set Home은 쓰지 않는다 (Go Home만)
/// </summary>
public sealed class MountHome(PrepareMode mode, NinaApiClient nina, Flow.PrepareFlow flow, Shoot.ShootSession shoot, Flow.MountLock mount,
    Tasks.Wrap.IWrapDevices wrap, ILogger<MountHome> log)
{
    private volatile bool _homing;
    /// <summary>홈 실행이 이미 시작됨 — 확인과 시작 사이에 await가 있어 두 번 눌러도(PC·휴대폰, 홈 버튼과 rig/rest) 한 번만 (2026-10-14 자체 리뷰)</summary>
    private int _begun;
    /// <summary>홈 이동 중 (종료가 프로그램을 닫지 않는다)</summary>
    public bool Homing => _homing;
    /// <summary>마지막 홈이 실패했으면 그 안내</summary>
    private volatile string? _problem;
    /// <summary>진행 중인 홈 (종료가 멈추고 끝나기를 기다린다)</summary>
    private Task _run = Task.CompletedTask;
    private CancellationTokenSource? _cts;
    private const string NotConnected = "적도의가 연결되지 않았습니다";
    private const string UnconfirmedText = "적도의가 멈췄는지 확인하지 못했습니다. 적도의를 직접 보고 멈춰 있으면 \"적도의 확인 필요\" 버튼에서 확인해 주세요.";

    /// <summary>멈춤 확인 필요 — 사용자 확인 전에는 적도의를 움직이는 작업을 모두 막는다</summary>
    private bool NeedsCheck => mount.Unconfirmed is not null;

    private void MarkUnconfirmed(string problem)
    {
        _problem = problem;
        mount.Unconfirmed = UnconfirmedText;
    }

    public sealed record State(bool Available, bool Homing, string? Reason, string? Problem = null, bool NeedsCheck = false);

    public async Task<State> StateAsync(CancellationToken ct)
    {
        if (_homing) return new(false, true, "적도의를 홈으로 보내는 중입니다");
        if (NeedsCheck) return new(false, false, "적도의가 멈췄는지 확인하지 못했습니다. 직접 확인한 뒤 눌러 주세요", _problem, true);
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
            return NotConnected;
        if (info.TryGetProperty("Slewing", out var sl) && sl.ValueKind == System.Text.Json.JsonValueKind.True)
            return "적도의가 움직이는 중입니다. 멈춘 뒤 눌러 주세요";
        // N.I.N.A.에서 직접 시작한 홈 이동: OnStep은 Slewing=false로 답한다 (MountTiming.HomingLikely — 2026-10-17 실기)
        if (HomingLikely(info)) return "적도의가 홈으로 가는 중입니다. 도착한 뒤 눌러 주세요";
        if (await nina.GetInfoAsync("guider", ct) is { ValueKind: System.Text.Json.JsonValueKind.Object } g
            && g.TryGetProperty("State", out var st) && st.GetString() is { } state
            && (state.Contains("Guiding", StringComparison.OrdinalIgnoreCase) || state.Contains("Calibrating", StringComparison.OrdinalIgnoreCase)))
            return "가이드 중에는 홈으로 보낼 수 없습니다. 가이드가 멈춘 뒤 눌러 주세요";
        return null;
    }

    /// <summary>N.I.N.A. 적도의 정보로 본 "홈으로 가는 중일 수 있음" (MountTiming.HomingLikely)</summary>
    private static bool HomingLikely(System.Text.Json.JsonElement info) =>
        Real.MountTiming.HomingLikely(
            info.TryGetProperty("AtHome", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.True,
            info.TryGetProperty("Coordinates", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Object && c.TryGetProperty("Dec", out var d) && d.TryGetDouble(out var dec) ? dec : double.NaN,
            info.TryGetProperty("SideOfPier", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() ?? "" : "");

    /// <summary>
    /// 아이라 종료 창에 "적도의를 홈으로 보내기"를 보일까 (2026-10-17 사용자 — 기본으로 홈, 끌 수 있게): 적도의가 연결돼 있고 홈이 아닐 때,
    /// 또는 홈 이동이 이미 진행 중일 때. 이미 홈·미연결·상태를 못 읽음·멈춤 확인 필요면 보이지 않는다. 모의 장비는 늘 보인다(모의 홈)
    /// </summary>
    public async Task<bool> OfferForQuitAsync(CancellationToken ct)
    {
        if (_homing) return true;
        if (NeedsCheck) return false;
        if (mode.Simulate) return true;
        return await nina.GetInfoAsync("mount", ct) is { ValueKind: System.Text.Json.JsonValueKind.Object } info
            && info.TryGetProperty("Connected", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.True
            && !(info.TryGetProperty("AtHome", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.True);
    }

    /// <summary>
    /// 아이라 종료 중 홈으로 (종료 창에서 체크했을 때 — 진행 중인 작업을 멈춘 뒤, 추적 끄기 전). 홈 도착(AtHome)까지 기다리고 추적을 끈다 — "적도의 홈" 버튼과 같은 절차.
    /// 이미 홈 이동 중이면(장비 준비 끝의 자동 홈 등) 그것이 끝나기를 기다린다. 보낼 일이 없으면(미연결·이미 홈) 문제 없음(null).
    /// 홈 도착이나 멈춤을 확인하지 못했으면 사용자에게 보일 문장 — 종료 창이 멈추고 "그래도 종료"를 보인다
    /// </summary>
    public async Task<string?> HomeForQuitAsync(CancellationToken ct)
    {
        if (!_homing)
        {
            var s = await StateAsync(ct);
            if (s.NeedsCheck) return s.Problem ?? UnconfirmedText;
            if (!s.Available)
            {
                // 연결 안 됨은 할 일 없음 (추적 끄기 단계가 따로 확인한다). 그 밖의 이유(움직이는 중 등)는 홈으로 보내지 못한 것
                if (s.Reason == NotConnected) return null;
                log.LogInformation("종료 전 홈으로 보내지 못함: {Reason}", s.Reason);
                return $"적도의를 홈으로 보내지 못했습니다 — {s.Reason}";
            }
            if (!mode.Simulate && await nina.GetInfoAsync("mount", ct) is { ValueKind: System.Text.Json.JsonValueKind.Object } info
                && info.TryGetProperty("AtHome", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.True)
                return null; // 이미 홈
            if (!Begin("종료 전")) { /* 그 사이 다른 쪽이 시작함 — 아래에서 함께 기다린다 */ }
            log.LogInformation("종료 전 적도의를 홈으로 보냄");
        }
        await _run.WaitAsync(ct);
        if (NeedsCheck) return _problem ?? UnconfirmedText;
        return _problem;
    }

    /// <summary>"적도의 홈" 버튼: 지금 보낼 수 있으면 시작하고 null, 아니면 이유. 홈·추적 끔은 뒤에서 (적도의 잠금 안에서)</summary>
    public async Task<string?> StartAsync(CancellationToken ct)
    {
        var s = await StateAsync(ct);
        if (!s.Available) return s.Reason;
        return Begin("홈 버튼") ? null : "적도의를 홈으로 보내는 중입니다";
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
    /// 다른 대상으로 계획에 돌아올 때(센터링·시험 사진·촬영의 "다른 대상"): 움직이지 않고 추적만 끈다 (2026-10-14 자체 리뷰 — 이동은 "촬영 시작 − 준비 시간"까지
    /// 기다리므로 몇 시간 앞 대상을 추적한 채 한계 쪽으로 흘러갈 수 있었다. 장비 준비 뒤 쉬게 하기와 같은 이유, 다만 홈으로 옮기지는 않는다).
    /// 끄려 했는데 끄지 못했거나 적도의 상태를 읽지 못했으면 사용자에게 보일 문장. 껐거나, 끌 일이 없거나(미연결이라고 분명히 답함·이미 꺼짐),
    /// 끌 수 없는 때(촬영 중·작업 중·홈 이동 중·멈춤 확인 필요 — 로그만)면 null.
    /// 상태를 못 읽음은 "미연결"로 보지 않는다 (CX-36-04 — 그대로 넘겨 추적이 켜진 채 남을 수 있었다. 종료 전 추적 끄기와 같은 구분)
    /// </summary>
    public async Task<string?> RestForPlanAsync(CancellationToken ct)
    {
        if (mode.Simulate) return null;
        string? busy = _homing ? "홈 이동 중" : NeedsCheck ? "멈춤 확인 필요" : shoot.Active ? "촬영 중" : mount.IsHeld ? "적도의 사용 중" : null;
        foreach (var runner in new[] { flow.Rig, flow.Target, flow.Wrap })
            busy ??= runner.ActiveTaskTitle is { } task ? $"{task} 작업 중" : null;
        if (busy is not null)
        {
            log.LogInformation("계획으로 돌아가며 추적 끄기를 하지 않음: {Reason}", busy);
            return null;
        }
        bool? off;
        await using (await mount.AcquireAsync(ct))
            off = await TrackingOffKnownAsync("계획으로 돌아가며", ct);
        return off == false ? "추적을 끄지 못했거나 적도의 상태를 읽지 못했습니다. N.I.N.A.에서 추적이 꺼졌는지 확인해 주세요." : null;
    }

    /// <summary>
    /// 아이라를 끄기 전: 적도의 추적을 끈다 (2026-10-11 실기 — 극축 정렬을 시작해 추적이 켜진 채 아이라를 끄자 몇 시간 뒤 RA축이 90° 돌아가 있었다:
    /// 케이블이 감기거나 경통이 삼각대에 닿을 수 있다). 촬영 중이면 그대로(촬영은 N.I.N.A.가 이어 감). 모의 장비이거나
    /// N.I.N.A.가 적도의 미연결이라고 분명히 답하면 할 일 없음(null). 적도의 상태를 읽지 못했거나 끄지 못했으면 false (CX-21-04 — 모름을 "할 일 없음"으로 보지 않는다)
    /// </summary>
    public async Task<bool?> TrackingOffForQuitAsync(CancellationToken ct)
    {
        if (mode.Simulate || shoot.Active) return null;
        return await TrackingOffKnownAsync("종료 전", ct);
    }

    /// <summary>
    /// 추적 끄기 — 상태를 못 읽음과 미연결을 나눈다: 미연결이라고 분명히 답하면 null(할 일 없음), 이미 꺼져 있거나 껐으면 true,
    /// 상태를 못 읽었거나 끄지 못했으면 false
    /// </summary>
    private async Task<bool?> TrackingOffKnownAsync(string when, CancellationToken ct)
    {
        if (await nina.GetInfoAsync("mount", ct) is not { ValueKind: System.Text.Json.JsonValueKind.Object } info
            || !info.TryGetProperty("Connected", out var c) || c.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
        {
            log.LogWarning("{When} 적도의 추적 끄기: 적도의 상태를 읽지 못함", when);
            return false;
        }
        if (c.ValueKind == System.Text.Json.JsonValueKind.False) return null;
        if (info.TryGetProperty("TrackingEnabled", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.False)
        {
            log.LogInformation("{When} 적도의 추적 끄기: 이미 꺼져 있음", when);
            return true;
        }
        var off = await wrap.TrackingOffAsync(ct);
        log.LogInformation("{When} 적도의 추적 끄기: {Result}", when, off ? "끔" : "실패");
        return off;
    }

    /// <summary>
    /// 확인 없이 끌 때("그래도 종료", 화면이 응답하지 않아 창이 직접 닫을 때): 가이딩 멈춤과 추적 끄기를 한 번씩 시도한다 (각 10초 안).
    /// 촬영 중이어도 끈다 — 아이라가 꺼지면 다음 사진·반전·끝을 볼 주체가 없다. 적도의 잠금은 기다리지 않는다(막힌 작업 때문에 끄는 경우라서).
    /// 추적을 끄지 못했으면(또는 모르면) 사용자에게 보일 문장, 껐거나 미연결·모의면 null (2026-10-14 자체 리뷰: 그동안 아무것도 시도하지 않고 꺼졌다)
    /// </summary>
    public async Task<string?> ForceSafeAsync()
    {
        if (mode.Simulate) return null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var guide = nina.RequestAsync("equipment/guider/stop", TimeSpan.FromSeconds(10), cts.Token);
        bool? off;
        try
        {
            off = await nina.GetInfoAsync("mount", cts.Token) is { ValueKind: System.Text.Json.JsonValueKind.Object } info
                && info.TryGetProperty("Connected", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.False
                ? null
                : await wrap.TrackingOffAsync(cts.Token);
        }
        catch (Exception e) { log.LogWarning(e, "확인 없이 끄기: 추적 끄기 실패"); off = false; }
        try { await guide; } catch (Exception e) { log.LogWarning(e, "확인 없이 끄기: 가이드 멈춤 실패"); }
        log.LogInformation("확인 없이 끄기: 추적 {Result}", off switch { true => "끔", false => "끄지 못함", null => "적도의 미연결" });
        return off == false ? "적도의 추적을 끄지 못했습니다. 이대로 두면 적도의가 밤새 돌아갈 수 있으니 N.I.N.A.나 적도의에서 추적을 직접 꺼 주세요." : null;
    }

    /// <summary>사용자가 적도의가 멈춘 것을 직접 확인함 — 확인 필요 상태를 푼다</summary>
    public void Confirm()
    {
        mount.Unconfirmed = null;
        _problem = null;
        log.LogInformation("적도의 멈춤을 사용자가 직접 확인함");
    }

    /// <summary>
    /// 아이라를 끄기 전 (CX-21-02): 홈 이동 중이면 멈추고 멈춘 것을 확인할 때까지 기다린다.
    /// 멈춤을 확인하지 못했거나 이미 "확인 필요"면 false (종료 화면이 문제를 보여 주고 사용자가 정한다)
    /// </summary>
    /// keepHome: 종료 창에서 "홈으로 보내기"를 골랐으면 진행 중인 홈 이동을 멈추지 않는다 (HomeForQuitAsync가 끝나기를 기다린다)
    public async Task<bool> StopForQuitAsync(bool keepHome = false)
    {
        if (keepHome && _homing) return !NeedsCheck;
        if (_homing)
        {
            log.LogInformation("종료: 홈 이동을 멈춤");
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* 막 끝남 */ }
            try { await _run.WaitAsync(TimeSpan.FromSeconds(60)); } catch { /* 시간 초과 — 아래에서 판단 */ }
        }
        return !_homing && !NeedsCheck;
    }

    /// <summary>홈 실행 시작. 이미 시작돼 있으면 false</summary>
    private bool Begin(string who)
    {
        if (Interlocked.CompareExchange(ref _begun, 1, 0) != 0) return false;
        _homing = true;
        _problem = null;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _run = Task.Run(async () =>
        {
            try
            {
                await using (await mount.AcquireAsync(CancellationToken.None))
                {
                    bool homed;
                    try { homed = await wrap.HomeAsync(cts.Token); }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { homed = false; } // 종료가 멈춤 — 아래에서 멈추고 확인
                    if (!homed)
                    {
                        // 홈 시간 초과·실패·중단은 정지를 뜻하지 않는다 → 멈추고 멈춘 것을 확인 (마무리 PackTask와 같은 규칙)
                        log.LogWarning("{Who}: 홈 도착을 확인하지 못해 멈춤", who);
                        if (!await wrap.StopAsync(CancellationToken.None))
                        {
                            MarkUnconfirmed("홈 위치에 도착한 것도, 적도의가 멈춘 것도 확인하지 못했습니다. 적도의를 직접 보고 멈춰 있는지, 케이블이 걸리지 않았는지 확인해 주세요.");
                            return;
                        }
                        if (cts.IsCancellationRequested) return; // 종료하려고 멈춤 — 추적은 종료 절차가 끈다
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
                MarkUnconfirmed("적도의를 홈으로 보내다 문제가 생겼습니다. 적도의가 멈춰 있는지 직접 확인해 주세요.");
            }
            finally
            {
                _homing = false;
                Volatile.Write(ref _begun, 0);
                cts.Dispose();
            }
        });
        return true;
    }
}
