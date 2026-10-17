using System.Runtime.CompilerServices;
using Astro.Server.Prepare.Flow;

namespace Astro.Server.Shoot;

/// <summary>
/// 촬영 (OPERATION_PROCEDURE.md "촬영", 시안 mockups/aa-shoot-wrap-v10.html). 확정 계획·대상 묶음의 결과로 찍는다.
/// 사진마다 등급(ShotGrader) → F면 제외 폴더로. 디더링(3장마다)·자오선 반전·초점 다시 맞추기·구름 멈춤은 묻지 않고 자동.
/// 끝: 계획 장수(쓸 사진 기준) · 대상이 계획의 끝 고도(기본 30°) 아래 · 계획 끝 시각(새벽) — 또는 사용자의 "촬영 중단"(지금 사진까지 찍고).
/// 끝나면 가이딩을 멈춘다. 그 뒤(마무리 / 다른 대상)는 화면이 고른다.
/// </summary>
public sealed partial class ShootSession(IShootDevices devices, Prepare.PrepareMode mode, ILogger<ShootSession> log, Equipment.EquipmentLearned? learned = null)
{
    public const int DitherEvery = 3;
    public const double FlipAfterDeg = 1.25;        // 자오선 지나 5분 뒤 반전
    public const double RefocusTempC = 2;
    public const double RefocusHfrFactor = 1.3;
    private const int Spark = 48;

    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _version;
    private CancellationTokenSource? _cts;
    private Task _run = Task.CompletedTask;
    private PrepContext? _ctx;
    private string? _key;

    // 화면 상태
    private ShootMode _mode = ShootMode.Idle;
    private int _good, _excluded, _elapsed, _flipStep;
    private readonly List<double> _guide = [], _hfr = [];
    private readonly Dictionary<string, int> _tally = ShotGrader.Letters.ToDictionary(l => l, _ => 0);
    private string? _lastGrade, _ended, _folder;
    private StatusLine? _note;
    private DateTimeOffset _noteUntil;
    private List<object>? _focusPoints;
    private DateTimeOffset? _photoAt;
    private double _focusHfr = 2;
    private volatile bool _stopRequested;
    // 가이드 별 잃음 (GuideWatch)
    private string? _pause;
    private DateTimeOffset _pausedSince;
    private readonly Dictionary<string, double> _pausedMinutes = [];
    private readonly List<double> _snrSamples = [], _hfdSamples = [];
    private double _snrBaseline, _hfdBaseline;
    private double? _mainStarsRatio, _mainMeanRatio;
    private readonly List<DateTimeOffset> _jumps = [], _hotPixels = [];
    private readonly Dictionary<GuideLoss, DateTimeOffset> _lastDegrade = [];
    private bool _windWarned;
    // CX-SHOOT-01: 끝날 때 가이딩 정지를 확인했는가 (확인 전에는 마무리·다른 대상으로 못 감). 가이더 없는 구성은 감시·정지를 건너뜀(07)
    private bool _guideStopped = true, _hasGuider = true;
    // CX-NIGHT-02: 반전 실패 뒤 적도의가 멈춘 것을 확인했는가 (확인 전에는 다른 대상·마무리로 못 감)
    private bool _mountStill = true;
    // CX-NIGHT-03: 가이딩 안정 기다리기를 "촬영 중단"·답이 바로 깨우도록
    private CancellationTokenSource? _wake;
    private long _seenSteps;
    private int _polling;
    private readonly List<string> _notMoved = [];
    // 디더링 안정화 실패 (2026-10-06 사용자 결정): 연속 3번이면 멈추고 안정되면 자동 재개, 15분 넘게 안 되면 "그대로 찍을까요?"
    private int _ditherFails;
    // 장 사이 쉬는 시간 재기 — 끝날 때 지금 카메라의 배운 값으로 남긴다 (2026-10-17)
    private readonly FrameGap _gap = new();
    private string? _ask, _answer;
    private bool _unstableAccepted;
    private DateTimeOffset _lowHfdWarned, _dewWarned;
    private DewStatus? _dew;
    private bool _flipped;
    private double? _lastFocusTemp;
    private readonly List<double> _hfrRecent = [];
    private int _recordIndex = -1;
    public const int UnstableAfter = 3;
    // 제외 사진이 이어짐: 가이드 별은 멀쩡한데 주 사진만 나쁠 때(렌즈 덮개·주 경통 이슬·초점·구름) — 2026-10-08 시뮬레이터 전체 시험에서 35장이 말없이 제외됨.
    // 5장 연속이면 멈추고 3분마다 한 장씩 확인 → 쓸 사진이 나오면 이어서, 30분 넘으면 촬영 끝 (2026-10-08 사용자 결정 — 가이드 별 신호는 이 경우 멀쩡해서 재개 기준으로 쓰지 않음)
    public const int ExcludedRunAlert = 5;
    public static readonly TimeSpan FramesRetry = TimeSpan.FromMinutes(3), FramesGiveUp = TimeSpan.FromMinutes(30);
    private int _excludedRun;
    private DateTimeOffset? _framesPausedSince;
    private DateTimeOffset _framesLastTry;
    /// <summary>[테스트] 제외 사진이 이어져 멈춘 횟수</summary>
    internal int ExcludedRunAlerts { get; private set; }
    public static readonly TimeSpan UnstableHold = TimeSpan.FromMinutes(1), UnstableAsk = TimeSpan.FromMinutes(15);

