using Astro.Core;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astro.Core.Setup;
using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

/// <summary>
/// 장비 연결. 지금 쓰는 N.I.N.A. 프로필에서 장비 목록을 읽어, 전원 허브부터 차례로 연결한다.
/// 장비 등급 (OPERATION_PROCEDURE.md "장비 연결"):
/// - 필수(카메라·적도의): 프로필에 없거나 연결에 실패하면 멈춘다
/// - 준필수(포커서·가이더): 프로필에 없으면 통과, 실패하면 사용자가 "없이 진행"을 고를 수 있다
/// - 선택(전원 허브·필터휠·회전장치·플랫패널): 프로필에 없으면 통과, 실패하면 경고만 하고 계속
///   단 전원 허브는 다른 장비에 전원을 주므로, 있으면 맨 먼저 연결하고 실패하면 뒤 장비는 보류한다
/// 돔·안전 모니터·날씨 장치는 다루지 않는다.
/// </summary>
public sealed partial class EquipmentConnector(NinaApiClient nina, IOptions<EquipmentOptions> options, EquipmentSimulation sim, EquipmentChoices choices, RigOverrides overrides, LiveDevices live, OpticsStore optics, EquipmentRun state, DevicePrecheck.IHostDevices host, CameraPowerStore cameraPower, ILogger<EquipmentConnector> log, Profiles.PowerWiringStore? power = null, Prepare.Real.AscomAxis? axis = null, Equipment.EquipmentLearned? learned = null)
{
    private const CheckSeverity Required = CheckSeverity.Required;
    private const CheckSeverity Recommended = CheckSeverity.Recommended;
    private const CheckSeverity Optional = CheckSeverity.Optional;

    /// <summary>연결 순서: 허브가 다른 장비에 전원을 주므로 가장 먼저 (docs/onboarding-and-architecture.md 4.2).</summary>
    // Fix: 연결에 실패했을 때 상태 옆 ? 툴팁에 보여 줄 해결 방법 (장비마다 다르게)
    private static readonly Slot[] Slots =
    [
        new("switch", "SwitchSettings", Optional, "전원 허브", "망원경·카메라·열선에 전원을 나눠 주는 장치입니다. 열선은 허브의 포트로 함께 제어합니다.",
            "허브의 12V 전원 어댑터와 PC로 가는 USB 케이블을 확인해 주세요. 허브가 켜져야 다른 장비에도 전원이 들어갑니다."),
        new("mount", "TelescopeSettings", Required, "적도의", "망원경을 움직이고 별을 따라 돌려 주는 받침대입니다.",
            "적도의 전원과 케이블(USB·네트워크)을 확인해 주세요. 무선으로 연결한다면 PC가 적도의의 와이파이에 연결되어 있는지도 확인해 주세요."),
        // 망원경: 연결되는 장비가 아니라 AA의 망원경 목록(OpticsStore)에서 고른 것. 초점거리가 있어야 화각·플레이트 솔빙이 된다 (2026-10-01)
        new(Scope, "", Required, "망원경", "빛을 모으는 망원경(또는 렌즈)입니다. 초점거리와 F값으로 화각과 노출을 계산합니다.",
            "아래 \"장비 변경\"에서 망원경 원을 눌러 쓰는 망원경을 고르거나 추가해 주세요."),
        new("camera", "CameraSettings", Required, "카메라", "사진을 찍는 카메라입니다.",
            "카메라 전원과 USB 케이블을 확인하고, 카메라의 PC 연결 방식이 테더링(PC 촬영)으로 되어 있는지 확인해 주세요."),
        new("focuser", "FocuserSettings", Recommended, "포커서", "초점을 자동으로 맞춰 주는 모터입니다. 없으면 초점을 손으로 맞춥니다.",
            "포커서 전원(허브 포트)과 USB 케이블을 확인해 주세요."),
        new("filterwheel", "FilterWheelSettings", Optional, "필터휠", "촬영 중에 필터를 바꿔 끼워 주는 장치입니다. 없으면 필터를 손으로 끼웁니다.",
            "필터휠 전원과 USB 케이블을 확인해 주세요."),
        new("rotator", "RotatorSettings", Optional, "회전장치", "카메라를 돌려 구도의 각도를 맞춰 주는 장치입니다. 없으면 손으로 돌립니다.",
            "회전장치 전원과 USB 케이블을 확인해 주세요."),
        new("flatdevice", "FlatDeviceSettings", Optional, "플랫패널", "플랫(밝기 고르게 맞추기용 사진)을 찍을 때 망원경 앞을 고르게 비추는 판입니다.",
            "플랫패널 전원과 USB 케이블을 확인해 주세요."),
        new("guider", "GuiderSettings", Recommended, "가이드", "보조 카메라로 별을 지켜보며 흔들림을 바로잡습니다. 없으면 노출을 짧게 찍습니다.",
            "PHD2가 켜져 있는지, PHD2 안에서 가이드 카메라와 적도의가 연결되어 있는지 확인해 주세요."),
    ];

