using Astro.Core;
using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.Polar;

/// <summary>① 극축 정렬 장비 (OPERATION_PROCEDURE.md ①). 실제: N.I.N.A.(추적·RA 회전) · PHD2(카메라 넘겨주기) · SharpCap(실행·스크립트·창 캡처)</summary>
public interface IPolarDevices
{
    Task<DeviceResult> SetSiderealTrackingAsync(CancellationToken ct);
    /// <summary>PHD2가 가이드 카메라를 놓게 한다 (루프 멈춤 → Stopped 기다림 → 연결 해제). 한 번 시도</summary>
    Task<DeviceResult> HandOverGuideCameraAsync(CancellationToken ct);
    /// <summary>PHD2 장비 연결 창이 열려 있는가 — 열려 있으면 PHD2가 카메라를 놓지 못하므로 넘겨받기 전에 본다 (사전 점검 F03)</summary>
    Task<bool> Phd2DialogOpenAsync(CancellationToken ct) => Task.FromResult(false);
    /// <summary>SharpCap을 카메라·스크립트와 함께 실행하고 극축 정렬을 켠다</summary>
    Task<DeviceResult> StartSharpCapAsync(CancellationToken ct);
    /// <summary>SharpCap이 이미 켜져 있나 (사용자가 켬 — 아이라 스크립트 없이 켜진 SharpCap은 조종할 수 없다)</summary>
    Task<bool> SharpCapAlreadyOpenAsync(CancellationToken ct) => Task.FromResult(false);
    /// <summary>켜져 있던 SharpCap 닫기 (사용자가 "닫고 다시 켜기"를 눌렀을 때)</summary>
    Task<DeviceResult> CloseOpenSharpCapAsync(CancellationToken ct) => Task.FromResult(DeviceResult.Success);
    /// <summary>첫 사진 → RA 회전 → 둘째 사진. 진행(별 개수·노출·회전 각도)을 알린다. 별이 모자라면 노출을 늘려 다시</summary>
    Task<PoleFindResult> FindPoleAsync(Action<PoleProgress> progress, CancellationToken ct);
    /// <summary>정렬 중 조절량을 계속 받는다 (SharpCap 스크립트 → AA)</summary>
    IAsyncEnumerable<PolarOffset> WatchOffsetsAsync(CancellationToken ct);
    /// <summary>SharpCap 닫기 → PHD2에 가이드 카메라 재연결 → N.I.N.A.–PHD2 연결 확인</summary>
    Task<DeviceResult> GiveBackGuideCameraAsync(CancellationToken ct);
    /// <summary>가이드 화면 배율(″/px). 모르면 null</summary>
    Task<double?> GuidePixelScaleAsync(CancellationToken ct);
    /// <summary>등급 말("충분해요")을 쓰는가 — 모의만. 실제 장비는 방향·픽셀 값만 (2026-10-14 사용자 결정, 옛 CX-PREP-IMPL-03)</summary>
    bool OffsetUnitVerified { get; }
    Task<bool> StopAsync(CancellationToken ct);
    /// <summary>적도의만 다시 멈추고 멈춘 것을 확인 (SharpCap·PHD2는 건드리지 않음)</summary>
    Task<bool> MountStillAsync(CancellationToken ct) => StopAsync(ct);
    /// <summary>극 찾기를 다시 하기 전: 적도의를 홈(시작 위치)으로 되돌리고 추적을 항성으로</summary>
    Task<PolarReturn> ReturnToStartAsync(CancellationToken ct) => Task.FromResult(PolarReturn.AtStart);
    Task<PolarEndState> ReadEndStateAsync(CancellationToken ct);
}

