using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.Slew;

/// <summary>③ 대상 이동 장비. 실제: N.I.N.A. slew(RA는 도 단위)·abort, 고도는 계획 좌표와 관측지로 계산</summary>
public interface ISlewDevices
{
    /// <summary>대상의 지금 고도(도)</summary>
    double TargetAltitude(PrepContext ctx);
    /// <summary>이동할 수 있는 고도(SlewTask.LowestAltitude, 15°) 위로 올라올 때까지 남은 분 (오늘 밤 안 올라오면 null)</summary>
    int? MinutesUntilUsable(PrepContext ctx);
    /// <summary>기다리는 동안 다시 확인하는 간격</summary>
    Task WaitTickAsync(CancellationToken ct);
    /// <summary>대상으로 이동. 남은 거리(도)를 알린다. 도착 오차(도)를 돌려준다</summary>
    Task<SlewMove> SlewToTargetAsync(double raDeg, double decDeg, Action<double> remainingDeg, CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<SlewEndState> ReadEndStateAsync(PrepContext ctx, CancellationToken ct);
}

public sealed record SlewMove(bool Ok, double ArrivalErrorDeg, string? Problem = null);
public sealed record SlewEndState(double DistanceToTargetDeg, bool Moving, bool Tracking);

/// <summary>
/// ③ 대상 이동 (OPERATION_PROCEDURE.md ④ 이동): 계획의 이동 버튼이 이동 승인. 이동하는 때는 계획에서 정한 "촬영 시작 − 준비 시간"이고
/// 대상이 15° 이상이어야 한다 (2026-10-14 사용자 결정 — 예전의 "30° 아래면 막음"은 폐기). 그 전이면 "기다리기"(남은 시간 + 때가 되면 알림).
/// 15° 아래면 이동을 막고, 시각만 이르면 "지금 이동"으로 먼저 옮길 수 있다. 이동 중 "멈춤".
/// </summary>
public sealed class SlewTask(ISlewDevices devices) : IPrepTask
{
    /// <summary>이보다 낮은 대상으로는 옮기지 않는다 (PlanRules.LowestAltitude)</summary>
    public const double LowestAltitude = Assistant.PlanRules.LowestAltitude;
    public const double ArrivalLimitDeg = 2;

    public string Id => "slew";
    public string Title => "대상 이동";
    public string StartLabel => "대상으로 이동";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("check", "이동 조건 확인"), new("move", "대상으로 이동"), new("arrive", "도착 확인")];

    public bool AppliesTo(PrepContext ctx) => true;

    /// <summary>이동이 안 될 때 "지금 위치를 목표로" — 핸드 컨트롤러·성도 앱으로 이미 맞춘 경우 (2026-10-08 사용자 결정). 센터링도 건너뜀</summary>
    private static readonly PrepAction AcceptHere = new("here", "지금 위치를 목표로");
    private bool _acceptHere;
    /// <summary>시각이 이른데 사용자가 "지금 이동"을 골랐다 (한 번 쓰고 끈다)</summary>
    private bool _moveNow;
    /// <summary>
    /// 사용자가 이번에 이동을 누른 것이 있는가 (2026-10-14 사용자 결정 — 큰 이동은 늘 누른 뒤에만): 계획 화면에서 방금 누른 이동 버튼(확정 직후 한 번),
    /// "지금 이동", 때가 된 뒤의 "이동", 멈춘 뒤의 "다시 이동"·실패 뒤의 "다시 시도". 기다리기에 들어가면 지운다(때가 되면 다시 누른다).
    /// 장비 준비를 다시 하고 돌아올 때·아이라를 다시 켜 이어 갈 때·N.I.N.A.를 다시 켠 뒤에는 승인이 없어 "○○로 이동"을 묻는다
    /// </summary>
    private bool _approved;
    /// <summary>적도의를 잡았을 때 대상이 이미 낮아져 이동을 보내지 않았다 (CX-36-03)</summary>
    private bool _lowAtLock;
    /// <summary>계획 화면의 이동 버튼을 누르고 이만큼 안에 시작한 대상 이동만 그 누름을 이동 승인으로 본다</summary>
    private static readonly TimeSpan ApprovalWindow = TimeSpan.FromMinutes(2);