    public sealed record Slot(string Kind, string ProfileKey, CheckSeverity Tier, string Role, string Hint, string Fix);

    public static Slot? FindSlot(string kind) => Slots.FirstOrDefault(s => s.Kind == kind);

    /// <summary>허브가 실패하면 뒤 장비는 이 문장으로 보류한다 (전원이 허브에서 오므로 원인을 하나로 모은다).</summary>
    private const string WaitingForHub = "전원 허브가 연결되면 확인합니다";

    /// <summary>지금 프로필의 전원 배선 (모의·시험에서 저장소가 없으면 기본값)</summary>
    private Profiles.PowerWiring Wiring => power?.Current() ?? Profiles.PowerWiring.Default;
    /// <summary>이번 연결에서 아이라가 허브 출력을 마지막으로 켠 시각 (그 뒤 10초 동안만 장비가 나타나기를 기다린다)</summary>
    private DateTime _switchedOnAt = DateTime.MinValue;
    private TimeSpan JustSwitchedWait => TimeSpan.FromSeconds(10) - (DateTime.UtcNow - _switchedOnAt) is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero;

    /// <summary>장비 하나 연결 + 걸린 시간을 logs\connect-날짜.log에 (2026-10-10 — 오래 걸리는 이유를 보려고)</summary>
    private ConnectTimingLog? _timing;
    /// <summary>연결에 성공하면 배울 것 (장비, 증거)</summary>
    private (string Kind, string Evidence)? _learnOnPass;

    /// <summary>화면에 미리 칸을 그릴 수 있게, 연결할 장비 목록만 먼저 알려 준다.</summary>
    /// 화면에 들어올 때마다 부르므로, 시뮬레이션의 실패 목록과 "없이 진행" 기록도 여기서 처음 상태로 되돌린다.
    public async Task<IReadOnlyList<CheckResult>> PlanAsync(CancellationToken ct = default)
    {
        if (options.Value.Simulate) sim.Reset(options.Value.SimulateFailing);
        choices.Reset();
        return (await ReadDevicesAsync(ct)).Select(d => d.Absent ? Make(d, CheckStatus.Absent, "") : Pending(d)).ToList();
    }

    // ── 연결 중 종료 (2026-10-09 사용자 요청): AA를 끄면 연결을 바로 멈추고, 이번에 연결하던 장비의 연결을 끊은 것을 확인한 뒤 닫는다.
    // 연결 요청을 취소해도 N.I.N.A.는 하던 연결을 끝까지 하므로(실기: AA가 꺼진 뒤 "스위치 연결됨") 끊고, 늦게 붙는지도 잠시 지켜본다
    // 연결기는 요청마다 새로 만들어지므로(Transient) 진행 상태는 앱에 하나인 EquipmentRun에 둔다
    // (2026-10-09 시험: 상태를 연결기에 두었더니 종료 요청이 연결 중인 것을 보지 못함)
    private Lock _gate => state.Gate;
    private CancellationTokenSource? _runCts { get => state.Cts; set => state.Cts = value; }
    private TaskCompletionSource? _runDone { get => state.Done; set => state.Done = value; }
    private List<string> _touched => state.Touched;

    private CancellationToken BeginRun(CancellationToken ct, bool fresh, string? only = null)
    {
        lock (_gate)
        {
            if (fresh) _touched.Clear();
            // 새 연결이 그 장비들을 다시 맡는다 — 끝난 뒤 정상 연결을 종료가 끊지 않게. 장비 하나만 다시 연결하면 그 장비만 (CX-25-01).
            // 답을 못 받은 요청 기록(Unanswered)은 여기서 지우지 않는다 — 그 장비의 새 요청이 답을 받아야 지운다
            if (fresh) state.Unresolved.Clear();
            else if (only is not null) state.Unresolved.Remove(only);
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _runCts.Token;
        }
    }