    public static readonly int[] GuideExposuresMs = [1000, 1500, 2000, 2500, 3000, 3500, 4000];
    public static readonly TimeSpan LightWait = TimeSpan.FromMinutes(2), CloudWait = TimeSpan.FromMinutes(30), RecenterAfter = TimeSpan.FromMinutes(10);

    public PrepContext? Context { get { lock (_gate) return _ctx; } }

    /// <summary>촬영이 진행 중인가 (멈춤·반전·초점 다시 맞추기 포함, 끝났거나 시작 전이면 false)</summary>
    public bool Active => _mode is not (ShootMode.Idle or ShootMode.Ended);

    public ShootView View()
    {
        lock (_gate) return Snapshot();
    }

    private ShootView Snapshot()
    {
        var c = _ctx;
        if (_note is not null && DateTimeOffset.Now > _noteUntil) _note = null;
        DateTimeOffset? flipAt = null, endAt = null;
        if (c is not null && _mode is not ShootMode.Ended and not ShootMode.Idle)
        {
            var ha = devices.HourAngleDeg(c);
            if (ha < FlipAfterDeg && !_flipped) flipAt = c.Now().AddMinutes((FlipAfterDeg - ha) / 15 * 60);
            var left = Math.Max(0, c.Plan.EstimatedFrames - _good);
            var est = c.Now().AddSeconds(left * (c.ExposureSeconds + 8.0));
            endAt = est < c.Plan.End ? est : c.Plan.End;
        }
        return new ShootView(c is not null, c is null ? null : c.TargetName, c?.Plan.EstimatedFrames ?? 0, _good, _excluded,
            c?.ExposureSeconds ?? 0, c is null ? 0 : c.Plan.Iso, _elapsed, _mode, _flipStep, flipAt,
            flipAt is { } fa && c is not null ? Math.Max(1, (int)Math.Ceiling((fa - c.Now()).TotalMinutes)) : null, _guide.ToArray(), _hfr.ToArray(), _focusHfr,
            c?.MainPixelScaleArcsec ?? 0, _lastGrade, new Dictionary<string, int>(_tally), ShotGrader.Criteria, _note, _focusPoints?.ToArray(),
            _ended, endAt, devices.PhotoUrl, _photoAt, _version, mode.Simulate,
            _pause, _pause is null ? 0 : (int)(DateTimeOffset.Now - _pausedSince).TotalSeconds, devices.GuideExposureMs, _guideStopped, _ask, _dew, _mountStill);
    }

