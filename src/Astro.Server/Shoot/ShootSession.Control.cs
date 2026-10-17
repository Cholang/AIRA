using Astro.Server.Prepare.Flow;

namespace Astro.Server.Shoot;

/// <summary>촬영 — 시작·중단·끝·정지 확인 (ShootSession 나눔, 상태 필드는 ShootSession.cs)</summary>
public sealed partial class ShootSession
{
    // ── 시작 · 중단 ─────────

    /// <summary>대상 묶음이 끝난 상황(계획·결과)으로 촬영을 시작한다. 같은 계획이 찍는 중이면 그대로(화면 재접속)</summary>
    public void Start(PrepContext ctx)
    {
        var key = ctx.Plan.ConfirmedAt.ToString("O");
        lock (_gate)
        {
            if (_key == key && _mode is not ShootMode.Ended) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ctx = ctx;
            _key = key;
            _good = _excluded = _elapsed = _flipStep = 0;
            _guide.Clear(); _hfr.Clear();
            foreach (var l in ShotGrader.Letters) _tally[l] = 0;
            _lastGrade = _ended = _folder = null; _note = null; _focusPoints = null; _photoAt = null;
            _flipped = false; _stopRequested = false; _recordIndex = -1; _hfrRecent.Clear(); _gap.Reset();
            _pause = null; _pausedMinutes.Clear(); _snrSamples.Clear(); _hfdSamples.Clear(); _snrBaseline = _hfdBaseline = 0;
            _mainStarsRatio = _mainMeanRatio = null; _jumps.Clear(); _hotPixels.Clear(); _lastDegrade.Clear(); _windWarned = false;
            _guideStopped = true; _mountStill = true; _hasGuider = ctx.HasGuider; _seenSteps = 0; _notMoved.Clear();
            _ditherFails = 0; _ask = _answer = null; _unstableAccepted = false; _excludedRun = 0; _framesPausedSince = null; _lowHfdWarned = _dewWarned = DateTimeOffset.MinValue; _dew = null;
            _focusHfr = ctx.Results.Get<FocusCheckResult>() is { Refocused: true, Hfr: { } rh } ? rh : ctx.Results.Get<FocusResult>()?.Hfr ?? 2;
            // 같은 계획을 전에 찍던 기록이 있으면(AA를 다시 켜 이어서) 그 줄에 이어서 센다
            if (ctx.Results.Get<NightShootResult>()?.Targets is { } rows && rows.ToList().FindLastIndex(t => t.PlanKey == key) is >= 0 and var at)
            {
                var row = rows[at];
                _recordIndex = at;
                _good = row.Good;
                _excluded = row.Excluded;
                foreach (var (l, n) in row.Tally) _tally[l] = n;
                _folder = row.Folder;
                foreach (var (k, m) in row.PausedMinutes ?? new Dictionary<string, double>()) _pausedMinutes[k] = m;
                _notMoved.AddRange(row.NotMoved ?? []);
            }
            _mode = ShootMode.Shoot;
            Changed();
        }
        var ct = _cts.Token;
        log.LogInformation("촬영 시작: {Target}", ctx.TargetName);
        // 이전 촬영(다른 계획)이 완전히 끝난 뒤에 시작 — 장비 명령이 겹치지 않게
        var previous = _run;
        _run = Task.Run(async () =>
        {
            try { await previous; } catch (Exception) { /* 이전 실행의 오류는 그쪽에서 기록 */ }
            // 찍는 동안 PC가 절전에 들지 않게 (끝·중단·오류 모두 풀림)
            using (Engine.KeepAwake.Hold($"{Core.Product.Name} 촬영 중"))
                await RunAsync(ctx, ct);
        }, CancellationToken.None);
    }

    /// <summary>
    /// N.I.N.A.가 예기치 않게 꺼짐 (CX-APP-R4): 지금 사진을 기다리지 않고 촬영을 멈추고 가이딩 정지를 확인한다(PHD2 직접).
    /// 화면은 N.I.N.A.를 다시 켠 뒤 대상 단계(이동부터)로 돌아간다
    /// </summary>
    public async Task AbortForRestartAsync()
    {
        CancellationTokenSource? cts;
        Task run;
        lock (_gate)
        {
            if (_ctx is null || _mode is ShootMode.Ended or ShootMode.Idle) return;
            cts = _cts;
            run = _run;
        }
        cts?.Cancel();
        try { await run.WaitAsync(TimeSpan.FromSeconds(20)); } catch (Exception) { /* 취소·시간 초과 */ }
        Note("N.I.N.A.가 꺼져 촬영을 멈췄어요", Tone.Fail, 600);
        await EndAsync("nina", CancellationToken.None);
    }