    private void EndRun()
    {
        lock (_gate)
        {
            _runDone?.TrySetResult();
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void Touch(string kind)
    {
        lock (_gate) if (!_touched.Contains(kind)) _touched.Add(kind);
    }

    /// <summary>
    /// 종료 전: 연결 중이면 멈추고, 이번에 연결을 시도한 장비를 끊고 끊긴 것을 확인한다 (전원 허브는 맨 나중).
    /// 연결 중이 아니었고 답을 기다리는 연결 요청도 없으면 아무것도 하지 않는다. 끊지 못한(또는 확인하지 못한) 장비가 있으면 그 이름들 —
    /// 그 장비들은 이번에 시도한 장비 목록에 남겨 다시 눌렀을 때 또 지켜본다 (CX-21-01)
    /// </summary>
    public async Task<IReadOnlyList<string>> AbortAsync(CancellationToken ct)
    {
        Task done;
        List<string> kinds;
        lock (_gate)
        {
            if (_runCts is null && state.InFlight.Count == 0 && state.Unresolved.Count == 0 && state.Unanswered.Count == 0) return [];
            // 연결이 진행 중이면 이번에 시도한 장비 전체, 끝났으면 답을 못 받은 요청과 지난번에 확인 못 한 장비만 (끝난 연결은 정상 연결 — 촬영 중일 수 있다)
            var leftover = state.InFlight.Keys.Union(state.Unresolved).Union(state.Unanswered);
            kinds = _runCts is null ? [.. leftover] : [.. _touched.Union(leftover)];
            _runCts?.Cancel();
            done = _runDone?.Task ?? Task.CompletedTask;
        }
        try { await done.WaitAsync(TimeSpan.FromSeconds(5), ct); } catch (TimeoutException) { }
        if (options.Value.Simulate)
        {
            lock (_gate) { _touched.Clear(); state.InFlight.Clear(); state.Unresolved.Clear(); state.Unanswered.Clear(); }
            return [];
        }

        // 1. 보낸 연결 요청의 답을 기다린다 (요청 한도 60초 + 여유) — 답이 오기 전에는 N.I.N.A.가 언제든 연결을 끝낼 수 있어 "조용함"이 증거가 못 된다
        KeyValuePair<string, Task<bool?>>[] pending;
        lock (_gate) pending = [.. state.InFlight];
        if (pending.Length > 0)
        {
            log.LogInformation("연결 멈춤: 답을 기다리는 연결 요청 {Kinds}", string.Join(", ", pending.Select(p => p.Key)));
            try { await Task.WhenAll(pending.Select(p => p.Value.ContinueWith(_ => { }, TaskScheduler.Default))).WaitAsync(TimeSpan.FromSeconds(65), ct); }
            catch (TimeoutException) { }
        }
        // 답이 없었던 요청(아직 안 끝남, 또는 시간 초과·통신 오류로 끝남)은 지금 끊겨 보여도 확인한 것이 아니다 (CX-24-01)
        var unanswered = new List<string>();
        lock (_gate)
            foreach (var (kind, request) in pending)
            {
                if (request is not { IsCompletedSuccessfully: true, Result: not null }) unanswered.Add(kind);
                if (request.IsCompleted && state.InFlight.GetValueOrDefault(kind) == request) state.InFlight.Remove(kind);
            }

        // 모든 장비를 함께 1초마다 보며 붙어 있으면 끊는다 (허브는 다른 장비가 다 끊긴 뒤에). 모두 끊긴 채로 몇 번 연속이면 끝.
        // 하던 연결이 늦게 붙으면 다시 끊는다 (최대 30초) — 장비마다 차례로 지켜보면 17초 걸렸음 (2026-10-09 시험).
        // 상태를 읽지 못한 것은 끊긴 것으로 치지 않는다 — 끝까지 모르면 "확인 못 함"으로 돌려준다 (CX-20-01)
        var order = Slots.Select(s => s.Kind).Reverse().Where(kinds.Contains).ToList();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        const int needQuiet = 3;
        var quiet = 0;
        var still = new List<string>();
        var unknown = new List<string>();
        var sawConnected = new HashSet<string>(); // 연결된 것을 본 장비 — 그 뒤 끊긴 것을 확인하면 하던 연결이 끝났다는 증거
        while (quiet < needQuiet && DateTime.UtcNow < deadline)
        {
            still.Clear();
            unknown.Clear();
            foreach (var kind in order)
                switch (await nina.ConnectedStateAsync(kind, ct))
                {
                    case true: still.Add(kind); sawConnected.Add(kind); break;
                    case null: unknown.Add(kind); break;
                }
            if (still.Count == 0 && unknown.Count == 0) quiet++;
            else
            {
                quiet = 0;
                var others = still.Where(k => k != Hub).ToList();
                if (still.Count > 0) await Task.WhenAll((others.Count > 0 ? others : still).Select(k => nina.DisconnectAsync(k, ct)));
            }
            await Task.Delay(1000, ct);
        }
        // 끊긴 것을 확인한 장비만 목록에서 뺀다. 답을 못 받은 요청은 지금 끊겨 보여도 확인한 것이 아니다 —
        // 그 기록(Unanswered)은 다시 중단해도 "끊겨 보임"만으로 지우지 않고, 이번에 연결된 것을 본 뒤 끊긴 것을 확인했을 때만 지운다 (CX-25-01).
        // 그 전에는 종료 창이 계속 알리고 "그래도 종료"는 사용자가 정한다
        List<string> stillUnanswered;
        lock (_gate)
        {
            state.Unanswered.UnionWith(unanswered);
            state.Unanswered.RemoveWhere(k => sawConnected.Contains(k) && !still.Contains(k) && !unknown.Contains(k));
            stillUnanswered = [.. state.Unanswered.Where(order.Contains)];
        }
        var unsure = unknown.Union(stillUnanswered).ToList();
        var confirmed = order.Except(still).Except(unsure).ToList();
        lock (_gate)
        {
            _touched.RemoveAll(confirmed.Contains);
            state.Unresolved.Clear();
            state.Unresolved.UnionWith(still.Union(unsure));
        }
        foreach (var kind in confirmed) live.Forget(kind);
        return still.Select(k => FindSlot(k)?.Role ?? k)
            .Concat(unknown.Select(k => $"{FindSlot(k)?.Role ?? k}(상태를 읽지 못함)"))
            .Concat(stillUnanswered.Except(still).Except(unknown).Select(k => $"{FindSlot(k)?.Role ?? k}(연결 요청이 끝나지 않음)"))
            .ToList();
    }

    public async IAsyncEnumerable<CheckResult> RunAsync([EnumeratorCancellation] CancellationToken outer = default)
    {
        var ct = BeginRun(outer, fresh: true);
        try
        {
        var devices = await ReadDevicesAsync(ct);
        if (devices.Count == 0)
        {
            yield return new CheckResult("profile", "N.I.N.A. 프로필", null, null, CheckSeverity.Required, CheckStatus.Fail,
                "장비 설정을 읽지 못했습니다", new Diagnosis([], "N.I.N.A.가 켜져 있고 장비가 프로필에 등록되어 있는지 확인한 뒤 새로고침을 눌러 주세요."));
            yield break;
        }

        var hubFailed = false;
        var wiring = Wiring;
        var runClock = System.Diagnostics.Stopwatch.StartNew();
        ConnectTimingLog.Append($"{DateTime.Now:HH:mm:ss.fff}  === 장비 연결 시작 ({devices.Count(x => !x.Absent)}개{(options.Value.Simulate ? ", 모의" : "")})");
        foreach (var d in devices)
        {
            // 허브가 실패하면 허브부터 해결한다: 전원 배선상 허브에서 전원을 받는 장비만 보류 (docs/implementation/POWER_WIRING.md — 배선을 믿는다).
            // 다만 PC에 보이는지 바로 알 수 있는 장비가 보이면 전원이 있다는 증거 → 연결하고, 허브에 전원이 없던 것이면 배선을 고친다
            // 등록되지 않은 장비: 연결하지 않고 작은 원으로만 보인다
            if (d.Absent)
            {
                yield return Make(d, CheckStatus.Absent, "");
                continue;
            }
            if (hubFailed && wiring.BehindHub(d.Slot.Kind) && !await PoweredAnywayAsync(d, ct))
            {
                yield return Make(d, CheckStatus.Skipped, WaitingForHub);
                continue;
            }
            if (d.Id is not null) yield return Make(d, CheckStatus.Running, "연결하는 중입니다");
            var result = await ConnectTimedAsync(d, ct);
            yield return result;
            if (d.Slot.Kind == Hub && result.Status != CheckStatus.Pass) hubFailed = true;
            // (예전: 파워박스 0V인데 적도의가 연결되면 "파워박스는 DC 직결"로 저장 — 적도의 출력 꺼짐·케이블 빠짐과 구분할 수 없어 뺐다, CX-20-07)
        }
        ConnectTimingLog.Append($"{DateTime.Now:HH:mm:ss.fff}  === 장비 연결 끝 {runClock.ElapsedMilliseconds}ms");
        }
        finally { EndRun(); }
    }

    /// <summary>
    /// 장비 하나만 다시 연결한다.
    /// simulateFail: [임시] 화면 설계용 — 실패했을 때와 똑같은 결과를 만든다 (Simulate가 켜져 있을 때만).
    /// </summary>
    public async Task<CheckResult?> RetryAsync(string id, bool simulateFail = false, CancellationToken ct = default)
    {
        var d = (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == id);
        if (d is null) return null;
        // [임시] 시뮬레이션: 사용자가 원인을 해결하고 "다시 연결"을 눌렀다고 보고, 이 장비는 이제 성공한다
        if (options.Value.Simulate && !simulateFail) sim.Fix(id);
        var token = BeginRun(ct, fresh: false, only: id);
        try { return await ConnectTimedAsync(d, token, simulateFail && options.Value.Simulate, retry: true); }
        finally { EndRun(); }
    }

    /// <summary>장비 하나의 지금 모습 (연결 전). 장비 변경 모드에서 고른 뒤 칸을 바꿀 때</summary>
    public async Task<CheckResult?> DescribeAsync(string kind, CancellationToken ct = default) =>
        (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == kind) is { } d
            ? d.Absent ? Make(d, CheckStatus.Absent, "") : Pending(d)
            : null;

    /// <summary>지금 이 종류로 쓰는 장비의 드라이버 Id (AA에서 고른 것 → 프로필). 없으면 null</summary>
    public async Task<string?> CurrentIdAsync(string kind, CancellationToken ct = default) =>
        (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == kind)?.Id;

    /// <summary>준필수 장비를 "없이 진행". 필수·선택 장비는 대상이 아니다.</summary>
    public async Task<CheckResult?> GoWithoutAsync(string id, CancellationToken ct = default)
    {
        var d = (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == id);
        if (d is null || d.Slot.Tier != Recommended) return null;
        choices.GoWithout(id);
        return Make(d, CheckStatus.Warn, $"{d.Slot.Role} 없이 진행합니다");
    }

    private async Task<CheckResult> ConnectTimedAsync(Device d, CancellationToken ct, bool forceFail = false, bool retry = false)
    {
        _timing = new ConnectTimingLog();
        try
        {
            _learnOnPass = null;
            var result = await ConnectOneAsync(d, ct, forceFail);
            if (result.Status == CheckStatus.Pass && _learnOnPass is { } learn && learn.Kind == d.Slot.Kind)
                power?.Learn(learn.Kind, Profiles.PowerWiring.Hub, learn.Evidence);
            _learnOnPass = null;
            _timing.Write(d.Slot.Kind, result.Status.ToString(), (retry ? "다시 연결 · " : "") + result.Message);
            log.LogInformation("장비 연결 {Kind}: {Status} — {Message}{Retry}", d.Slot.Kind, result.Status, result.Message, retry ? " (다시 연결)" : "");
            return result;
        }
        catch (OperationCanceledException)
        {
            _timing.Write(d.Slot.Kind, "Canceled");
            throw;
        }
        finally { _timing = null; }
    }

    private async Task<CheckResult> ConnectOneAsync(Device d, CancellationToken ct, bool forceFail = false)
    {
        if (d.Absent) return Make(d, CheckStatus.Absent, "");

        // 망원경: 연결 대신 고른 망원경의 초점거리·F값을 N.I.N.A.에 써 넣는다 (시뮬레이션이어도 — N.I.N.A.가 화각·솔빙에 쓴다)
        if (d.Slot.Kind == Scope)
        {
            if (optics.Current is not { } scope)
                return Make(d, CheckStatus.Fail, "등록된 망원경이 없습니다", new Diagnosis([], d.Slot.Fix));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            await nina.ChangeProfileValueAsync("TelescopeSettings-FocalLength", Math.Round(scope.EffectiveFocalLength, 1).ToString(inv), ct);
            await nina.ChangeProfileValueAsync("TelescopeSettings-FocalRatio", Math.Round(scope.EffectiveFocalRatio, 2).ToString(inv), ct);
            await nina.ChangeProfileValueAsync("TelescopeSettings-Name", scope.Name, ct);
            // 솔버가 있어야 센터링을 할 수 있다 (H01) — 망원경 칸에서 알린다
            if (!options.Value.Simulate && await nina.GetActiveProfileAsync(ct) is { ValueKind: JsonValueKind.Object } prof && prof.TryGetProperty("PlateSolveSettings", out var ps)
                && DevicePrecheck.Solver(Text(ps, "PlateSolverType"), Text(ps, "ASTAPLocation")) is { } solver)
                return Make(d, CheckStatus.Warn, solver.Message, new Diagnosis([], solver.Fix));
            return Make(d, CheckStatus.Pass, scope.Optics);
        }

        // 필수 장비가 프로필에 없음: 연결을 시도할 수 없다
        if (d.Id is null)
            return Make(d, CheckStatus.Fail, $"N.I.N.A.에 등록된 {d.Slot.Role}{Josa(d.Slot.Role)} 없습니다", new Diagnosis([],
                $"아래 \"장비 변경\"에서 {d.Slot.Role}{Reul(d.Slot.Role)} 등록해 주세요. 처음 쓰는 드라이버라면 N.I.N.A.의 장비 탭에서 드라이버 설정(톱니바퀴)도 한 번 해 주세요."));

        bool connected;
        if (forceFail)
        {
            connected = false;
        }
        else if (options.Value.Simulate)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), ct); // 연결되는 모습이 보이게 잠깐 기다린다
            connected = !sim.IsFailing(d.Slot.Kind);
        }
        else
        {
            // 실기 미검증: 장비를 연결할 수 있는 환경에서 확인 필요
            // N.I.N.A.의 "연결됨"은 어떤 장비인지 알려 주지 않는다. 그래서 "이미 연결됨"을 믿는 것은
            //  - AA가 이 장비를 연결했다고 기록해 둔 경우, 또는
            //  - AA에서 따로 고른 적이 없어 N.I.N.A. 프로필 그대로인 경우(N.I.N.A.가 연결한 것 = 프로필 장비)만.
            // 그 밖에는(AA에서 다른 장비를 골랐는데 확인된 적 없음) 지금 연결을 끊고 고른 장비로 새로 연결한다 (2026-09-30 리뷰)
            var kind = d.Slot.Kind;
            Touch(kind);
            using var behind = BackgroundWindows.ForConnect(kind); // PHD2·Wanderer Empire가 켜지며 AA 위로 뜨지 않게
            var connectedNow = await nina.IsConnectedAsync(kind, ct);
            var known = live.Get(kind);
            var trust = connectedNow && (known == d.Id || (known is null && !d.Chosen));
            if (trust)
            {
                connected = true;
            }
            else
            {
                // 연결 직전 점검 (docs/implementation/PRECHECK.md ③): 장비가 PC에 보이지 않으면 N.I.N.A.에 맡기지 않고 할 일을 알린다 —
                // N.I.N.A. 오류 알림·2분 기다림 없이. 허브 USB 출력을 막 켰을 수 있어 USB 장치는 10초까지 기다려 본다
                if (!connectedNow)
                {
                    if (kind == Hub && DevicePrecheck.HubAppStale(host))
                    {
                        // Empire가 켜진 뒤 USB가 다시 꽂힘 → Empire가 허브와 끊겨 있을 수 있다. 닫아 두면 N.I.N.A. 연결이 새로 띄우며 자동으로 연결 (10/09 실기)
                        log.LogInformation("허브 프로그램이 켜진 뒤 USB 장치가 새로 꽂혀 다시 켭니다");
                        await Task.Run(host.CloseHubApp, ct);
                    }
                    // 기다리지 않고 한 번만 확인한다 (2026-10-10 사용자 결정 — 기록상 꺼진 장비를 10초씩 기다린 게 대부분: 안 되면 바로 실패 + 다시 연결).
                    // 다만 이번에 아이라가 허브 출력(USB 등)을 막 켰으면 USB 장치가 나타나는 데 몇 초 걸려 켠 뒤 10초까지만 남은 만큼 기다린다
                    var pre = await DevicePrecheck.CheckAsync(host, kind, d.Id, d.Slot.Role, JustSwitchedWait, ct);
                    _timing?.Step("PC에 보이는지");
                    // 카메라 전원을 허브 출력에서 받으면(장비 변경에서 고르고 전압을 확인받음) 그 출력을 켜고 카메라가 나타나기를 20초 기다린다 (J05, 10/09 실기: 14초 — 2026-10-10 30초에서 줄임)
                    if (!pre.Ok && kind == "camera" && cameraPower.Outlet is { } outlet && await nina.IsConnectedAsync(Hub, ct))
                    {
                        var before = _switchedOnAt;
                        var turned = await SetHubOutputAsync(n => n == outlet, true, ct);
                        var switchedNow = _switchedOnAt != before; // 이미 켜져 있던 출력이면 "켜자 나타남"의 증거가 아니다
                        pre = turned == true
                            ? await DevicePrecheck.CheckAsync(host, kind, d.Id, d.Slot.Role, TimeSpan.FromSeconds(20), ct) is { Ok: true } p ? LearnIfConnected(p, kind, switchedNow, "파워박스 출력을 켜자 나타나 연결됨")
                                : new(false, $"카메라 전원(허브 {ShortOutlet(outlet)})을 켰지만 카메라가 PC에 보이지 않습니다",
                                    $"카메라 전원 스위치가 ON인지, 전원 어댑터가 허브 {ShortOutlet(outlet)}에 꽂혀 있는지, USB 케이블을 확인해 주세요.")
                            : new(false, $"카메라 전원(허브 {ShortOutlet(outlet)})을 켜지 못했습니다",
                                turned is null ? $"전원 허브에 {ShortOutlet(outlet)} 출력이 없습니다. 장비 변경에서 카메라 전원을 다시 골라 주세요." : "Wanderer Empire에서 출력이 켜지는지 확인해 주세요.");
                        _timing?.Step("카메라 전원 켜기");
                    }
                    if (!pre.Ok)
                    {
                        live.Forget(kind);
                        var diag = new Diagnosis([], $"{pre.Fix}{WiringHint(kind)} 그다음 다시 연결을 눌러 주세요.");
                        return d.Slot.Tier == Optional && kind != Hub
                            ? Make(d, CheckStatus.Warn, $"{pre.Message} — {d.Slot.Role} 없이 진행합니다", diag)
                            : Make(d, CheckStatus.Fail, pre.Message!, diag);
                    }
                }
                if (connectedNow) await nina.DisconnectAsync(kind, ct);
                // N.I.N.A. 장비 목록에 끝내 없으면 연결을 시도하지 않는다 — N.I.N.A. 오류 알림 대신 할 일 (Codex B05)
                var listed = await WaitListedAsync(kind, d.Id, ct);
                _timing?.Step("N.I.N.A. 목록");
                if (listed == false)
                {
                    live.Forget(kind);
                    var miss = new Diagnosis(["드라이버가 이 PC에 설치되어 있지 않습니다", "장비 전원이 꺼져 있어 목록에 나타나지 않습니다(플러그인 카메라 등)", "N.I.N.A. 프로필의 장비가 다른 PC에서 고른 것입니다"],
                        $"장비 전원과 USB를 확인하고, 처음 쓰는 장비라면 제조사 드라이버를 설치해 주세요. 아래 \"장비 변경\"에서 이 PC에 있는 장비를 다시 고를 수도 있습니다. (찾는 장비: {d.DriverName})");
                    return d.Slot.Tier == Optional && kind != Hub
                        ? Make(d, CheckStatus.Warn, $"N.I.N.A.가 {d.Slot.Role}{Reul(d.Slot.Role)} 찾지 못해 {d.Slot.Role} 없이 진행합니다", miss)
                        : Make(d, CheckStatus.Fail, $"N.I.N.A.가 {d.Slot.Role}{Reul(d.Slot.Role)} 찾지 못했습니다", miss);
                }
                // 요청은 취소 토큰 없이 (연결 요청 한도 60초는 그대로) — 멈추면 기다리기만 그만두고, 답은 종료(AbortAsync)가 기다린다
                // 답을 받지 못하고 끝난 요청(시간 초과·통신 오류)도 남겨 둔다 — N.I.N.A.는 연결을 계속하고 있을 수 있다 (CX-24-01)
                var request = nina.ConnectAnsweredAsync(kind, d.Id, CancellationToken.None);
                lock (_gate) state.InFlight[kind] = request;
                try { connected = await request.WaitAsync(ct) == true && await nina.IsConnectedAsync(kind, ct); }
                finally
                {
                    // 답을 받았으면 이 장비의 "답 없음" 기록도 끝 (N.I.N.A.가 이 장비 연결을 마쳤다)
                    if (request is { IsCompletedSuccessfully: true, Result: not null })
                        lock (_gate)
                        {
                            if (state.InFlight.GetValueOrDefault(kind) == request) state.InFlight.Remove(kind);
                            state.Unanswered.Remove(kind);
                        }
                }
                _timing?.Step("N.I.N.A. 연결");
                // 적도의가 연결되면 그 ASCOM 드라이버를 기억 — N.I.N.A.가 답하지 않을 때 직접 멈추는 비상 정지가 이 적도의 드라이버를 쓰게 (자체 점검 M2)
                if (connected && kind == "mount" && axis is not null && d.Id.StartsWith("ASCOM.", StringComparison.OrdinalIgnoreCase)) axis.DriverId = d.Id;
            }
            // 허브가 USB로만 붙고 전원(DC)이 안 들어오면(10/09: USB만으로도 연결됨) 뒤 장비를 N.I.N.A.에 맡기지 않는다 —
            // 허브 입력 전압이 곧 장비 전원 (사용자 배선: 외부 전원 → 적도의 → 새들 → 허브). 전압 칸을 모르면 그냥 진행
            if (connected && kind == Hub && await HubInputVoltsAsync(ct) is { } volts && volts < NoPowerVolts)
            {
                // 안내는 배선을 믿고 (적도의 먼저면 적도의 전원부터)
                var w = Wiring;
                var fix = w.SourceOf(Hub) == Profiles.PowerWiring.Mount
                    ? "파워박스는 적도의에서 전원을 받습니다. 적도의 전원을 켜면 파워박스도 켜집니다. 적도의 전원과 적도의 → 파워박스 케이블을 확인한 뒤 다시 연결을 눌러 주세요."
                    : w.SourceOf("mount") == Hub
                        ? "적도의를 포함한 모든 장비가 파워박스에서 전원을 받습니다. 파워박스 전원(DC 전원)을 켜고 입력 케이블을 확인한 뒤 다시 연결을 눌러 주세요."
                        : "파워박스 전원(DC 전원)을 켜고 입력 케이블을 확인한 뒤 다시 연결을 눌러 주세요. 파워박스에서 전원을 받는 장비는 그때 연결합니다.";
                return Make(d, CheckStatus.Fail, $"전원 허브에 전원이 들어오지 않습니다 (입력 {volts:0.0}V)", new Diagnosis(
                    ["전원이 꺼져 있습니다", "파워박스로 가는 전원 케이블이 빠졌습니다"], fix));
            }
            // 허브가 연결되면 USB 출력을 켜 둔다 — 가이드 카메라·포커서가 허브 USB에 붙어 있다 (자동 해결, 다시 읽어 확인 — 10/09: N.I.N.A.는 실패해도 성공이라 답함)
            if (connected && kind == Hub)
            {
                _timing?.Step("허브 전압");
                await EnsureHubUsbOnAsync(ct);
                _timing?.Step("허브 USB 출력");
            }
            if (connected) live.Set(kind, d.Id);
            else live.Forget(kind);

            // 가이더: N.I.N.A.가 PHD2 프로그램에 붙은 것만으로는 부족 — PHD2 안의 카메라·적도의까지 직접 확인 (2026-10-06)
            if (connected && kind == "guider")
            {
                var problem = await CheckPhd2Async(ct);
                _timing?.Step("PHD2 확인");
                if (problem is not null) return Make(d, CheckStatus.Fail, problem.Message, new Diagnosis([], problem.Fix));
            }
        }