    public async IAsyncEnumerable<ShootView> WatchAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var last = -1;
        while (!ct.IsCancellationRequested)
        {
            ShootView view;
            Task changed;
            lock (_gate)
            {
                view = Snapshot();
                changed = _changed.Task;
            }
            if (view.Version != last) { last = view.Version; yield return view; }
            try { await changed.WaitAsync(TimeSpan.FromSeconds(2), ct); }
            catch (TimeoutException) { /* 알림이 끝났는지 다시 그리기 */ last = -1; }
            catch (OperationCanceledException) { break; }
        }
    }

    public Task NextChangeAsync()
    {
        lock (_gate) return _changed.Task;
    }

    private void Changed()
    {
        _version++;
        var old = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
    }

    private void Set(Action change)
    {
        lock (_gate)
        {
            change();
            if (_modes.Count == 0 || _modes[^1] != _mode) _modes.Add(_mode);
            Changed();
        }
    }

    private readonly List<ShootMode> _modes = [];

    /// <summary>[테스트] 지나온 상태들</summary>
    internal IReadOnlyList<ShootMode> ModeHistory { get { lock (_gate) return _modes.ToArray(); } }

    private void Note(string text, Tone tone, double seconds = 6) =>
        Set(() => { _note = new StatusLine(text, tone); _noteUntil = DateTimeOffset.Now.AddSeconds(seconds); });

    private async Task RunAsync(PrepContext ctx, CancellationToken ct)
    {
        _lastFocusTemp = ctx.Results.Get<FocusCheckResult>() is { Refocused: true, TemperatureC: { } t } ? t : ctx.Results.Get<FocusResult>()?.TemperatureC;
        int? baseStars = null; double? baseMean = null;
        var firstStars = new List<int>(); var firstMeans = new List<double>();
        var fails = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // 끝 조건
                if (EndReason(ctx) is { } endWhy) { await EndAsync(endWhy, ct); return; }

                // 자오선 반전 (항상 자동, 한 번)
                if (!_flipped && devices.HourAngleDeg(ctx) > FlipAfterDeg)
                {
                    _gap.Spoil();
                    Set(() => { _mode = ShootMode.Flip; _flipStep = 0; });
                    FlipOutcome flip;
                    await using (await ctx.Mount.AcquireAsync(ct)) flip = await devices.FlipAsync(ctx, s => Set(() => _flipStep = s), ct);
                    if (!flip.Flipped)
                    {
                        // 반전 자체가 안 됨: 자오선 너머로 계속 찍지 않는다 (CX-SHOOT-02). 적도의가 멈춘 것을 확인하지 못했으면 다음으로 못 감 (CX-NIGHT-02)
                        Set(() => _mountStill = flip.MountStill);
                        Note("자오선 반전이 되지 않았어요 · 적도의를 확인해 주세요. 촬영을 멈췄어요", Tone.Fail, 600);
                        await EndAsync("flip", ct);
                        return;
                    }
                    _flipped = true;
                    Set(() => _mode = ShootMode.Shoot);
                    Note(flip.Centered ? "자오선 반전을 마쳤어요 · 다시 찍어요" : "반전은 했지만 가운데 맞추기가 안 됐어요 · 그대로 이어서 찍어요", flip.Centered ? Tone.Ok : Tone.Warn);
                    // 가이딩 재개 실패는 아래 가이드 별 확인이 원인별로 처리
                }

                await CheckDewAsync(ct);

                // 초점 다시 맞추기: 기온 2°C 이상, 또는 최근 좋은 사진의 별 크기가 초점 때보다 30% 넘게 큼
                var temp = await devices.TemperatureAsync(ct);
                var recent = _hfrRecent.Count >= 3 ? _hfrRecent.TakeLast(3).Average() : (double?)null;
                var tempDrift = temp is { } tn && _lastFocusTemp is { } tf && Math.Abs(tn - tf) >= RefocusTempC;
                if (tempDrift || recent > _focusHfr * RefocusHfrFactor)
                {
                    _gap.Spoil();
                    var why = tempDrift ? $"초점을 맞춘 뒤 기온이 {Math.Abs(temp!.Value - _lastFocusTemp!.Value):F1}°C 바뀌었어요" : "최근 사진의 별이 커졌어요";
                    var points = new List<object>();
                    Set(() => { _mode = ShootMode.Focus; _focusPoints = []; _note = new StatusLine(why, Tone.Warn); _noteUntil = DateTimeOffset.MaxValue; });
                    var r = await devices.RefocusAsync((p, h) => { points.Add(new { pos = p, hfr = h }); Set(() => _focusPoints = [.. points]); }, ct);
                    if (r.StopUnconfirmed)
                    {
                        // 자동초점이 멈췄는지 모르면 다음 사진을 찍지 않는다 — 같은 카메라·포커서를 함께 쓰게 됨 (CX-NIGHT-06)
                        Set(() => { _mode = ShootMode.Shoot; _focusPoints = null; });
                        Note("자동초점이 멈췄는지 확인하지 못해 촬영을 멈췄어요 · N.I.N.A.를 확인해 주세요", Tone.Fail, 600);
                        await EndAsync("focus", ct);
                        return;
                    }
                    _lastFocusTemp = temp;
                    _hfrRecent.Clear();
                    Set(() => { _mode = ShootMode.Shoot; _focusPoints = null; _note = null; if (r.Ok) _focusHfr = r.Hfr; });
                    Note(r.Ok ? $"초점을 다시 맞췄어요 · 별 크기 {r.Hfr:F1} · 위치 {r.Position:N0}" : $"초점을 다시 맞추지 못했어요 · 그대로 찍어요 ({r.Problem})", r.Ok ? Tone.Ok : Tone.Warn);
                }

                // 가이드 별: 찍기 전에 확인 — 잃었으면 원인별로 (docs/implementation/SHOOT.md B)
                var before = await CheckGuideAsync(ct);
                if (before.IsLost())
                {
                    _gap.Spoil();
                    if (!await HandleLossAsync(ctx, before, ct)) return;
                    continue;
                }

                // 제외 사진이 이어져 멈춘 동안: 3분마다 한 장만 찍어 확인, 30분 넘으면 끝
                if (_framesPausedSince is { } fp)
                {
                    _gap.Spoil();
                    var retry = mode.Simulate ? TimeSpan.FromMilliseconds(300) : FramesRetry;
                    var giveUp = mode.Simulate ? TimeSpan.FromSeconds(3) : FramesGiveUp;
                    if (DateTimeOffset.Now - fp > giveUp)
                    {
                        Resume(null);
                        Note($"별이 없는 사진이 {(int)giveUp.TotalMinutes}분 넘게 이어져 촬영을 멈췄어요 · 렌즈 덮개·주 망원경 이슬·초점·구름을 확인해 주세요", Tone.Fail, 600);
                        await EndAsync("frames", ct);
                        return;
                    }
                    var left = retry - (DateTimeOffset.Now - _framesLastTry);
                    if (left > TimeSpan.Zero)
                    {
                        Set(() => { _note = new StatusLine($"멈춤 · 별이 없는 사진이 이어져요 · {(int)(DateTimeOffset.Now - fp).TotalMinutes}분째 · {Math.Ceiling(left.TotalSeconds):0}초 뒤 한 장 확인", Tone.Warn); _noteUntil = DateTimeOffset.MaxValue; });
                        await WakeableDelayAsync(left < TimeSpan.FromSeconds(5) ? left : TimeSpan.FromSeconds(5), ct);
                        continue;
                    }
                    _framesLastTry = DateTimeOffset.Now;
                }

                // 반전·초점·가이드 별 확인 사이에 끝 조건이 됐으면(멈춤 포함) 새 사진을 시작하지 않는다 (2026-10-14 자체 리뷰: 한 장 더 찍고 멈춰 종료 한도를 넘겼다)
                if (EndReason(ctx) is { } lateWhy) { await EndAsync(lateWhy, ct); return; }

                // 한 장 (찍는 동안에도 가이드 별을 지켜봄 — 오래 잃으면 이 장을 멈추고 버림)
                Set(() => { _mode = _stopRequested ? ShootMode.Finishing : ShootMode.Shoot; _elapsed = 0; });
                _gap.Begin(DateTimeOffset.Now, ctx.ExposureSeconds);
                var (shot, lost) = await ExposeWatchedAsync(ctx, ct);
                if (lost != GuideLoss.None)
                {
                    _gap.Spoil();
                    // 노출을 멈추지 못해 사진이 저장됐으면 제외 폴더로 옮기고 F로 센다 (CX-SHOOT-09)
                    if (shot.Ok && shot.File is { } bad) ExcludeFrame(bad, "가이드 별을 잃은 동안 찍혔어요");
                    if (!await HandleLossAsync(ctx, lost, ct)) return;
                    continue;
                }
                if (!shot.Ok || shot.Stats is not { } st)
                {
                    _gap.Spoil();
                    fails++;
                    Note($"사진을 받지 못했어요 · 다시 찍어요 ({shot.Problem})", Tone.Warn);
                    if (fails >= 3) { await EndAsync("error", ct); return; }
                    continue;
                }
                fails = 0;
                var rms = st.GuideRmsArcsec ?? await devices.GuideRmsAsync(ct);
                var stats = st with { GuideRmsArcsec = rms };
                var grade = ShotGrader.Judge(stats, new GradeBaseline(_focusHfr, baseStars, baseMean, ctx.MainPixelScaleArcsec));
                var moveFailed = grade.Excluded && shot.File is { } badFile && !MoveExcluded(badFile);
                if (!grade.Excluded)
                {
                    // 그날 밤 기준: 처음 좋은 사진 세 장의 별 수·배경
                    if (baseStars is null) { firstStars.Add(stats.Stars); if (stats.Mean is { } m) firstMeans.Add(m); }
                    if (baseStars is null && firstStars.Count >= 3) { baseStars = Median(firstStars); baseMean = firstMeans.Count >= 3 ? firstMeans.Order().ElementAt(1) : null; }
                    _hfrRecent.Add(stats.Hfr);
                }
                // 가이드 별 원인 판정에 쓰는 주 사진 신호 (그날 기준 대비)
                _mainStarsRatio = baseStars is > 0 ? stats.Stars / (double)baseStars : null;
                _mainMeanRatio = baseMean is > 0 && stats.Mean is { } mm ? mm / baseMean : null;
                Set(() =>
                {
                    if (grade.Excluded) _excluded++; else _good++;
                    _tally[grade.Letter]++;
                    _lastGrade = grade.Letter;
                    if (!grade.Excluded) { _hfr.Add(stats.Hfr); if (_hfr.Count > 20) _hfr.RemoveAt(0); }
                    if (shot.File is { } f) _folder ??= Path.GetDirectoryName(f);
                    _photoAt = DateTimeOffset.Now;
                });
                if (grade.Excluded) Note($"{_good + _excluded}번 사진: {grade.Reason} · {(moveFailed ? "제외 폴더로 옮기지 못했어요 (직접 옮겨 주세요)" : "제외 폴더로 옮겼어요")}", Tone.Warn, 8);
                _excludedRun = grade.Excluded ? _excludedRun + 1 : 0;
                if (_framesPausedSince is not null)
                {
                    if (grade.Excluded) Pause("frames", $"확인 사진도 별이 없어요 ({grade.Reason}) · 3분 뒤 다시 확인해요", keepSince: true);
                    else { _framesPausedSince = null; Resume("별이 돌아왔어요 · 이어서 찍어요"); }
                }
                else if (_excludedRun >= ExcludedRunAlert)
                {
                    ExcludedRunAlerts++;
                    _framesPausedSince = _framesLastTry = DateTimeOffset.Now;
                    Pause("frames", $"제외한 사진이 {_excludedRun}장 이어져 촬영을 멈췄어요 ({grade.Reason}) · 렌즈 덮개·주 망원경 이슬·초점·구름을 확인해 주세요");
                }
                Record(ctx);

                // 디더링 (좋은 사진 3장마다)
                if (_hasGuider && !grade.Excluded && _good % DitherEvery == 0 && !_stopRequested && EndReason(ctx) is null) // 다음 사진이 필요할 때만 (CX-NIGHT-03)
                {
                    Set(() => _mode = ShootMode.Dither);
                    var settled = await devices.DitherAsync(ct);
                    Set(() => _mode = ShootMode.Shoot);
                    _ditherFails = settled ? 0 : _ditherFails + 1;
                    if (!settled && _ditherFails >= UnstableAfter && !_unstableAccepted)
                    {
                        _gap.Spoil();
                        if (!await WaitUnstableAsync(ctx, ct)) return;
                    }
                    else if (!settled) Note("디더링 뒤 가이드가 다 안정되지 않았어요 · 그대로 찍고 흐른 사진은 등급으로 걸러요", Tone.Warn);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e)
        {
            log.LogError(e, "촬영 오류");
            Note("예상하지 못한 오류로 촬영을 멈췄습니다", Tone.Fail, 600);
            await EndAsync("error", CancellationToken.None);
        }
    }

    /// <summary>기다리기 — "촬영 중단"이 바로 깨운다</summary>
    private async Task WakeableDelayAsync(TimeSpan wait, CancellationToken ct)
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate) _wake = wake;
        try { await Task.Delay(wait, wake.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        finally { lock (_gate) _wake = null; }
    }

    /// <summary>끝낼 이유: user(중단) · done(계획 장수) · low(계획의 끝 고도 아래 — 기본 30°, 시작을 낮게 잡았으면 그 고도) · dawn(계획 끝 시각). 아니면 null</summary>
    private string? EndReason(PrepContext ctx) =>
        _stopRequested ? "user"
        : _good >= ctx.Plan.EstimatedFrames ? "done"
        // 고도로 끝내는 것은 대상이 내려갈 때(자오선을 지난 뒤)만 — 올라오는 중에 촬영을 시작하면(지금 이동·준비가 빨리 끝남) 첫 장 전에 끝나 버렸다 (2026-10-14 자체 리뷰)
        : !ctx.IgnoreAltitude && devices.HourAngleDeg(ctx) > 0 && devices.TargetAltitude(ctx) < ctx.Plan.MinAltitude ? "low"
        // 계획 끝 시각: 새벽이거나 고도 한계(계획이 그때를 끝으로 계산) — 고도가 한계 근처면 low로 알린다 (새벽 2시에 "새벽 박명"이라 하지 않게)
        : ctx.Now() >= ctx.Plan.End ? (!ctx.IgnoreAltitude && devices.HourAngleDeg(ctx) > 0 && devices.TargetAltitude(ctx) < ctx.Plan.MinAltitude + 1 ? "low" : "dawn")
        : null;

    /// <summary>[테스트] 촬영이 끝날 때까지 기다린다</summary>
    internal Task Running => _run;
}