    /// <summary>
    /// 아이라 종료 (2026-10-11 Codex CX-30-01): 연속 촬영은 N.I.N.A.가 아니라 아이라가 한 장씩 시킨다 — 아이라가 꺼지면 다음 사진·자오선 반전·끝 조건을 볼
    /// 주체가 사라져 추적만 남는다(자오선을 넘어 경통이 삼각대에 닿을 수 있음). 그래서 촬영을 끝낸 뒤 종료한다.
    /// 지금 사진은 중단하지 않고 끝까지 찍어 받은 뒤 멈춘다 ("촬영 중단"과 같은 길) — 2026-10-11 실기, Fujifilm X-T5: N.I.N.A. 후지 플러그인의 노출 중단은
    /// 벌브 노출만 멈춘다. 일반 노출(셔터 속도 지정)은 "Stop exposure (bulb safety) failed"로 실패하는데 N.I.N.A.는 "중단됨"으로 답해, 카메라는 끝까지 찍고
    /// 사진을 PC로 보내려고 기다리다 전원도 꺼지지 않았다("이미지를 PC로 전송"). 끝나면 가이딩 정지 확인 → 뒤의 종료 절차가 추적을 끄고 장비를 정리한다.
    /// 다 확인했으면 true. 노출 시간 + 3분 안에 끝나지 않으면 false
    /// </summary>
    public async Task<bool> StopForQuitAsync()
    {
        Task run;
        int exposure;
        lock (_gate)
        {
            if (_ctx is null || _mode is ShootMode.Ended or ShootMode.Idle) return true;
            run = _run;
            exposure = _ctx.ExposureSeconds;
        }
        log.LogInformation("종료: 지금 사진이 끝나면 촬영을 멈춤");
        Stop(); // 지금 사진까지 찍고 끝낸다 (끝낼 때 가이드 정지 확인)
        Note($"지금 사진이 끝나면 촬영을 멈추고 {Astro.Core.Product.Reul} 종료해요", Tone.Busy, 600);
        try { await run.WaitAsync(TimeSpan.FromSeconds(exposure + 180)); }
        catch (TimeoutException)
        {
            log.LogWarning("종료: 지금 사진이 {Seconds}초 안에 끝나지 않음", exposure + 180);
            return false;
        }
        catch (Exception) { /* 촬영 쪽 오류는 그쪽에서 기록·마무리 */ }
        lock (_gate) return _mode is ShootMode.Ended && _guideStopped && _mountStill;
    }