        if (connected)
        {
            choices.Connected(d.Slot.Kind);
            // 연결 직후 점검 (docs/implementation/PRECHECK.md ④⑤): 시뮬레이터가 골라졌는지(A06), 카메라면 사진 저장 폴더(I01~I03)
            if (!options.Value.Simulate)
            {
                var note = DevicePrecheck.Simulator(d.Slot.Role, d.Name);
                if (note is null && d.Slot.Kind == "camera" && await nina.GetActiveProfileAsync(ct) is { ValueKind: JsonValueKind.Object } prof && prof.TryGetProperty("ImageFileSettings", out var files))
                    note = DevicePrecheck.Storage(Text(files, "FilePath"));
                if (note is null && d.Slot.Kind == "mount" && await nina.GetActiveProfileAsync(ct) is { ValueKind: JsonValueKind.Object } p2 && p2.TryGetProperty("AstrometrySettings", out var astro)
                    && await nina.GetInfoAsync("mount", ct) is { ValueKind: JsonValueKind.Object } mi)
                    note = DevicePrecheck.MountSite(Prepare.Real.NinaRig.Num(astro, "Latitude"), Prepare.Real.NinaRig.Num(astro, "Longitude"), Prepare.Real.NinaRig.Num(mi, "SiteLatitude"), Prepare.Real.NinaRig.Num(mi, "SiteLongitude"));
                if (note is not null)
                    return Make(d, note.Block ? CheckStatus.Fail : CheckStatus.Warn, note.Message, new Diagnosis([], note.Fix));
            }
            return Make(d, CheckStatus.Pass, "연결되어 있습니다");
        }

