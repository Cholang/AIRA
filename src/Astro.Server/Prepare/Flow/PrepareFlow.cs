namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 장비 준비 · 대상 두 묶음 (OPERATION_PROCEDURE.md "장비 준비 · 대상", 2026-10-07 결정). 러너 코드는 같고 작업 목록·설정만 다르다.
/// 묶음 사이의 일을 맡는다:
/// - 결과 기록은 그날 밤 두 묶음이 함께 쓴다 — 대상을 바꿔도 장비 준비 결과 유지 (밤이 바뀌면 새로)
/// - 대상 묶음은 장비 준비가 모두 끝났을 때만 시작
/// - 장비 준비 작업을 (다시) 할 때는 대상 묶음을 먼저 멈춰 두고(가이딩 정지 확인 포함) 영향받는 작업을 "다시 확인 필요"로
/// - 대상 묶음이 장비 준비 작업을 다시 하자고 하면(가이딩 → 캘리브레이션) 대상을 멈춰 두고 장비 준비로 넘긴다.
///   장비 준비가 다시 끝나 같은 계획으로 대상을 시작하면 다시 확인할 첫 작업부터 이어서 한다
/// </summary>
public sealed class PrepareFlow
{
    /// <summary>장비 준비 작업을 다시 하면 대상 묶음에서 다시 확인할 작업. 극축·캘리브레이션은 적도의를 옮기므로 전부</summary>
    public static readonly IReadOnlyDictionary<string, string[]> CrossDependents = new Dictionary<string, string[]>
    {
        ["polar"] = ["slew", "center", "focuscheck", "guiding", "test"],
        ["calibration"] = ["slew", "center", "focuscheck", "guiding", "test"],
        ["focus"] = ["focuscheck", "test"],
    };

    public static readonly string[] RigTaskIds = ["polar", "calibration", "focus"];
    /// <summary>마무리 묶음 (OPERATION_PROCEDURE.md "마무리")</summary>
    public static readonly string[] WrapTaskIds = ["flat", "dark", "pack"];

    private readonly Lock _gate = new();
    private PrepResults _results = new();
    private DateOnly? _evening;

    public PrepareRunner Rig { get; }
    public PrepareRunner Target { get; }
    public PrepareRunner Wrap { get; }

    /// <param name="prepTimes">준비 시간 기록 (대상 묶음의 이동~시험 사진이 실제로 끝나면). 모의 장비면 null</param>
    public PrepareFlow(IEnumerable<IPrepTask> tasks, ILogger<PrepareRunner> log, bool simulated, TimeSpan? autoNextDelay = null, PrepTimeRecorder? prepTimes = null)
    {
        var all = tasks.ToList();
        var delay = autoNextDelay ?? TimeSpan.FromSeconds(1.8);
        Rig = new PrepareRunner(all.Where(t => RigTaskIds.Contains(t.Id)), log, RunnerSetup.Rig) { Simulated = simulated, AutoNextDelay = delay };
        Target = new PrepareRunner(all.Where(t => !RigTaskIds.Contains(t.Id) && !WrapTaskIds.Contains(t.Id)), log, RunnerSetup.Target) { Simulated = simulated, AutoNextDelay = delay };
        Wrap = new PrepareRunner(all.Where(t => WrapTaskIds.Contains(t.Id)), log, RunnerSetup.Wrap) { Simulated = simulated, AutoNextDelay = delay };
        if (prepTimes is not null) Target.TaskFinished = (task, result, ctx) => prepTimes.Finished(task, result, ctx, Target.Tasks);

        // 장비 준비 작업이 돌기 전: 대상 묶음이 장비를 쓰고 있으면 멈춰 두고 영향받는 작업을 다시 확인 필요로
        Rig.BeforeRun = id => Target.SuspendAsync(CrossDependents.GetValueOrDefault(id, []), "장비 준비를 다시 하는 중이에요", handoff: "rig");
        // 대상 묶음이 장비 준비 작업을 다시 하자고 함 → 대상을 멈춰 두고 장비 준비에서 다시
        Target.ForeignRedo = async id =>
        {
            if (!RigTaskIds.Contains(id)) return;
            // 요청한 작업(가이딩)의 실행이 먼저 끝나게 한 뒤 멈춘다 (자기 실행을 기다리지 않게)
            await Task.Yield();
            if (!await Target.SuspendAsync(CrossDependents.GetValueOrDefault(id, []), "장비 준비를 다시 하는 중이에요", handoff: "rig")) return;
            await Rig.RedoAsync(id, fromRunning: false);
        };
    }

    /// <summary>그날 밤 결과 기록 (두 묶음이 함께). 밤이 바뀌면 새로 만든다</summary>
    public PrepResults ResultsFor(DateOnly evening)
    {
        lock (_gate)
        {
            if (_evening != evening)
            {
                _evening = evening;
                _results = new PrepResults();
            }
            return _results;
        }
    }

    /// <summary>장비 준비 시작 (그날 밤 하나 — 같은 밤이면 하던 곳에서 그대로)</summary>
    public void StartRig(PrepContext ctx, DateOnly evening) => Rig.Start(ctx, $"rig:{evening:yyyy-MM-dd}");