/// <summary>UserSet = 노출을 사용자가 SharpCap에서 직접 정함</summary>
public sealed record PoleProgress(string Stage, int Stars, double ExposureSeconds, double RotationDeg, bool UserSet = false);
/// <summary>StopUnconfirmed = 회전 뒤 적도의가 멈춘 것을 확인하지 못함 — 다시 돌리지 않고 확인부터. Rotated = 이번에 RA축을 돌렸다(다시 하기 전에 시작 위치로) (2026-10-14)</summary>
public sealed record PoleFindResult(bool Ok, string? Problem = null, bool StopUnconfirmed = false, bool Rotated = false);

/// <summary>시작 위치(홈)로 되돌리기 결과: 도착 · 도착은 못 했지만 멈춤 확인 · 멈춤도 확인 못 함</summary>
public enum PolarReturn { AtStart, Stopped, StopUnconfirmed }
/// <summary>조절량(px): X = 방위(+는 왼쪽으로), Y = 고도(+는 아래로)</summary>
public sealed record PolarOffset(double XPx, double YPx, DateTimeOffset At);
public sealed record PolarEndState(bool SharpCapClosed, bool GuideCameraOnPhd2, bool Phd2Looping, bool NinaGuiderConnected, bool MountMoving);

/// <summary>① 극축 정렬: 넘겨받기 → 극 찾기 → 정렬(사용자) → 돌려주기. 사용자는 나사 조절과 "정렬 완료"만</summary>
public sealed class PolarTask(IPolarDevices devices) : IPrepTask
{
    private const int HandOverTries = 3;
    /// <summary>이보다 오래 갱신되지 않은 조절량으로는 완료하지 않는다</summary>
    private static readonly TimeSpan OffsetStaleAfter = TimeSpan.FromSeconds(5);