        var diagnosis = new Diagnosis([],
            $"{d.Slot.Fix} 그다음 다시 연결을 눌러 주세요. 처음 쓰는 장비라면 N.I.N.A.의 장비 탭에서 드라이버 설정(톱니바퀴)을 먼저 해 주세요. (드라이버: {d.DriverName})");
        // 선택 장비는 경고만 (허브는 뒤 장비의 전원이라 실패로 둔다)
        return d.Slot.Tier == Optional && d.Slot.Kind != Hub
            ? Make(d, CheckStatus.Warn, $"연결되지 않아 {d.Slot.Role} 없이 진행합니다", diagnosis)
            : Make(d, CheckStatus.Fail, "연결되어 있지 않습니다", diagnosis);
    }

    // 장비 이름 뒤 조사 (카메라가 / 적도의가 — 모두 받침 없음이지만 이름이 바뀌어도 맞게)
    private static string Josa(string word) => Astro.Core.Josa.HasFinalConsonant(word) ? "이" : "가";
    private static string Reul(string word) => Astro.Core.Josa.HasFinalConsonant(word) ? "을" : "를";

    private static CheckResult Pending(Device d) => Make(d, CheckStatus.Pending, "");

    private static CheckResult Make(Device d, CheckStatus status, string message, Diagnosis? diagnosis = null) =>
        // 칸 가운데에는 장비 종류(적도의 등), 그 아래 작게 장비 이름(OnStep 등)
        new(d.Slot.Kind, d.Slot.Role, d.Name, d.Slot.Hint, d.Slot.Tier, status, message, diagnosis);
}