    /// <summary>대상 시작 (확정 계획 하나). 장비 준비가 끝나지 않았으면 이유</summary>
    public string? StartTarget(PrepContext ctx)
    {
        if (!Rig.AllDone) return "장비 준비가 아직 끝나지 않았습니다. 장비 준비를 먼저 끝내 주세요.";
        Target.Start(ctx);
        return null;
    }

    /// <summary>마무리 시작 (그날 밤 하나 — 같은 밤이면 하던 곳에서 그대로). 촬영이 끝난 뒤</summary>
    public void StartWrap(PrepContext ctx, DateOnly evening) => Wrap.Start(ctx, $"wrap:{evening:yyyy-MM-dd}");

    // AA를 다시 켜 이어서 (Session/NightSession): 같은 열쇠로 시작해 화면이 열 때 "같은 계획 — 이어서"가 되게
    public Task ResumeRigAsync(PrepContext ctx, DateOnly evening, IReadOnlySet<string> done) => Rig.StartResumedAsync(ctx, $"rig:{evening:yyyy-MM-dd}", done);
    /// <summary>
    /// 이어서 하기: 계획 화면의 확정은 지난 실행의 누름이라 이번 이동 승인이 아니다 — 쓴 것으로 두고 시작해 이동을 다시 묻게 한다
    /// (CX-36-02: 확정 뒤 2분 안에 다시 켜 이어 가면 누르지 않아도 이동했다)
    /// </summary>
    public Task ResumeTargetAsync(PrepContext ctx, IReadOnlySet<string> done)
    {
        ctx.MoveApprovalUsed = ctx.Plan.ConfirmedAt;
        return Target.StartResumedAsync(ctx, ctx.Plan.ConfirmedAt.ToString("O"), done);
    }
    public Task ResumeWrapAsync(PrepContext ctx, DateOnly evening, IReadOnlySet<string> done) => Wrap.StartResumedAsync(ctx, $"wrap:{evening:yyyy-MM-dd}", done);

    /// <summary>세 묶음에서 끝난 작업들</summary>
    public IReadOnlyList<string> DoneIds() => [.. Rig.DoneIds(), .. Target.DoneIds(), .. Wrap.DoneIds()];

    /// <summary>촬영 중 N.I.N.A.가 꺼졌다 다시 켜졌을 때: 적도의 위치·가이딩을 알 수 없으므로 대상 작업 전체를 다시 확인 (같은 계획으로 시작하면 이동부터)</summary>
    public Task<bool> RecheckTargetAsync() =>
        Target.SuspendAsync(Target.Tasks.Select(t => t.Id).ToList(), "N.I.N.A.가 다시 켜져 대상 맞추기부터 다시 해요");

    // ── 구도 회전 (⑤ 센터링 뒤, 2026-10-16 사용자 결정 — Tasks.Center.Framing) ─────────

    /// <summary>[모의] 카메라를 돌렸다고 칠 때 (모의 센터링 장비가 다음 사진의 방향을 바꾼다). 실장비에서는 null</summary>
    public Action<double>? SimulateTurn { get; set; }

    /// <summary>센터링을 마친 사진에서 틀을 돌린 각도(화면 시계 방향 +)를 원하는 구도로 정한다. 허용 오차 안이면 지운다. 안 되면 이유</summary>
    public string? SetFraming(double turnDeg)
    {
        if (CenterPhoto() is not { } at) return "센터링을 마친 사진에서만 구도를 돌릴 수 있습니다.";
        var turn = Tasks.Center.Framing.Wrap(turnDeg);
        if (Math.Abs(turn) <= Tasks.Center.Framing.ToleranceDeg) at.Ctx.Results.Remove(typeof(Tasks.Center.FramingGoal));
        else at.Ctx.Results.Set(new Tasks.Center.FramingGoal(Tasks.Center.FramingGoal.KeyOf(at.Ctx), at.Angle, turn));
        return null;
    }

    /// <summary>카메라를 돌린 뒤: 센터링을 다시 해 새 사진으로 남은 각도를 잰다 (CenterTask가 정해 둔 구도와 비교). 안 되면 이유</summary>
    public string? CheckFraming()
    {
        if (CenterPhoto() is not { } at) return "센터링을 마친 사진에서만 확인할 수 있습니다.";
        if (at.Ctx.Results.Get<Tasks.Center.FramingGoal>() is not { } goal || goal.PlanKey != Tasks.Center.FramingGoal.KeyOf(at.Ctx)) return "돌릴 구도가 정해지지 않았습니다.";
        SimulateTurn?.Invoke(goal.TurnDeg);
        _ = Target.RedoAsync("center", fromRunning: false);
        return null;
    }

    /// <summary>대상 묶음이 센터링을 마치고 확정을 기다리는 중이면 그 상황과 사진의 카메라 방향</summary>
    private (PrepContext Ctx, double Angle)? CenterPhoto()
    {
        var v = Target.View();
        if (Target.Context is not { HasPlan: true } ctx || v.Current?.TaskId != "center"
            || v.Tasks.FirstOrDefault(t => t.Id == "center")?.Status != PrepTaskStatus.Done) return null;
        return ctx.Results.Get<CenterResult>() is { Skipped: false, CameraAngleDeg: { } angle } ? (ctx, angle) : null;
    }

    public PrepareRunner? Runner(string group) => group switch
    {
        "rig" => Rig,
        "target" => Target,
        "wrap" => Wrap,
        _ => null,
    };
}