    private Completed Accepted(PrepContext ctx) =>
        new(new SlewResult(0, devices.TargetAltitude(ctx), ctx.Now(), AcceptedHere: true), "지금 위치를 목표로", "지금 위치에서 찍어요",
            "적도의를 옮기지 않고 지금 가리키는 곳을 대상으로 봐요. 가운데 맞추기(센터링)도 건너뛰니, 시험 사진에서 구도를 확인해 주세요.") { AutoNext = true };

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        _acceptHere = false;
        _moveNow = false;
        _lowAtLock = false;
        _approved = ctx.MoveApprovalUsed != ctx.Plan.ConfirmedAt && DateTimeOffset.Now - ctx.Plan.ConfirmedAt < ApprovalWindow;
        ctx.MoveApprovalUsed = ctx.Plan.ConfirmedAt;
        run.Live("sky-map");
        DateTimeOffset? movedAt = null;
        while (true)
        {
            run.SubStep(0);
            await WaitUntilMoveAsync(run, ct);
            if (!_approved)
            {
                // 누름 없이는 움직이지 않는다: 케이블 안내와 함께 이동 버튼
                ShowAltitude(run, ctx);
                run.Guide($"{Core.Josa.Ro(ctx.TargetName)} 이동합니다", "누르면 적도의가 대상 쪽으로 크게 돕니다. 케이블이 당기거나 걸리지 않는지 봐 주세요.");
                run.Status(null);
                await run.AskAsync([new("move", $"{Core.Josa.Ro(ctx.TargetName)} 이동", true)], ct);
                _approved = true;
                if (TooLow(ctx)) continue; // 누르는 사이 낮아졌으면 다시 기다리기
            }
            movedAt ??= ctx.Now();
            var moved = await MoveAsync(run, ct);
            if (_acceptHere) return Accepted(ctx);
            if (moved is null)
            {
                // 잡는 사이 낮아짐 → 움직이지 않았다: 기다리기부터 다시(때가 되면 다시 누른다)
                if (_lowAtLock) { _lowAtLock = false; movedAt = null; }
                continue; // 멈춤 → 다시 이동을 누르면 처음부터(고도 확인 포함)
            }
            run.SubStep(2);
            if (moved.ArrivalErrorDeg > ArrivalLimitDeg)
            {
                run.Status($"목표에서 {moved.ArrivalErrorDeg:F1}° 벗어난 곳에 멈췄습니다. 다시 이동해 주세요", Tone.Fail);
                if (await run.AskAsync([new("move", "다시 이동", true), AcceptHere], ct) == "here") return Accepted(ctx);
                continue;
            }
            var alt = devices.TargetAltitude(ctx);
            run.Readout("distance", $"{moved.ArrivalErrorDeg:F1}°", "대상까지 남은 거리 · 목표 2° 안", Tone.Ok,
                new Dictionary<string, double> { ["arrivalDeg"] = moved.ArrivalErrorDeg, ["altitudeDeg"] = alt });
            run.Status(null);
            return new Completed(new SlewResult(moved.ArrivalErrorDeg, alt, ctx.Now(), MovedAt: movedAt), $"고도 {alt:F0}° · 도착 오차 {moved.ArrivalErrorDeg:F1}°",
                $"{ctx.TargetName}에 도착했습니다", "목표 근처에 도착했습니다. 다음은 사진을 찍어 대상을 화면 가운데로 맞추고, 그 사진으로 구도를 확인합니다.");
        }
    }

    /// <summary>옮길 때: 촬영 시작 − 준비 시간 (계획에서 쓴 값)</summary>
    private static DateTimeOffset MoveAt(PrepContext ctx) => ctx.Plan.Start - ctx.Plan.PrepLead;
    private bool TooLow(PrepContext ctx) => !ctx.IgnoreAltitude && devices.TargetAltitude(ctx) < LowestAltitude;
    private bool TooEarly(PrepContext ctx) => !ctx.IgnoreAltitude && !_moveNow && ctx.Now() < MoveAt(ctx);
    private static int MinutesUntil(PrepContext ctx, DateTimeOffset at) => Math.Max(1, (int)Math.Ceiling((at - ctx.Now()).TotalMinutes));
    private static string Topic(string name) => Core.Josa.Pick(name, "은", "는");

    /// <summary>
    /// 옮길 때가 될 때까지 기다린다 — 기다리는 화면으로 바로 (2026-10-16 사용자: "기다리기"를 따로 누르게 하면 할 수 있는 일은 결국 "지금 이동"뿐이고
    /// "기다리기 끝"과 오가며 버튼만 보였다 사라졌다). 너무 낮으면 버튼 없이 기다리기만(이동은 막음), 시각만 이르면 "지금 이동" 하나.
    /// 때가 되면 알리고 "○○로 이동"을 누른 뒤에 움직인다(큰 이동은 늘 누른 뒤에만)
    /// </summary>
    private async Task WaitUntilMoveAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        if (!TooLow(ctx) && !TooEarly(ctx)) return;
        _approved = false;
        CancellationTokenSource? asking = null;
        Task<string>? moveNow = null;
        bool? low = null;
        try
        {
            while (TooLow(ctx) || TooEarly(ctx))
            {
                ShowAltitude(run, ctx);
                var nowLow = TooLow(ctx);
                if (nowLow != low)
                {
                    // 기다리는 까닭이 바뀌었을 때만 안내·버튼을 바꾼다 (틱마다 다시 묻지 않는다 — 버튼이 깜빡이지 않게)
                    low = nowLow;
                    asking?.Cancel();
                    asking?.Dispose();
                    asking = null;
                    moveNow = null;
                    if (nowLow)
                    {
                        var minutes = devices.MinutesUntilUsable(ctx);
                        var when = minutes is { } m ? $"약 {m}분 뒤 {LowestAltitude:F0}° 위로 올라오면 알려 드립니다" : $"오늘 밤 {LowestAltitude:F0}° 위로 올라오지 않습니다";
                        run.Guide($"{ctx.TargetName}{Topic(ctx.TargetName)} 아직 낮습니다", $"{when}. 그동안 장비는 그대로 둡니다.");
                    }
                    else
                    {
                        run.Guide($"{ctx.TargetName}{Topic(ctx.TargetName)} {ctx.Plan.Start:HH:mm}부터 찍기로 했습니다",
                            $"준비 시간을 생각해 {MoveAt(ctx):HH:mm}에 이동합니다. 그동안 장비는 그대로 둡니다. 먼저 옮겨도 됩니다.");
                        asking = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        moveNow = run.AskAsync([new("move-now", "지금 이동", true)], asking.Token);
                    }
                }
                if (nowLow)
                {
                    var m = devices.MinutesUntilUsable(ctx);
                    run.Status(m is { } mm ? $"대상이 올라오기를 기다리는 중입니다 · 약 {mm}분 남음" : "대상이 올라오기를 기다리는 중입니다");
                    await devices.WaitTickAsync(ct);
                    continue;
                }
                run.Status($"이동 시각({MoveAt(ctx):HH:mm})을 기다리는 중입니다 · 약 {MinutesUntil(ctx, MoveAt(ctx))}분 남음");
                await Task.WhenAny(moveNow!, devices.WaitTickAsync(ct));
                if (moveNow!.IsCompletedSuccessfully)
                {
                    _moveNow = true;
                    _approved = true;
                    if (!TooLow(ctx)) return;
                    _approved = false; // 누르는 사이 15° 아래로 내려갔으면 다시 기다리기 (2026-10-14 자체 리뷰)
                    low = null;
                }
                ct.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            asking?.Cancel();
            asking?.Dispose();
        }
        // 때가 됨: 알리고(나중에 휴대폰 진동) 누른 뒤에 움직인다
        run.Status($"이동할 때가 됐어요 — {ctx.TargetName} 고도 {devices.TargetAltitude(ctx):F0}°", Tone.Ok);
        run.Guide($"{Core.Josa.Ro(ctx.TargetName)} 이동합니다", "누르면 적도의가 대상 쪽으로 크게 돕니다. 케이블이 당기거나 걸리지 않는지 봐 주세요.");
        await run.AskAsync([new("move", $"{Core.Josa.Ro(ctx.TargetName)} 이동", true)], ct);
        _approved = true;
    }

    private void ShowAltitude(ITaskRun run, PrepContext ctx)
    {
        var alt = devices.TargetAltitude(ctx);
        run.Readout("altitude", $"{alt:F0}°", $"{ctx.Plan.TargetName} 지금 고도 · {LowestAltitude:F0}° 위면 이동", alt >= LowestAltitude ? Tone.Ok : Tone.Warn,
            new Dictionary<string, double> { ["altitudeDeg"] = alt });
    }


    /// <summary>이동. 멈춤을 누르면 정지를 확인하고 null</summary>
    private async Task<SlewMove?> MoveAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        while (true)
        {
            run.SubStep(1);
            using var moving = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var asking = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task<string> stop;
            Task<SlewMove> slew;
            var stopPressed = false;
            var stopped = false;
            // 적도의를 먼저 잡는다 — 홈 이동 등 다른 작업이 쥐고 있으면 끝나기를 기다린다. 멈춤 버튼은 잡은 뒤에만 보인다
            // (2026-10-14 자체 리뷰: 잡기 전에 멈춤을 보여 눌러도 홈 이동은 계속되고, 홈이 끝나면 대상 이동이 잠깐 나갔다 멈췄다)
            run.Status("적도의가 다른 동작을 마치기를 기다리는 중입니다");
            await using (await ctx.Mount.AcquireAsync(ct))
            {
                // 기다리는 사이(홈 이동 등) 대상이 15° 아래로 내려갔으면 보내지 않는다 — 잡은 뒤 명령 바로 앞에서 다시 본다 (CX-36-03)
                if (TooLow(ctx))
                {
                    _lowAtLock = true;
                    return null;
                }
                run.Guide($"{Core.Josa.Ro(ctx.TargetName)} 이동하고 있습니다", "적도의가 대상 쪽으로 돌고 있습니다. 케이블이 걸리면 멈춤을 누르세요.");
                run.Status("이동 중입니다");
                stop = run.AskAsync([new("stop", "멈춤", true)], asking.Token);
                slew = devices.SlewToTargetAsync(ctx.Plan.RaDegrees, ctx.Plan.DecDegrees,
                    d => run.Readout("distance", $"{d:F1}°", "대상까지 남은 거리 (적도의 위치)", Tone.Busy, new Dictionary<string, double> { ["remainingDeg"] = d }, live: true), moving.Token);
                await Task.WhenAny(slew, stop);
                if (stop.IsCompletedSuccessfully)
                {
                    stopPressed = true;
                    moving.Cancel();
                    // 정지 명령은 이동 명령이 끝나기를 기다리지 않고 바로 (어댑터가 취소에 늦게 응답해도 멈추게, CX-PREP-CODE-02)
                    stopped = await TryStopAsync();
                    try { await slew.WaitAsync(StopWait); } catch (Exception e) when (e is OperationCanceledException or TimeoutException) { }
                }
            }
            if (stopPressed)
            {
                run.Guide("이동을 멈췄습니다", "케이블 등을 확인한 뒤 다시 이동하세요.");
                // 멈춘 것을 확인하기 전에는 다시 이동할 수 없다 (CX-PREP-CODE-02)
                while (!stopped)
                {
                    run.Status("적도의가 멈췄는지 확인하지 못했습니다. 적도의를 직접 확인한 뒤 장비 상태 다시 확인을 눌러 주세요", Tone.Fail);
                    await run.AskAsync([new("recheck-stop", "장비 상태 다시 확인", true)], ct);
                    await using (await ctx.Mount.AcquireAsync(ct))
                        stopped = await TryStopAsync();
                }
                run.Status("적도의가 멈춰 있습니다", Tone.Warn);
                await run.AskAsync([new("move", "다시 이동", true)], ct);
                return null;
            }
            asking.Cancel();
            try { await stop; } catch (OperationCanceledException) { }
            var result = await slew;
            if (result.Ok) return result;
            // 응답 없음·시간 초과는 멈춤을 뜻하지 않는다 → 멈추고 멈춘 것을 확인한 뒤에만 다시 시도 (2026-10-14 자체 리뷰: 움직이는 적도의에 새 이동을 보낼 수 있었다)
            run.Guide("이동을 마치지 못했습니다", "적도의가 응답하지 않습니다. 적도의 전원과 케이블을 확인해 주세요.");
            run.Status("적도의를 멈추고 확인하는 중입니다", Tone.Busy);
            await using (await ctx.Mount.AcquireAsync(ct))
                stopped = await TryStopAsync();
            while (!stopped)
            {
                run.Status("적도의가 멈췄는지 확인하지 못했습니다. 적도의를 직접 확인한 뒤 장비 상태 다시 확인을 눌러 주세요", Tone.Fail);
                await run.AskAsync([new("recheck-stop", "장비 상태 다시 확인", true)], ct);
                await using (await ctx.Mount.AcquireAsync(ct))
                    stopped = await TryStopAsync();
            }
            run.Status(result.Problem ?? "이동하지 못했습니다", Tone.Fail);
            if (await run.AskAsync([new("move", "다시 시도", true), AcceptHere], ct) == "here") { _acceptHere = true; return null; }
            return null; // 바깥 반복이 고도·시각을 다시 본 뒤 옮긴다 (다시 시도를 눌렀으니 승인은 그대로)
        }
    }

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ctx, ct);
        if (s.Moving) return EndStateCheck.Fail("적도의가 아직 움직이고 있습니다");
        if (!s.Tracking) return EndStateCheck.Fail("적도의 추적이 꺼져 있습니다");
        if (ctx.Results.Get<SlewResult>() is not { AcceptedHere: true } && s.DistanceToTargetDeg > ArrivalLimitDeg) return EndStateCheck.Fail($"적도의가 목표에서 {s.DistanceToTargetDeg:F1}° 벗어나 있습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);

    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(15);

    /// <summary>정지 명령 + 확인. 예외·시간 초과는 확인 못 함(false)</summary>
    private async Task<bool> TryStopAsync()
    {
        using var cts = new CancellationTokenSource(StopWait);
        try { return await devices.StopAsync(cts.Token); }
        catch (Exception e) when (e is not OutOfMemoryException) { return false; }
    }
}