/// <summary>장비 연결 진행 상태 (앱에 하나): 연결 취소용 토큰, 끝남 신호, 이번에 연결을 시도한 장비</summary>
public sealed class EquipmentRun
{
    public Lock Gate { get; } = new();
    public CancellationTokenSource? Cts { get; set; }
    public TaskCompletionSource? Done { get; set; }
    public List<string> Touched { get; } = [];
    /// <summary>
    /// N.I.N.A.에 보낸 연결 요청 중 아직 답이 없는 것 (장비 → 그 요청). 연결을 멈춰도 요청 자체는 취소하지 않는다 — N.I.N.A.는 그 연결을 끝까지 하므로,
    /// 답이 온 것(= N.I.N.A.가 연결을 끝냄)을 종료가 기다린 뒤 끊는다 (CX-20-01, CX-21-01)
    /// </summary>
    public Dictionary<string, Task<bool?>> InFlight { get; } = [];
    /// <summary>지난 연결 중단(종료)에서 끊긴 것을 확인하지 못한 장비 — 다시 중단하면 이 장비들만 다시 본다 (CX-24-02: 이번에 시도한 장비 전체를 다시 보면 촬영 중 정상 연결까지 끊는다)</summary>
    public HashSet<string> Unresolved { get; } = [];
    /// <summary>연결 요청의 답을 끝내 받지 못한 장비 (시간 초과·통신 오류) — N.I.N.A.가 그 연결을 아직 하고 있을 수 있다. 연결된 것을 본 뒤 끊김을 확인하거나, 그 장비의 새 요청이 답을 받아야 지운다 (CX-25-01)</summary>
    public HashSet<string> Unanswered { get; } = [];
}