    public string Id => "polar";
    public string Title => UiText.Get("rail.task.polar");
    public string StartLabel => UiText.Get("button.polar.start");
    public string? StartPrompt => "가이드 카메라를 SharpCap으로 넘겨 극축 정렬을 시작할까요? 극을 찾을 때 적도의가 RA축으로 크게 돌아요.";
    public IReadOnlyList<SubStep> SubSteps { get; } =
        [new("handover", UiText.Get("rail.step.polar.handover")), new("find", UiText.Get("rail.step.polar.find")), new("align", UiText.Get("rail.step.polar.align")), new("giveback", UiText.Get("rail.step.polar.giveback"))];

    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var cloud = await CloudNotice.ReadAsync(run.Context, ct);
        if (!await HandOverAsync(run, cloud, ct) || !await FindPoleAsync(run, cloud, ct)) return await SkipAsync(run, ct);
        var offset = await AlignAsync(run, ct);
        await GiveBackAsync(run, ct);
        var scale = await devices.GuidePixelScaleAsync(ct);
        var (arcmin, grade) = Evaluate(offset, scale, run.Context);
        var result = new PolarResult(arcmin, offset.XPx, offset.YPx, grade, run.Context.Now());
        run.Context.Memory.LastPolarAlignedAt = run.Context.Now(); // 이 뒤로는 새 캘리브레이션 (CX-PREP-CODE-06)
        var line = arcmin is { } a ? $"{grade} · {a:F1}′" : $"{Math.Abs(offset.XPx):F0}·{Math.Abs(offset.YPx):F0}px";
        // 캘리브레이션 버튼(남쪽 하늘로 이동 / 가이딩 없이 진행)을 누르기 전에 구름 예보를 다시 본다
        return new Completed(result, line, grade ?? UiText.Get("title.polar.done"),
            CloudNotice.With("가이드 카메라가 PHD2에 다시 연결되었습니다. 다음은 가이드 보정값을 만드는 캘리브레이션입니다.", await CloudNotice.ReadAsync(run.Context, ct)));
    }

    /// <summary>
    /// 극 찾기 "다시 시도" 전에 적도의를 홈(시작 위치)으로 되돌린다 (2026-10-14 사용자 결정 — 이 적도의는 홈에서 한쪽으로 약 60°까지만 돈다.
    /// 되돌리지 않으면 같은 방향으로 60°를 또 돌려 케이블·한계 센서에 걸릴 수 있다). 되돌리지 못하면 손으로 홈 근처에 두고 다시 누르게 한다.
    /// 건너뛰기를 고르면 false
    /// </summary>
    private async Task<bool> ReturnToStartAsync(ITaskRun run, CancellationToken ct)
    {
        while (true)
        {
            run.Guide(UiText.Get("title.polar.backToStart"), "다시 돌리기 전에 적도의를 홈(시작 위치)으로 되돌립니다. 케이블이 당기거나 걸리지 않는지 봐 주세요.");
            run.Status("적도의를 홈으로 되돌리는 중입니다", Tone.Busy);
            PolarReturn back;
            await using (await run.Context.Mount.AcquireAsync(ct))
                back = await devices.ReturnToStartAsync(ct);
            if (back == PolarReturn.AtStart) { run.Context.Memory.PolarNeedsReturn = false; return true; }
            var unconfirmed = back == PolarReturn.StopUnconfirmed;
            while (unconfirmed)
            {
                run.Status("홈으로 가다 멈췄는지 확인하지 못했습니다", Tone.Fail);
                await run.AskAsync("적도의를 직접 본 뒤 장비 상태를 다시 읽어 볼까요?", [new("recheck-stop", ButtonText.Recheck, true)], ct);
                await using (await run.Context.Mount.AcquireAsync(ct))
                    unconfirmed = !await devices.MountStillAsync(ct);
            }
            // 멈춘 것은 확인됨: 자동 홈을 다시 시도하거나, 손으로 홈에 옮겼으면 그대로 극 찾기로 (CX-36-09 — 실패하는 홈 명령만 되풀이하지 않게)
            run.Status("홈으로 되돌리지 못했습니다", Tone.Fail);
            var c = await run.AskAsync("적도의를 홈 근처(망원경이 북극성 쪽, 균형추가 아래)로 손으로 옮겼으면 완료를 눌러 극 찾기를 할까요?\n재시도를 누르면 적도의가 다시 홈으로 움직이고, 건너뛰기를 누르면 극축 정렬 없이 넘어가요.",
                [new("manual-home", ButtonText.Done, true), new("retry", ButtonText.Retry), new("skip", ButtonText.Skip)], ct);
            if (c == "skip") return false;
            if (c == "manual-home") { run.Context.Memory.PolarNeedsReturn = false; return true; }
        }
    }

    /// <summary>
    /// 극축 정렬 건너뛰기 (2026-10-08 사용자 결정 — 북쪽이 막혔거나 SharpCap·카메라 문제로 밤이 막히지 않게): SharpCap을 닫고 카메라를 PHD2에 돌려준 뒤 다음으로
    /// </summary>
    private async Task<TaskOutcome> SkipAsync(ITaskRun run, CancellationToken ct)
    {
        run.Guide(UiText.Get("title.polar.skipping"), "SharpCap을 닫고 가이드 카메라를 PHD2에 돌려준 뒤 다음으로 갑니다.");
        run.Live("none");
        while (true)
        {
            run.Status("가이드 카메라를 PHD2로 돌려주는 중입니다");
            if (await devices.StopAsync(ct)) break;
            // StopAsync가 false = 적도의 정지를 확인하지 못함 (카메라 돌려주기 실패는 장비 상태 확인에서 따로 보인다)
            run.Status("적도의가 멈췄는지 확인하지 못했습니다", Tone.Fail);
            await run.AskAsync("적도의를 직접 본 뒤 가이드 카메라를 PHD2로 다시 돌려줄까요?", [new("retry", ButtonText.Retry, true)], ct);
        }
        return new Completed(new PolarResult(null, 0, 0, "건너뜀", run.Context.Now(), Skipped: true), UiText.Get("rail.result.skipped"), UiText.Get("title.polar.skipped"),
            "극축이 틀어져 있으면 가이드 보정이 잦아지고 긴 촬영에서 시야가 조금 돌 수 있어요. 다음은 캘리브레이션입니다.") { AutoNext = true };
    }

    /// <summary>넘겨받기. 건너뛰기를 고르면 false</summary>
    private async Task<bool> HandOverAsync(ITaskRun run, string? cloud, CancellationToken ct)
    {
        run.SubStep(0);
        run.Guide(UiText.Get("rail.step.polar.handover"), CloudNotice.With("가이드 카메라를 PHD2에서 SharpCap으로 넘깁니다. 손대실 것은 없습니다.", cloud));
        run.Live("none");
        while (true)
        {
            run.Status("적도의 추적 속도를 항성으로 맞추는 중입니다");
            // 적도의 명령은 공통 적도의 잠금 안에서 — 정지 확인이 필요한 상태(홈 실패 등)면 잠금이 이유를 돌려주고 여기서 멈춘다 (2026-10-11 Codex CX-30-02:
            // 극축 정렬만 잠금을 거치지 않아 확인 필요 상태에서도 추적을 켜고 RA축을 돌렸다)
            await using (await run.Context.Mount.AcquireAsync(ct))
                await devices.SetSiderealTrackingAsync(ct); // 실패해도 진행 (OPERATION_PROCEDURE.md ①)
            // 사용자가 켜 둔 SharpCap: 아이라는 켤 때 넘기는 스크립트로만 SharpCap을 조종하므로 닫고 다시 켠다 — 누르면 (2026-10-11 사용자 결정).
            // PHD2가 카메라를 놓기 전에 묻는다 (놓은 뒤에 막히면 가이드 카메라가 아무 데도 없음)
            if (await devices.SharpCapAlreadyOpenAsync(ct))
            {
                run.Status("SharpCap이 이미 켜져 있습니다", Tone.Warn);
                var pick = await run.AskAsync($"{Product.Neun} 극축 정렬용 설정으로 SharpCap을 켜야 조종할 수 있어요. 켜져 있는 SharpCap을 닫고 다시 켤까요?\n건너뛰기를 누르면 극축 정렬 없이 넘어가요.",
                    [new("restart-sharpcap", UiText.Get("button.sharpcap.restart"), true), new("skip", ButtonText.Skip)], ct);
                if (pick == "skip") return false;
                run.Status("켜져 있던 SharpCap을 닫는 중입니다");
                var closed = await devices.CloseOpenSharpCapAsync(ct);
                if (!closed.Ok)
                {
                    run.Status(closed.Problem ?? "SharpCap이 닫히지 않았습니다.", Tone.Fail);
                    if (await run.AskAsync("SharpCap을 다시 닫아 볼까요?\n건너뛰기를 누르면 극축 정렬 없이 넘어가요.", [new("retry", ButtonText.Retry, true), new("skip", ButtonText.Skip)], ct) == "skip") return false;
                }
                continue;
            }
            if (await devices.Phd2DialogOpenAsync(ct))
            {
                // 넘겨받기를 세 번 헛되이 시도하지 않고 먼저 닫게 한다 (F03)
                run.Status("PHD2의 장비 연결 창이 열려 있습니다", Tone.Warn);
                if (await run.AskAsync("이 창이 열려 있으면 PHD2가 가이드 카메라를 놓지 못해요. 창을 닫은 뒤 다시 확인할까요?\n건너뛰기를 누르면 극축 정렬 없이 넘어가요.",
                    [new("retry", ButtonText.Recheck, true), new("skip", ButtonText.Skip)], ct) == "skip") return false;
                continue;
            }
            var released = DeviceResult.Fail("PHD2가 가이드 카메라를 놓지 않았습니다.");
            for (var i = 1; i <= HandOverTries && !released.Ok; i++)
            {
                run.Status(i == 1 ? "PHD2가 가이드 카메라를 놓게 하는 중입니다" : $"PHD2가 가이드 카메라를 놓게 하는 중입니다 ({i}/{HandOverTries})");
                released = await devices.HandOverGuideCameraAsync(ct);
            }
            if (released.Ok)
            {
                run.Status("SharpCap을 실행하는 중입니다");
                var started = await devices.StartSharpCapAsync(ct);
                if (started.Ok) return true;
                run.Status($"SharpCap을 실행하지 못했습니다. {started.Problem}".Trim(), Tone.Fail);
            }
            else run.Status($"{released.Problem} PHD2의 장비 연결 창에서 가이드 카메라 연결을 해제한 뒤 다시 시도해 주세요.", Tone.Fail);
            if (await run.AskAsync("가이드 카메라를 SharpCap으로 다시 넘겨 볼까요?\n건너뛰기를 누르면 극축 정렬 없이 넘어가요.", [new("retry", ButtonText.Retry, true), new("skip", ButtonText.Skip)], ct) == "skip") return false;
        }
    }

    /// <summary>극 찾기. 건너뛰기를 고르면 false</summary>
    private async Task<bool> FindPoleAsync(ITaskRun run, string? cloud, CancellationToken ct)
    {
        run.SubStep(1);
        // 케이블 확인은 출발 전 점검에서 빼고 크게 돌리기 직전에 (2026-10-12 사용자 결정)
        run.Guide(UiText.Get("rail.step.polar.find"), CloudNotice.With("적도의를 RA축으로 크게 돌려 회전 전후의 별 위치를 비교합니다. 케이블이 당기거나 걸리지 않는지 봐 주세요.", cloud));
        run.Live("sharpcap", "/api/prepare/live/sharpcap");
        var memory = run.Context.Memory;
        while (true)
        {
            // 앞서 RA축을 돌렸으면(이번 실행이든, 중단하고 다시 시작한 앞 실행이든) 다시 하기 전에 시작 위치로 (CX-36-01)
            if (memory.PolarNeedsReturn && !await ReturnToStartAsync(run, ct)) return false;
            // 돌리기 전에 켠다 — 도중에 취소돼도 다음 시작이 되돌리기부터
            memory.PolarNeedsReturn = true;
            // 극 찾기는 RA축을 돌린다 — 끝나고 멈춘 것을 확인할 때까지 적도의 잠금을 쥔다 (CX-30-02)
            PoleFindResult found;
            await using (await run.Context.Mount.AcquireAsync(ct))
            found = await devices.FindPoleAsync(p =>
            {
                run.Status(p.Stage switch
                {
                    "rotate" => $"적경축을 돌리는 중입니다 ({p.RotationDeg:F0}°)",
                    // 별 개수는 모의만 (SharpCap은 알려 주지 않음)
                    _ => (p.Stars > 0 ? $"별을 찾는 중입니다 · 별 {p.Stars}개" : "별을 찾는 중입니다") + $" · 노출 {p.ExposureSeconds:0.##}초 ({(p.UserSet ? "직접 정함" : $"{Product.Ga} 정함")})",
                });
            }, ct);
            if (found.Ok) return true;
            if (!found.Rotated) memory.PolarNeedsReturn = false; // 돌리기 전에 실패(첫 사진 등) — 되돌릴 것 없음
            // 멈춘 것을 확인하지 못했으면 다시 돌리지 않는다: 확인될 때까지 "장비 상태 다시 확인"만
            var unconfirmed = found.StopUnconfirmed;
            while (unconfirmed)
            {
                run.Status(found.Problem ?? "적도의가 멈췄는지 확인하지 못했습니다. 적도의 상태를 직접 확인해 주세요.", Tone.Fail);
                await run.AskAsync("적도의를 직접 본 뒤 장비 상태를 다시 읽어 볼까요?", [new("recheck-stop", ButtonText.Recheck, true)], ct);
                await using (await run.Context.Mount.AcquireAsync(ct))
                    unconfirmed = !await devices.MountStillAsync(ct);
            }
            if (found.StopUnconfirmed) found = found with { Problem = "적도의가 멈춘 것을 확인했습니다. 다시 시도하거나 극축 정렬을 건너뛸 수 있습니다." };
            run.Status(found.Problem ?? "위치를 찾지 못했습니다. 망원경이 북극에서 멀리 향해 있을 수 있습니다. 삼각대 방향과 고도 나사로 망원경을 북극 쪽으로 조금 더 가깝게 향하게 한 뒤 다시 시도해 주세요.", Tone.Fail);
            if (await run.AskAsync("극 찾기를 다시 할까요? 적도의가 RA축으로 다시 돌아요.\n건너뛰기를 누르면 극축 정렬 없이 넘어가요.", [new("retry", ButtonText.Retry, true), new("skip", ButtonText.Skip)], ct) == "skip") return false;
        }
    }

    /// <summary>
    /// 사용자가 "정렬 완료"를 누를 때까지 조절량을 보여 준다. 마지막 값을 돌려준다.
    /// 조절량을 아직 못 받았거나 한동안 갱신되지 않았으면 완료하지 않고 기다리게 한다 — 없는 값을 0·좋은 등급으로 저장하지 않는다 (CX-PREP-CODE-04)
    /// </summary>
    private async Task<PolarOffset> AlignAsync(ITaskRun run, CancellationToken ct)
    {
        run.SubStep(2);
        run.Guide(UiText.Get("rail.step.polar.align"), "화살표 방향으로 나사를 조절해 오차를 줄여 주세요.");
        run.Status(null);
        var scale = await devices.GuidePixelScaleAsync(ct);
        PolarOffset? last = null;
        var arrived = new SemaphoreSlim(0);
        using var watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = Task.Run(async () =>
        {
            await foreach (var o in devices.WatchOffsetsAsync(watch.Token))
            {
                last = o;
                ShowOffset(run, o, scale);
                arrived.Release();
            }
        }, CancellationToken.None);
        try
        {
            while (true)
            {
                await run.AskAsync("원하는 만큼 맞췄으면 정렬을 마칠까요? 누르면 SharpCap을 닫고 가이드 카메라를 PHD2로 돌려줘요.", [new("align-done", ButtonText.Done, true)], ct);
                var got = last;
                if (got is null)
                    run.Status("아직 조절량을 받지 못했습니다. SharpCap 화면에 조절 화살표가 나오면 다시 눌러 주세요", Tone.Warn);
                else if (run.Context.Now() - got.At > OffsetStaleAfter)
                    run.Status($"조절량이 {(run.Context.Now() - got.At).TotalSeconds:F0}초째 갱신되지 않습니다. SharpCap 화면을 확인한 뒤 다시 눌러 주세요", Tone.Warn);
                else
                {
                    run.Status(null);
                    return got;
                }
                // 새 값이 오거나 잠시 지나면 다시 묻는다
                await arrived.WaitAsync(TimeSpan.FromSeconds(1), ct);
            }
        }
        finally
        {
            watch.Cancel();
            try { await reader; } catch (OperationCanceledException) { }
        }
    }

    private void ShowOffset(ITaskRun run, PolarOffset o, double? scale)
    {
        var dir = $"{(o.XPx > 0 ? "←" : "→")} {(o.YPx > 0 ? "↓" : "↑")}";
        var values = new Dictionary<string, double> { ["xPx"] = o.XPx, ["yPx"] = o.YPx };
        var (arcmin, grade) = Evaluate(o, scale, run.Context);
        if (arcmin is { } a && grade is not null)
        {
            values["errorArcmin"] = a;
            values["xArcmin"] = Math.Abs(o.XPx) * scale!.Value / 60;
            values["yArcmin"] = Math.Abs(o.YPx) * scale.Value / 60;
            run.Readout("polar-offset", grade, $"극축 오차 {a:F1}′", PolarRules.Tone(a, PolarRules.ToleranceArcmin(run.Context)), values, live: true, observedAt: o.At);
        }
        else
            run.Readout("polar-offset", $"{dir} {Math.Abs(o.XPx):F0}·{Math.Abs(o.YPx):F0}px", "줄어들고 있는지 보세요 (환산 확인 전 — 방향과 픽셀만)", Tone.Busy, values, live: true, unverified: true, observedAt: o.At);
    }

    /// <summary>환산을 확인했으면 각도 오차와 등급, 아니면 (null, null) — CX-PREP-IMPL-03</summary>
    private (double? Arcmin, string? Grade) Evaluate(PolarOffset o, double? scale, PrepContext ctx)
    {
        if (!devices.OffsetUnitVerified || scale is not > 0) return (null, null);
        var arcmin = Math.Sqrt(o.XPx * o.XPx + o.YPx * o.YPx) * scale.Value / 60;
        return (arcmin, PolarRules.Grade(arcmin, PolarRules.ToleranceArcmin(ctx)));
    }

    private async Task GiveBackAsync(ITaskRun run, CancellationToken ct)
    {
        run.SubStep(3);
        run.Guide(UiText.Get("rail.step.polar.giveback"), "SharpCap을 닫고 가이드 카메라를 PHD2에 다시 연결합니다.");
        run.Live("none");
        while (true)
        {
            run.Status("가이드 카메라를 PHD2로 돌려주는 중입니다");
            var back = await devices.GiveBackGuideCameraAsync(ct);
            if (back.Ok) { run.Status(null); return; }
            run.Status($"가이드 카메라를 다시 연결하지 못했습니다. {back.Problem}".Trim(), Tone.Fail);
            await run.AskAsync("가이드 카메라를 PHD2에 다시 연결해 볼까요?", [new("retry", ButtonText.Retry, true)], ct);
        }
    }

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (!s.SharpCapClosed) return EndStateCheck.Fail("SharpCap이 아직 열려 있습니다");
        if (!s.GuideCameraOnPhd2) return EndStateCheck.Fail("가이드 카메라가 PHD2에 연결되어 있지 않습니다");
        if (s.Phd2Looping) return EndStateCheck.Fail("PHD2가 아직 사진을 찍고 있습니다");
        if (!s.NinaGuiderConnected) return EndStateCheck.Fail("N.I.N.A.가 PHD2에 연결되어 있지 않습니다");
        if (s.MountMoving) return EndStateCheck.Fail("적도의가 아직 움직이고 있습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}

/// <summary>극축 정렬 판정 (순수 함수). 허용 오차는 계획에서 — 계산 근거: 주 카메라 화각이 넓을수록 너그럽게</summary>
public static class PolarRules
{
    /// <summary>
    /// 이 계획에서 "충분해요"의 기준(′). 근거(임시): 한 장 노출 동안 극축 오차로 생기는 별 흐름이 주 카메라 한 픽셀을 넘지 않게 —
    /// 최악 흐름 ≈ 오차(rad) × 노출 동안 도는 각도(rad) → 오차′ ≈ 픽셀″ / (노출s × 15″/s × π/180) / 60. 너무 작거나 크지 않게 2~10′로 자른다.
    /// 맑은 날 실기로 다시 정한다.
    /// </summary>
    public static double ToleranceArcmin(PrepContext ctx)
    {
        var turnRad = ctx.ExposureSeconds * 15.0 / 3600 * Math.PI / 180;
        var arcmin = ctx.MainPixelScaleArcsec / Math.Max(turnRad, 1e-6) / 60;
        return Math.Clamp(arcmin, 2, 10);
    }

    public static string Grade(double arcmin, double tolerance) =>
        arcmin < 1 ? "완벽해요" : arcmin <= tolerance ? "충분해요" : arcmin <= tolerance * 3 ? "아쉬워요" : "멀어요";

    public static Tone Tone(double arcmin, double tolerance) =>
        arcmin <= tolerance ? Flow.Tone.Ok : arcmin <= tolerance * 3 ? Flow.Tone.Warn : Flow.Tone.Fail;
}