    /// <summary>
    /// [임시] 건너뛰기 (2026-10-14 — 장비 없이 모든 단계를 넘겨 볼 때): 지금 사진을 기다리지 않고 촬영을 끝낸다. 성공 = 계획 장수를 다 찍은 것으로, 실패 = 일부만 찍고 오류로 끝난 것으로.
    /// 끝낼 때는 평소처럼 가이딩 정지·추적 끄기를 시도하고, 정지를 확인하지 못했으면 그 이유를 돌려준다(멈춘 척하지 않는다). 지울 때: 이 메서드 · AppServer의 /shoot/temp-skip · 웹 ShootScreen
    /// </summary>
    public async Task<string?> TempSkipAsync(bool success)
    {
        CancellationTokenSource? cts;
        Task run;
        bool ended;
        PrepContext ctx;
        lock (_gate)
        {
            if (_ctx is null || _mode is ShootMode.Idle) return "찍고 있지 않습니다.";
            ctx = _ctx;
            cts = _cts;
            run = _run;
            ended = _mode is ShootMode.Ended;
        }
        if (!ended)
        {
            cts?.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(20)); } catch (Exception) { /* 취소·시간 초과 */ }
        }
        var planned = Math.Max(1, ctx.Plan.EstimatedFrames);
        Set(() =>
        {
            _good = success ? planned : Math.Max(1, planned / 3);
            _excluded = success ? 0 : 2;
            foreach (var l in ShotGrader.Letters) _tally[l] = 0;
            _tally["A"] = _good;
            _tally["F"] = _excluded;
            _note = success ? new StatusLine("[임시] 다 찍은 것으로 넘겼어요", Tone.Ok) : new StatusLine("[임시] 오류로 끝난 것으로 넘겼어요", Tone.Fail);
            _noteUntil = DateTimeOffset.MaxValue;
        });
        if (ended) Record(ctx);
        else await EndAsync(success ? "frames" : "error", CancellationToken.None);
        log.LogInformation("[임시] 촬영 건너뛰기({Outcome})", success ? "성공" : "실패");
        return BlocksNext();
    }

    /// <summary>"촬영 중단": 지금 사진은 끝까지 찍고 멈춘다. 찍고 있지 않으면 이유</summary>
    public string? Stop()
    {
        lock (_gate)
        {
            if (_ctx is null || _mode is ShootMode.Ended or ShootMode.Idle) return "찍고 있지 않습니다.";
            _stopRequested = true;
            _wake?.Cancel();
            _note = new StatusLine("지금 사진까지 찍고 멈춥니다", Tone.Busy);
            _noteUntil = DateTimeOffset.MaxValue;
            Changed();
        }
        return null;
    }

    /// <summary>화면의 질문에 답한다 (unstable: shoot = 그대로 찍기 · wait = 더 기다리기). 묻고 있지 않으면 이유</summary>
    public string? Answer(string choice)
    {
        lock (_gate)
        {
            if (_ask is null) return "지금은 묻고 있는 것이 없습니다.";
            if (choice is not ("shoot" or "wait")) return "알 수 없는 답입니다.";
            _answer = choice;
            _wake?.Cancel();
            Changed();
        }
        return null;
    }

    private string? TakeAnswer()
    {
        lock (_gate)
        {
            var a = _answer;
            _answer = null;
            return a;
        }
    }

    /// <summary>
    /// 끝: 가이딩을 멈추고(마무리·다른 대상 모두 필요) 추적도 끈 뒤 끝 이유를 남긴다. 추적을 끄는 까닭: 저절로 끝나면(장수·고도·새벽·오류) 사람이 없을 수 있어
    /// 아침까지 RA축이 돌아 한계·케이블 쪽으로 간다 (2026-10-14 자체 리뷰 — 극축 정렬 중 종료 사고와 같은 상황). 마무리·다른 대상은 추적이 필요 없다
    /// </summary>
    private async Task EndAsync(string reason, CancellationToken ct)
    {
        Set(() => { _mode = ShootMode.Finishing; _note = new StatusLine("가이드를 멈추고 확인하는 중입니다", Tone.Busy); _noteUntil = DateTimeOffset.MaxValue; });
        var stopped = !_hasGuider || await TryStopGuidingAsync(ct);
        bool trackingOff;
        try { trackingOff = await devices.TrackingOffAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "촬영 끝 추적 끄기 실패"); trackingOff = false; }
        if (!trackingOff) log.LogWarning("촬영 끝: 추적을 끄지 못함");
        Set(() =>
        {
            _mode = ShootMode.Ended;
            _ended = reason;
            _guideStopped = stopped;
            // 끝 이유 알림(실패)이 있으면 그대로, 없으면 가이딩 정지 결과
            if (!_mountStill) _note = new StatusLine("적도의가 멈췄는지 확인하지 못했어요. 적도의를 확인한 뒤 \"다시 확인\"을 눌러 주세요", Tone.Fail);
            else if (!stopped) _note = new StatusLine("가이드가 멈췄는지 확인하지 못했어요. PHD2를 확인한 뒤 \"다시 확인\"을 눌러 주세요", Tone.Fail);
            else if (!trackingOff) _note = new StatusLine("적도의 추적을 끄지 못했어요. 켜 둔 채 두면 적도의가 계속 돌아가니 N.I.N.A.에서 추적을 꺼 주세요", Tone.Fail);
            else if (_note is not { Tone: Tone.Fail }) _note = new StatusLine(_hasGuider ? "가이드를 멈췄어요" : "촬영을 마쳤어요", Tone.Ok);
            _noteUntil = DateTimeOffset.MaxValue;
        });
        if (_ctx is { } c) Record(c);
        devices.EndSession();
        log.LogInformation("촬영 끝: {Reason} · 쓸 사진 {Good} · 제외 {Excluded}", reason, _good, _excluded);
        // 장 사이 쉬는 시간: 그 밤의 중앙값을 지금 카메라에 남긴다 — 다음 계획이 그 값으로 장수·끝 시각을 센다 (모의 장비는 남기지 않음)
        if (!mode.Simulate && _gap.Median is { } gap && learned is not null)
        {
            Equipment.Camera.CameraLearned.RecordFrameOverhead(learned, gap, DateTimeOffset.Now);
            log.LogInformation("장 사이 쉬는 시간 {Gap:F1}초 ({Count}번 잼) — 카메라 {Camera}", gap, _gap.Samples.Count, Equipment.Camera.CameraLearned.CurrentCamera(learned) ?? "모름");
        }
    }

    private async Task<bool> TryStopGuidingAsync(CancellationToken ct)
    {
        try { return await devices.StopGuidingAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "가이드 정지 확인 실패"); return false; }
    }

    /// <summary>"장비 상태 다시 확인": 끝났는데 가이딩 정지를 확인하지 못했을 때 다시 멈추고 확인한다 (CX-SHOOT-01). 확인되면 null</summary>
    public async Task<string?> RecheckStopAsync()
    {
        bool guide, mount;
        lock (_gate) { if (_mode != ShootMode.Ended) return null; guide = _guideStopped; mount = _mountStill; }
        if (guide && mount) return null;
        if (!mount)
        {
            bool still;
            try { still = await devices.ConfirmMountStillAsync(CancellationToken.None); }
            catch (Exception e) { log.LogWarning(e, "적도의 정지 확인 실패"); still = false; }
            if (!still) return "아직 적도의가 멈췄는지 확인하지 못했습니다. 적도의를 확인해 주세요.";
            Set(() => _mountStill = true);
        }
        if (!guide && !await TryStopGuidingAsync(CancellationToken.None)) return "아직 가이드가 멈췄는지 확인하지 못했습니다. PHD2를 확인해 주세요.";
        Set(() => { _guideStopped = true; _note = new StatusLine("장비가 멈춘 것을 확인했어요", Tone.Ok); _noteUntil = DateTimeOffset.MaxValue; });
        return null;
    }

    /// <summary>촬영이 끝났는데 적도의가 멈춘 것을 확인하지 못함 (반전 실패 뒤 등) — 종료 절차가 본다</summary>
    public bool MountUnconfirmed { get { lock (_gate) return _ctx is not null && _mode == ShootMode.Ended && !_mountStill; } }

    /// <summary>마무리·다른 대상으로 가도 되는가 (찍는 중이거나 가이딩 정지를 확인하지 못했으면 이유)</summary>
    public string? BlocksNext()
    {
        lock (_gate)
        {
            if (_ctx is null || _mode == ShootMode.Idle) return null;
            if (_mode != ShootMode.Ended) return "아직 촬영 중입니다. \"촬영 중단\"으로 멈춘 뒤 진행해 주세요.";
            if (!_mountStill) return "적도의가 멈췄는지 확인되지 않았습니다. 촬영 화면에서 \"다시 확인\"을 눌러 주세요.";
            if (!_guideStopped) return "가이드가 멈췄는지 확인되지 않았습니다. 촬영 화면에서 \"다시 확인\"을 눌러 주세요.";
            return null;
        }
    }

    /// <summary>그날 밤 결과 기록에 이 촬영(대상 하나)을 남긴다</summary>
    private void Record(PrepContext ctx)
    {
        lock (_gate)
        {
            var mine = new TargetShots(ctx.Plan.TargetId, ctx.TargetName, _good, _excluded, ctx.ExposureSeconds, ctx.Plan.Iso, new Dictionary<string, int>(_tally), _folder,
                new Dictionary<string, double>(_pausedMinutes), [.. _notMoved], _key);
            var list = ctx.Results.Get<NightShootResult>()?.Targets.ToList() ?? [];
            // 이 촬영(계획 하나)의 줄: 처음이면 더하고, 이후엔 바꾼다
            if (_recordIndex >= 0 && _recordIndex < list.Count) list[_recordIndex] = mine;
            else { list.Add(mine); _recordIndex = list.Count - 1; }
            ctx.Results.Set(new NightShootResult(list));
        }
    }
}
