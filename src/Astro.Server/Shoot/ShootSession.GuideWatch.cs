using Astro.Server.Prepare.Flow;

namespace Astro.Server.Shoot;

/// <summary>촬영 — 이슬·가이드 불안정·가이드 별 잃음 (ShootSession 나눔, 상태 필드는 ShootSession.cs)</summary>
public sealed partial class ShootSession
{
    /// <summary>
    /// 이슬 여유·열선 상태를 화면에 (한 장마다). 열선은 Empire 자동이 맡으므로 AA는 이상할 때만 알린다(10분에 한 번):
    /// 여유가 2°C 아래인데 열선이 꺼져 있음 · 여유가 3°C 아래인데 자동 제어가 아님
    /// </summary>
    private async Task CheckDewAsync(CancellationToken ct)
    {
        DewStatus? dew;
        try { dew = await devices.DewAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "이슬 상태를 읽지 못함"); return; }
        Set(() => _dew = dew);
        if (dew is not { MarginC: { } m } || DateTimeOffset.Now - _dewWarned < TimeSpan.FromMinutes(10)) return;
        string? warn = null;
        if (m < GuideWatch.DewMarginC && dew.HeaterPower is 0) warn = $"이슬 여유가 {m:F1}°C인데 열선이 꺼져 있어요 · 열선 연결과 Empire 설정을 확인해 주세요";
        else if (m < 3 && dew.HeaterPower is not null && !dew.HeaterAuto) warn = $"이슬 여유가 {m:F1}°C예요 · 열선이 Empire 자동 모드가 아니에요";
        if (warn is null) return;
        _dewWarned = DateTimeOffset.Now;
        Note(warn, Tone.Warn, 20);
    }

    /// <summary>
    /// 디더링 안정화가 연속으로 실패: 촬영을 멈추고 가이드 오차가 기준 안에 1분 머물면 자동으로 이어서 찍는다.
    /// 15분이 지나면 "그대로 찍을까요?"(기준이 너무 엄격해 밤새 멈추지 않게). 그 사이 별을 잃으면 원인별 대응으로. 촬영을 이어 가면 true
    /// </summary>
    private async Task<bool> WaitUnstableAsync(PrepContext ctx, CancellationToken ct)
    {
        Pause("unstable", "가이드가 불안정해 촬영을 멈췄어요 · 안정되면 이어서 찍어요");
        var since = DateTimeOffset.Now;
        var hold = mode.Simulate ? TimeSpan.FromMilliseconds(500) : UnstableHold;
        var askAfter = mode.Simulate ? TimeSpan.FromSeconds(2) : UnstableAsk;
        while (true)
        {
            // 끝 조건(중단·계획 장수·고도·새벽)이나 반전 시각이 되면 기다림을 그만두고 바깥 고리가 처리 (CX-NIGHT-03)
            if (_stopRequested || EndReason(ctx) is not null || !_flipped && devices.HourAngleDeg(ctx) > FlipAfterDeg) { Set(() => _ask = null); Resume(null); return true; }
            switch (TakeAnswer())
            {
                case "shoot":
                    _unstableAccepted = true; // 이 대상에서는 다시 멈추지 않는다 (흐른 사진은 등급으로)
                    _ditherFails = 0;
                    Set(() => _ask = null);
                    Resume("가이드가 불안정한 채로 이어서 찍어요 · 흐른 사진은 등급으로 걸러요");
                    return true;
                case "wait":
                    since = DateTimeOffset.Now;
                    Set(() => _ask = null);
                    break;
            }
            var lost = await CheckGuideAsync(ct);
            if (lost.IsLost())
            {
                _ditherFails = 0;
                Set(() => _ask = null);
                Resume(null);
                return await HandleLossAsync(ctx, lost, ct);
            }
            bool settled;
            using (var wake = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                lock (_gate) _wake = wake;
                try { settled = await devices.WaitSettledAsync(hold, hold + (mode.Simulate ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(30)), wake.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { settled = false; } // 중단·답 — 위에서 다시 본다
                finally { lock (_gate) _wake = null; }
            }
            if (settled)
            {
                _ditherFails = 0;
                Set(() => _ask = null);
                Resume("가이드가 안정됐어요 · 이어서 찍어요");
                return true;
            }
            var waited = DateTimeOffset.Now - _pausedSince;
            Set(() =>
            {
                if (_ask is null && DateTimeOffset.Now - since > askAfter) _ask = "unstable";
                _note = new StatusLine($"멈춤 · 가이드가 안정되기를 기다리는 중 · {(int)waited.TotalMinutes}분째", Tone.Warn);
                _noteUntil = DateTimeOffset.MaxValue;
            });
        }
    }

    // ── 가이드 별 지켜보기 (docs/implementation/SHOOT.md "B. 가이딩 중 가이드 별을 잃었을 때") ─────────

    private int PollMs => mode.Simulate ? 300 : 3000;

    /// <summary>장비 신호를 읽어 원인을 가린다. 가이딩이 안정된 동안의 SNR·HFD로 기준을 잡고, 약해지면(이슬·가이드 초점·옅은 구름) 가이드 노출을 올리고 알린다</summary>
    private async Task<GuideLoss> CheckGuideAsync(CancellationToken ct)
    {
        // 가이더 없는 구성: PHD2를 부르지 않고 적도의 추적만 (CX-SHOOT-07, CX-APP-R3)
        if (!_hasGuider)
        {
            try { return await devices.MountTrackingAsync(ct) ? GuideLoss.None : GuideLoss.MountStopped; }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "적도의 상태를 읽지 못함"); return GuideLoss.None; }
        }
        GuideRaw raw;
        try { raw = await devices.GuideRawAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "가이드 신호를 읽지 못함"); return GuideLoss.None; }
        // 새로 받은 걸음만 기준 표본에 (같은 값을 매번 다시 넣지 않게)
        var fresh = (int)Math.Clamp(raw.Steps - _seenSteps, 0, raw.Snr.Count);
        _seenSteps = Math.Max(_seenSteps, raw.Steps);
        if (raw.Guiding && fresh > 0)
        {
            // 처음 안정된 가이딩 20걸음의 중앙값이 그날 기준
            if (_snrBaseline <= 0) { _snrSamples.AddRange(raw.Snr.TakeLast(fresh)); if (_snrSamples.Count >= 20) _snrBaseline = _snrSamples.Order().ElementAt(_snrSamples.Count / 2); }
            if (_hfdBaseline <= 0 && raw.Hfd.Count > 0) { _hfdSamples.AddRange(raw.Hfd.TakeLast(Math.Min(fresh, raw.Hfd.Count))); if (_hfdSamples.Count >= 20) _hfdBaseline = _hfdSamples.Order().ElementAt(_hfdSamples.Count / 2); }
        }
        // 별을 자주 놓치는데 그 대부분이 "HFD가 낮음"이면 핫픽셀·너무 작은 별 (10분에 한 번 알림)
        if (raw.LowHfdRecent >= 10 && DateTimeOffset.Now - _lowHfdWarned > TimeSpan.FromMinutes(10))
        {
            _lowHfdWarned = DateTimeOffset.Now;
            Note("가이드 별을 자주 놓쳐요 (별이 너무 작거나 핫픽셀) · PHD2의 다크 라이브러리·별 고르기를 확인해 주세요", Tone.Warn, 15);
        }
        var loss = GuideWatch.Classify(new GuideSignals(raw, _mainStarsRatio, _mainMeanRatio, _snrBaseline, _hfdBaseline));
        if (loss is GuideLoss.Dew or GuideLoss.GuideFocus or GuideLoss.Faint or GuideLoss.Cloud && raw.Guiding) await DegradeAsync(loss, ct);
        else if (raw.Guiding && _snrBaseline > 0 && raw.Snr.Count >= GuideWatch.Recent && raw.Snr.TakeLast(GuideWatch.Recent).Average() > _snrBaseline * 1.5)
            await StepGuideExposureAsync(-1, ct); // 다시 밝아지면 짧게 (하모닉 적도의는 짧은 노출이 좋음)
        // 적도의 멈춤·장비 끊김은 PHD2가 아직 Guiding이어도 먼저 (CX-SHOOT-06)
        if (loss is GuideLoss.MountStopped or GuideLoss.Disconnected) return loss;
        return raw.Guiding ? GuideLoss.None : loss;
    }

    /// <summary>가이딩은 되지만 별이 약해짐: 가이드 노출 한 단계 올림 + 원인별 알림 (같은 원인은 5분에 한 번)</summary>
    private async Task DegradeAsync(GuideLoss loss, CancellationToken ct)
    {
        if (_lastDegrade.TryGetValue(loss, out var at) && DateTimeOffset.Now - at < TimeSpan.FromMinutes(5)) return;
        _lastDegrade[loss] = DateTimeOffset.Now;
        var raised = await StepGuideExposureAsync(+1, ct);
        var exp = devices.GuideExposureMs / 1000.0;
        switch (loss)
        {
            case GuideLoss.Dew:
                var heater = await devices.DewHeaterBoostAsync(ct);
                Note($"가이드 망원경에 이슬이 맺히는 것 같아요 · {(heater ? "열선을 올렸어요" : "렌즈를 확인해 주세요")}{(raised ? $" · 가이드 노출 {exp:0.#}초" : "")}", Tone.Warn, 20);
                break;
            case GuideLoss.GuideFocus:
                Note($"가이드 별이 커지고 있어요 · 가이드 망원경 초점을 확인해 주세요{(raised ? $" · 가이드 노출 {exp:0.#}초" : "")}", Tone.Warn, 20);
                break;
            case GuideLoss.Cloud:
                Note($"옅은 구름이 지나가요 · 가이드 노출을 {exp:0.#}초로 늘렸어요", Tone.Warn, 12);
                break;
            default:
                if (raised) Note($"가이드 별이 약해져 가이드 노출을 {exp:0.#}초로 늘렸어요", Tone.Warn, 12);
                break;
        }
    }

    /// <summary>가이드 노출을 한 단계 위(+1)·아래(−1)로 (PHD2 목록 값만). 바꿨으면 true</summary>
    private async Task<bool> StepGuideExposureAsync(int dir, CancellationToken ct)
    {
        var now = devices.GuideExposureMs;
        var i = Array.FindIndex(GuideExposuresMs, v => v >= now);
        if (i < 0) i = GuideExposuresMs.Length - 1;
        var j = Math.Clamp(i + dir, 0, GuideExposuresMs.Length - 1);
        if (GuideExposuresMs[j] == now) return false;
        return await devices.SetGuideExposureAsync(GuideExposuresMs[j], ct);
    }

    /// <summary>한 장 찍는 동안 가이드 별을 지켜본다. 적도의 멈춤·끊김·크게 튐이면 바로, 잃은 시간이 노출의 20%(최소 20초)를 넘으면 이 장을 멈추고 버린다</summary>
    private async Task<(FrameShot Shot, GuideLoss Lost)> ExposeWatchedAsync(PrepContext ctx, CancellationToken ct)
    {
        var guidePoll = 0;
        var expose = devices.ExposeAsync(ctx.ExposureSeconds, ctx.Plan.Iso, s =>
        {
            Set(() => _elapsed = s);
            if (mode.Simulate || ++guidePoll % 5 == 0) _ = PollGuideAsync(ct);
        }, ct);
        var lost = GuideLoss.None;
        DateTimeOffset? lostAt = null;
        var limit = TimeSpan.FromSeconds(Math.Max(20, ctx.ExposureSeconds * 0.2));
        while (!expose.IsCompleted)
        {
            await Task.WhenAny(expose, Task.Delay(PollMs, ct));
            if (expose.IsCompleted) break;
            var l = await CheckGuideAsync(ct);
            if (!l.IsLost()) { lostAt = null; continue; }
            lostAt ??= DateTimeOffset.Now;
            if (l is GuideLoss.MountStopped or GuideLoss.Disconnected or GuideLoss.Jump || DateTimeOffset.Now - lostAt > limit)
            {
                // 노출을 중단하지 않고 끝까지 찍은 뒤 이 장을 버린다 (2026-10-11 실기 — Fujifilm X-T5: N.I.N.A. 후지 플러그인의 노출 중단은 벌브 노출만 멈춘다.
                // 셔터 속도를 정한 일반 노출(예: 30초)에는 "Stop exposure (bulb safety) failed"로 실패하고, N.I.N.A.는 "중단됨"으로 답하며 기다리기만 그만둔다.
                // 카메라는 끝까지 찍고 사진을 PC로 보내려고 기다려 다음 사진이 꼬이고 전원도 안 꺼졌다 — 그래서 중단하지 않는다)
                lost = l;
                break;
            }
        }
        var shot = await expose;
        return (shot, lost);
    }

    /// <summary>가이드 별을 잃음: 원인별 대응. 촬영을 이어 가면 true, 끝냈으면 false</summary>
    private async Task<bool> HandleLossAsync(PrepContext ctx, GuideLoss loss, CancellationToken ct)
    {
        switch (loss)
        {
            case GuideLoss.MountStopped:
                Note("적도의가 추적을 멈췄어요 (한계·전원 확인) · 촬영을 끝냈어요", Tone.Fail, 600);
                await EndAsync("mount", ct);
                return false;

            case GuideLoss.Disconnected:
                Pause("guider", "가이드 카메라 연결을 다시 하는 중이에요");
                for (var i = 0; i < 2; i++)
                    if (await devices.ReconnectGuiderAsync(ct) && await devices.ResumeGuidingAsync(ct)) { Resume("가이드 카메라를 다시 연결했어요 · 이어서 찍어요"); return true; }
                Resume(null);
                Note("가이드 카메라(PHD2) 연결이 끊겼어요 · 연결을 확인해 주세요", Tone.Fail, 600);
                await EndAsync("guider", ct);
                return false;

            case GuideLoss.Jump:
                // 바람·케이블: 바로 별을 다시 고름. 10분에 3번이면 알림
                _jumps.Add(DateTimeOffset.Now);
                _jumps.RemoveAll(t => DateTimeOffset.Now - t > TimeSpan.FromMinutes(10));
                Pause("jump", "별이 갑자기 움직였어요 · 별을 다시 고르는 중이에요");
                if (!await devices.ReselectStarAsync(ct) && !await RecenterSafelyAsync(ctx, ct)) return false;
                Resume(_jumps.Count >= 3 && !_windWarned ? "별이 자주 튀어요 · 바람이나 케이블을 확인해 주세요" : "가이드 별을 다시 잡았어요 · 이어서 찍어요");
                if (_jumps.Count >= 3) _windWarned = true;
                return true;

            case GuideLoss.HotPixel:
                // 핫픽셀·너무 작은 별: 기다리지 않고 바로 별을 다시 고른다. 10분에 두 번째면 가이드 노출을 한 단계 올림 (별 신호가 핫픽셀보다 커지게)
                _hotPixels.Add(DateTimeOffset.Now);
                _hotPixels.RemoveAll(t => DateTimeOffset.Now - t > TimeSpan.FromMinutes(10));
                Pause("hotpixel", "가이드 별 대신 핫픽셀이나 너무 작은 별을 잡은 것 같아요 · 별을 다시 고르는 중이에요");
                var raised = _hotPixels.Count >= 2 && await StepGuideExposureAsync(+1, ct);
                if (!await devices.ReselectStarAsync(ct)) { Resume(null); return await WaitForStarAsync(ctx, GuideLoss.Cloud, ct); }
                Resume(raised ? $"다른 별을 골랐어요 · 가이드 노출을 {devices.GuideExposureMs / 1000.0:0.#}초로 늘렸어요" : "다른 별을 골랐어요 · 이어서 찍어요");
                return true;

            case GuideLoss.Light:
                return await WaitForStarAsync(ctx, GuideLoss.Light, ct);

            default:
                return await WaitForStarAsync(ctx, GuideLoss.Cloud, ct);
        }
    }

    /// <summary>
    /// 별이 돌아오기를 기다림: 빛은 같은 자리에서 2분(별을 새로 고르지 않음, 넘으면 구름처럼), 구름은 30초마다 가이딩을 다시 켜 보며 30분.
    /// 돌아오면 10분 넘게 멈췄던 경우 다시 가운데로. 상한을 넘으면 촬영을 멈추고 알린다
    /// </summary>
    private async Task<bool> WaitForStarAsync(PrepContext ctx, GuideLoss cause, CancellationToken ct)
    {
        var key = GuideWatch.Key(cause);
        Pause(key, cause == GuideLoss.Light ? "강한 빛이 들어왔어요 · 같은 자리에서 기다리는 중" : "별이 돌아오기를 기다리는 중");
        var since = DateTimeOffset.Now;
        var lastTry = DateTimeOffset.MinValue;
        // PHD2는 별이 같은 자리에 돌아오면 스스로 다시 잡는다(LostLock → Guiding, 2026-10-07 시뮬레이터 실기) →
        // 처음 3분은 손대지 않고, 그 뒤 2분마다 별을 다시 골라 본다 (오래 멈춰 별이 흘렀을 수 있음)
        var hands = mode.Simulate ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromMinutes(3);
        var retry = mode.Simulate ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromMinutes(2);
        while (true)
        {
            if (_stopRequested) { Resume(null); return true; }
            var raw = await devices.GuideRawAsync(ct);
            if (!raw.MountTracking) { Resume(null); return await HandleLossAsync(ctx, GuideLoss.MountStopped, ct); }
            if (raw.Guiding) break;
            var waited = DateTimeOffset.Now - since;
            if (cause == GuideLoss.Light && waited > LightWait) { cause = GuideLoss.Cloud; key = "cloud"; Pause(key, "별이 돌아오기를 기다리는 중", keepSince: true); }
            if (cause == GuideLoss.Cloud && waited > CloudWait)
            {
                Resume(null);
                Note("별이 30분 넘게 돌아오지 않아 촬영을 멈췄어요 · 하늘을 확인해 주세요", Tone.Fail, 600);
                await EndAsync("cloud", ct);
                return false;
            }
            if (cause == GuideLoss.Cloud && waited > hands && DateTimeOffset.Now - lastTry > retry) { lastTry = DateTimeOffset.Now; await devices.ReselectStarAsync(ct); }
            Set(() => { _note = new StatusLine($"멈춤 · {(cause == GuideLoss.Light ? "강한 빛이 지나가기를" : "별이 돌아오기를")} 기다리는 중 · {(int)waited.TotalMinutes}분째", Tone.Warn); _noteUntil = DateTimeOffset.MaxValue; });
            await Task.Delay(PollMs, ct);
        }
        if (DateTimeOffset.Now - since > RecenterAfter && !await RecenterSafelyAsync(ctx, ct)) return false;
        Resume("별이 돌아왔어요 · 이어서 찍어요");
        return true;
    }

    /// <summary>
    /// 다시 가운데 (CX-SHOOT-05): 가이딩 멈춤 확인 → 적도의 잠금 → 센터링 → 가이딩 재개·확인. 어느 단계든 실패하면 촬영을 멈추고 알린다(false).
    /// 가이딩 중에 적도의를 옮기지 않는다
    /// </summary>
    private async Task<bool> RecenterSafelyAsync(PrepContext ctx, CancellationToken ct)
    {
        Pause(_pause ?? "cloud", "다시 가운데로 맞추는 중이에요", keepSince: true);
        string? fail = null;
        if (_hasGuider && !await devices.StopGuidingAsync(ct)) fail = "가이드를 멈추지 못해 다시 가운데로 맞추지 못했어요";
        else
        {
            bool centered;
            await using (await ctx.Mount.AcquireAsync(ct)) centered = await devices.RecenterAsync(ctx, ct);
            if (!centered) fail = "다시 가운데로 맞추지 못했어요 (별·구름 확인)";
            else if (_hasGuider && !await devices.ResumeGuidingAsync(ct)) fail = "다시 가운데로 맞췄지만 가이드를 다시 켜지 못했어요";
        }
        if (fail is null) return true;
        Resume(null);
        Note(fail + " · 촬영을 멈췄어요", Tone.Fail, 600);
        await EndAsync("recenter", ct);
        return false;
    }

    /// <summary>제외할 사진: 제외 폴더로 옮기고 F로 센다</summary>
    private void ExcludeFrame(string file, string why)
    {
        var moved = MoveExcluded(file);
        Set(() => { _excluded++; _tally["F"]++; _lastGrade = "F"; _folder ??= Path.GetDirectoryName(file); });
        Note($"{_good + _excluded}번 사진: {why} · {(moved ? "제외 폴더로 옮겼어요" : "제외 폴더로 옮기지 못했어요 (직접 옮겨 주세요)")}", Tone.Warn, 8);
        if (_ctx is { } c) Record(c);
    }

    /// <summary>제외 폴더로 옮긴다. 못 옮기면(잠김·권한·같은 이름) 기록해 두고 false — 등급 F는 그대로 (CX-APP-R2)</summary>
    private bool MoveExcluded(string file)
    {
        string? moved;
        try { moved = devices.MoveToExcluded(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { moved = null; }
        if (moved is not null) return true;
        lock (_gate) _notMoved.Add(file);
        return false;
    }

    private void Pause(string key, string text, bool keepSince = false) => Set(() =>
    {
        if (!keepSince || _pause is null) _pausedSince = DateTimeOffset.Now;
        _pause = key;
        _mode = ShootMode.Paused;
        _note = new StatusLine(text, Tone.Warn);
        _noteUntil = DateTimeOffset.MaxValue;
    });

    private void Resume(string? message)
    {
        Set(() =>
        {
            if (_pause is { } k) _pausedMinutes[k] = _pausedMinutes.GetValueOrDefault(k) + (DateTimeOffset.Now - _pausedSince).TotalMinutes;
            _pause = null;
            _mode = ShootMode.Shoot;
            _note = null;
        });
        if (message is not null) Note(message, Tone.Ok);
    }

    private async Task PollGuideAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return; // 앞 조회가 아직이면 건너뜀 (느린 API에서 겹치지 않게)
        try
        {
            if (await devices.GuideRmsAsync(ct) is { } g)
                Set(() => { _guide.Add(g); if (_guide.Count > Spark) _guide.RemoveAt(0); });
        }
        catch (Exception) { /* 가이드 값은 다음에 */ }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private static int Median(List<int> xs) => xs.Order().ElementAt(xs.Count / 2);
}
