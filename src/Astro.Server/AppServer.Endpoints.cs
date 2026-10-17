using System.Text.Json.Serialization;
using Astro.Nina;
using Astro.Server.Assistant;
using Astro.Server.Engine;
using Astro.Server.Profiles;
using Astro.Server.Setup;
using Astro.Server.Sky;
using Microsoft.Extensions.Options;

namespace Astro.Server;

/// <summary>코어 서버 — 영역별 엔드포인트 묶음(촬영·준비·설정·계획·오늘 밤 기록·프로필) (AppServer 나눔, 등록 순서는 AppServer.Build)</summary>
public static partial class AppServer
{
    /// <summary>촬영 (OPERATION_PROCEDURE.md "촬영") · 오늘 밤 요약 (마무리 끝)</summary>
    private static void MapShoot(RouteGroupBuilder shoot, RouteGroupBuilder night)
    {
        // 대상 묶음이 끝난 상황으로 촬영 시작 (찍는 중이면 그대로 — 화면 재접속)
        shoot.MapPost("/start", (Prepare.PrepareStarter starter, Shoot.ShootSession session) =>
            starter.StartShoot(session) is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(session.View()));
        shoot.MapGet("/state", (Shoot.ShootSession session) => session.View());
        shoot.MapGet("/watch", (Shoot.ShootSession session, CancellationToken ct) =>
            TypedResults.ServerSentEvents(session.WatchAsync(ct), eventType: "state"));
        // "촬영 중단": 지금 사진까지 찍고 멈춘다 (그 뒤 마무리 / 다른 대상은 화면이 고름)
        shoot.MapPost("/stop", (Shoot.ShootSession session) =>
            session.Stop() is { } problem ? Results.BadRequest(new { error = problem }) : Results.NoContent());
        // N.I.N.A.가 예기치 않게 꺼짐: 촬영을 멈추고(가이딩 정지 확인) 대상 작업을 이동부터 다시 확인하게 (CX-APP-R4)
        shoot.MapPost("/abort-for-restart", async (Shoot.ShootSession session, Prepare.Flow.PrepareFlow flow) =>
        {
            await session.AbortForRestartAsync();
            await flow.RecheckTargetAsync();
            return Results.NoContent();
        });
        // 끝났는데 가이딩 정지를 확인하지 못했을 때 "장비 상태 다시 확인"
        shoot.MapPost("/recheck-stop", async (Shoot.ShootSession session) =>
            await session.RecheckStopAsync() is { } problem ? Results.Conflict(new { error = problem }) : Results.NoContent());
        // 촬영 중 질문에 답하기 (가이딩 불안정이 오래가면: shoot = 그대로 찍기 · wait = 더 기다리기)
        // [임시] 건너뛰기 (2026-10-14): 촬영을 다 찍은 것(ok=true)·오류로 끝난 것(ok=false)으로 끝낸다. 가이딩·적도의 정지를 확인하지 못했으면 이유
        shoot.MapPost("/temp-skip", async (bool? ok, Shoot.ShootSession session) =>
            await session.TempSkipAsync(ok ?? true) is { } problem ? Results.Conflict(new { error = problem }) : Results.NoContent());
        shoot.MapPost("/answer", (ShootAnswer body, Shoot.ShootSession session) =>
            session.Answer(body.Choice) is { } problem ? Results.Conflict(new { error = problem }) : Results.NoContent());

        // 오늘 밤 요약: 대상별 촬영 + 보정 프레임 + 장비 정리 (그날 밤 결과 기록에서)
        night.MapGet("/summary", (Prepare.Flow.PrepareFlow flow) =>
        {
            var r = flow.ResultsFor(Sky.TonightService.EveningOf(DateTimeOffset.Now));
            var targets = r.Get<Shoot.NightShootResult>()?.Targets ?? [];
            var tally = Shoot.ShotGrader.Letters.ToDictionary(l => l, l => targets.Sum(t => t.Tally.GetValueOrDefault(l)));
            var folder = targets.Select(t => t.Folder).FirstOrDefault(f => f is not null);
            return Results.Ok(new
            {
                targets, tally, folder,
                flat = r.Get<Prepare.Tasks.Wrap.FlatResult>(),
                dark = r.Get<Prepare.Tasks.Wrap.DarkResult>(),
                pack = r.Get<Prepare.Tasks.Wrap.PackResult>(),
            });
        });
        night.MapPost("/open-folder", (Prepare.Flow.PrepareFlow flow) =>
        {
            var r = flow.ResultsFor(Sky.TonightService.EveningOf(DateTimeOffset.Now));
            var folder = r.Get<Shoot.NightShootResult>()?.Targets.Select(t => t.Folder).FirstOrDefault(f => f is not null && Directory.Exists(f));
            if (folder is null) return Results.NotFound(new { error = "오늘 밤 사진 폴더를 찾지 못했습니다." });
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            return Results.NoContent();
        });
    }

    private static void MapPrepare(RouteGroupBuilder prepare)
    {
        // 묶음 두 개 (OPERATION_PROCEDURE.md "장비 준비 · 대상"): rig = 장비 준비(계획 없이, 그날 밤), target = 대상(확정 계획 하나). 같은 것이면 하던 곳에서 그대로
        prepare.MapPost("/rig/start", async (Prepare.PrepareStarter starter, Prepare.Flow.PrepareFlow flow, CancellationToken ct) =>
            await starter.StartRigAsync(ct) is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(flow.Rig.View()));
        // 마무리 (플랫 · 다크 · 장비 정리 — 촬영이 끝난 뒤, 그날 밤 하나)
        // 장비 준비가 끝나 계획으로 갈 때: 적도의를 홈에 두고 추적을 끈다 (2026-10-08 사용자 결정 — 계획·기다리는 시간이 길어도 한계로 흘러가지 않게).
        // 기다리지 않는다 — 대상 이동은 적도의 잠금을 기다리므로 홈이 끝난 뒤에 움직인다. Set Home은 쓰지 않는다(Go Home만)
        // 장비 준비 끝: 적도의를 홈에 두고 추적을 끈다 — "적도의 홈" 버튼과 같은 절차(홈 실패면 멈춤·정지 확인, CX-20-03)
        prepare.MapPost("/rig/rest", async (Prepare.MountHome home, CancellationToken ct) =>
        {
            await home.RestAfterRigAsync(ct);
            return Results.NoContent();
        });
        // 다른 대상으로 계획에 돌아올 때: 움직이지 않고 추적만 끈다 (MountHome.RestForPlanAsync). 끄지 못한 까닭은 화면이 계획 위에 알린다
        prepare.MapPost("/plan/rest", async (Prepare.MountHome home, CancellationToken ct) =>
            Results.Ok(new { problem = await home.RestForPlanAsync(ct) }));
        prepare.MapPost("/wrap/start", async (Prepare.PrepareStarter starter, Prepare.Flow.PrepareFlow flow, CancellationToken ct) =>
            await starter.StartWrapAsync(ct) is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(flow.Wrap.View()));
        prepare.MapPost("/target/start", (Prepare.PrepareStarter starter, Prepare.Flow.PrepareFlow flow) =>
            starter.StartTarget() is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(flow.Target.View()));
        prepare.MapGet("/{group}/state", (string group, Prepare.Flow.PrepareFlow flow) =>
            flow.Runner(group) is { } r ? Results.Ok(r.View()) : Results.NotFound());
        // 상태가 바뀔 때마다 (Server-Sent Events)
        prepare.MapGet("/{group}/watch", (string group, Prepare.Flow.PrepareFlow flow, CancellationToken ct) =>
            flow.Runner(group) is { } r ? (IResult)TypedResults.ServerSentEvents(r.WatchAsync(ct), eventType: "state") : Results.NotFound());
        // 버튼 (중앙 정보의 버튼·끝 버튼·다시 하기)
        prepare.MapPost("/{group}/act", (string group, PrepAct input, Prepare.Flow.PrepareFlow flow) =>
            flow.Runner(group) is not { } r ? Results.NotFound()
            : r.Act(input.Action) is { } problem ? Results.BadRequest(new { error = problem }) : Results.NoContent());
        // 구도 회전 (⑤ 센터링 뒤): 사진 위 틀을 돌린 각도(화면 시계 방향 +)를 원하는 구도로 · 카메라를 돌린 뒤 다시 찍어 확인
        prepare.MapPost("/target/framing", (FramingInput input, Prepare.Flow.PrepareFlow flow) =>
            flow.SetFraming(input.TurnDeg) is { } problem ? Results.BadRequest(new { error = problem }) : Results.NoContent());
        prepare.MapPost("/target/framing/check", (Prepare.Flow.PrepareFlow flow) =>
            flow.CheckFraming() is { } problem ? Results.BadRequest(new { error = problem }) : Results.NoContent());
        // 중단 (진행 표시 아래) — 하던 작업을 멈추고 정지를 확인
        prepare.MapPost("/{group}/abort", async (string group, Prepare.Flow.PrepareFlow flow) =>
            flow.Runner(group) is not { } r ? Results.NotFound()
            : await r.AbortAsync() ? Results.NoContent() : Results.Conflict(new { error = "장비가 멈췄는지 확인하지 못했습니다. 장비 상태를 확인해 주세요." }));

        // [임시] 건너뛰기 — 지금(또는 다음) 작업을 더미 성공(ok=true)·실패(ok=false) 기록으로 넘김 (2026-10-14 모든 작업, PrepareRunner.TempSkipAsync — 지울 때 같이)
        prepare.MapPost("/{group}/temp-skip", async (string group, bool? ok, Prepare.Flow.PrepareFlow flow) =>
            flow.Runner(group) is not { } r ? Results.NotFound()
            : await r.TempSkipAsync(ok ?? true) is { } problem ? Results.BadRequest(new { error = problem }) : Results.NoContent());

        // [모의] 화면·시험용: 다음 동작 하나 실패시키기(키는 SimFaults.Known), 대상이 보인다고 가정, 모의 속도
        prepare.MapPost("/sim/fail-next/{key}", (string key, Prepare.Sim.SimFaults faults) =>
        {
            if (!Prepare.Sim.SimFaults.Known.Contains(key)) return Results.BadRequest(new { error = $"모르는 키: {key}", known = Prepare.Sim.SimFaults.Known });
            faults.Arm(key);
            return Results.NoContent();
        });
        prepare.MapPost("/sim/ignore-altitude", (Prepare.Flow.PrepareFlow flow) =>
        {
            if (flow.Target.Context is { } c) c.IgnoreAltitude = true;
            return Results.NoContent();
        });
        // 실장비: 하늘 화면 이미지 (sharpcap 창 캡처 · guide PHD2 사진 · photo 솔빙 사진 · test 시험 사진)
        // 화면이 가진 것과 같은 이미지면(If-None-Match = 표지) 다시 보내지 않는다 (304) — 1초마다 묻는 실시간 사진이 같은 사진을 통째로 받지 않게
        prepare.MapGet("/live/{kind}", async (string kind, HttpContext http, Prepare.Real.LiveImages images, CancellationToken ct) =>
        {
            if (await images.GetAsync(kind, ct) is not { } img) return Results.NotFound();
            http.Response.Headers.ETag = img.Tag;
            http.Response.Headers.CacheControl = "no-cache";
            return http.Request.Headers.IfNoneMatch.ToString() == img.Tag ? Results.StatusCode(304) : Results.File(img.Bytes, img.Type);
        });
        // 데스크톱 창이 SharpCap 영상을 직접 비추기 시작함: 영상 저장을 바로 멈춘다 (저장마다 SharpCap이 영상 위에 "Snapshot captured" 줄을 띄움 — SharpCapBridge.StopView)
        prepare.MapPost("/sharpcap/view-off", () => { Prepare.Real.SharpCapBridge.StopView(); return Results.NoContent(); });
        // SharpCap 스크립트가 극축 정렬 상태를 보내고, 답으로 AA의 명령(advance · exposure:ms · quit)을 받는다
        prepare.MapPost("/sharpcap/report", (System.Text.Json.JsonElement body, Prepare.Real.SharpCapBridge bridge) => Results.Text(bridge.Receive(body)));
    }

    /// <summary>
    /// 설정 화면 (2026-10-11 사용자 결정): AI 비서 켜기·키 · 프로그램 경로(N.I.N.A.·SharpCap — PHD2는 N.I.N.A.가 켠다) · 기록 폴더 열기·진단 내보내기.
    /// 키 자체는 화면에 돌려주지 않는다 (있는지와 어디서 왔는지만)
    /// </summary>
    private static void MapSettings(RouteGroupBuilder settings)
    {
        settings.MapGet("", (Setup.UserSettings user, IOptions<Assistant.AssistantOptions> ai) => Results.Ok(new
        {
            aiEnabled = user.AiEnabled,
            keyFrom = user.GeminiKey is not null ? "settings" : string.IsNullOrWhiteSpace(ai.Value.ApiKeys.GetValueOrDefault("gemini")) ? null : "system",
            ninaExe = user.NinaExe,
            ninaAuto = InstallLocator.AutoNinaExe(),
            sharpCapExe = user.SharpCapExe,
            sharpCapAuto = Prepare.Real.SharpCapBridge.FindExe(),
            logsDir = Setup.Diagnostics.LogsDir,
        }));
        settings.MapPut("/ai", (SettingsAi input, Setup.UserSettings user) => { user.SetAiEnabled(input.Enabled); return Results.NoContent(); });
        settings.MapPut("/ai-key", (SettingsKey input, Setup.UserSettings user) => { user.SetGeminiKey(input.Key); return Results.NoContent(); });
        // 경로: 비우면 자동으로 찾기. 지정하면 그 파일이 있어야 하고 이름이 맞아야 한다 (다른 프로그램을 잘못 켜지 않게 — NINA 설치 프로그램 등)
        settings.MapPut("/paths/{which}", (string which, SettingsPath input, Setup.UserSettings user) =>
        {
            var path = string.IsNullOrWhiteSpace(input.Path) ? null : input.Path.Trim().Trim('"');
            var name = which switch { "nina" => "NINA.exe", "sharpcap" => "SharpCap.exe", _ => null };
            if (name is null) return Results.NotFound();
            if (path is not null && (!File.Exists(path) || !string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new { error = $"{name} 파일이 아닙니다. 설치 폴더의 {name}를 골라 주세요." });
            if (which == "nina") user.SetNinaExe(path); else user.SetSharpCapExe(path);
            return Results.NoContent();
        });
        settings.MapPost("/open-logs", () =>
        {
            Directory.CreateDirectory(Setup.Diagnostics.LogsDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Setup.Diagnostics.LogsDir}\"") { UseShellExecute = true });
            return Results.NoContent();
        });
        // 진단 묶음을 바탕 화면에 만들고 탐색기에서 그 파일을 골라 보여 준다
        settings.MapPost("/diagnostics", (Setup.UserSettings user) =>
        {
            var path = Setup.Diagnostics.Export(user);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return Results.Ok(new { path });
        });
    }

    private sealed record SettingsAi(bool Enabled);
    private sealed record SettingsKey(string? Key);
    private sealed record SettingsPath(string? Path);

    /// <summary>버튼. Step은 예전 화면 호환용(쓰지 않음)</summary>
    private sealed record PrepAct(string? Step, string Action);

    private static void MapPlan(RouteGroupBuilder plan)
    {
        // 화면을 열 때: 대화·계획·그래프를 한 번에 (처음이면 오늘 밤 정보를 준비하고 인사를 만든다)
        plan.MapGet("/state", async (PlanAssistant assistant, DsoCatalog catalog, CancellationToken ct) =>
        {
            if (await assistant.EnsureStartedAsync(ct) is { } problem) return Results.Ok(new { error = problem });
            var night = assistant.Night!;
            return Results.Ok(new
            {
                messages = assistant.Display,
                plan = assistant.View(),
                chart = assistant.Chart(),
                catalogAvailable = catalog.Available,
                cloudsAvailable = night.Clouds.Count > 0,
                // AI를 쓸 수 없으면(이유 문장) 대화 대신 이름 검색만 (2026-10-14)
                practiceReason = assistant.PracticeReason,
            });
        });

        plan.MapGet("/chart", (PlanAssistant assistant) =>
            assistant.Night is not null ? Results.Ok(assistant.Chart()) : Results.NotFound());

        // 화면에서 계획 고치기 (AI 도구와 같은 함수) — 고친 뒤의 계획·그래프를 돌려준다. 고칠 수 없으면 이유
        IResult Edited(PlanAssistant a, string? problem) =>
            problem is not null ? Results.BadRequest(new { error = problem })
            : a.Night is null ? Results.BadRequest(new { error = "오늘 밤 정보가 아직 없습니다. 화면을 다시 열어 주세요." })
            : Results.Ok(new { plan = a.View(), chart = a.Chart() });
        plan.MapGet("/search", (string? q, PlanAssistant assistant) => Results.Ok(assistant.Search(q ?? "")));
        plan.MapPost("/pick", (PlanTargetInput input, PlanAssistant assistant) => Edited(assistant, assistant.Pick(input.Id)));
        plan.MapPost("/remove", (PlanTargetInput input, PlanAssistant assistant) => Edited(assistant, assistant.Remove(input.Id)));
        plan.MapPost("/settings", (PlanSettingsInput input, PlanAssistant assistant) =>
            Edited(assistant, assistant.ChangeSettings(input.Filter, input.Iso, input.Exposure, input.TotalMinutes)));
        // 시작점 옮기기: 일몰부터의 분. null이면 기본(30° 위가 되는 때)으로
        plan.MapPost("/start", (PlanStartInput input, PlanAssistant assistant) => Edited(assistant, assistant.SetStart(input.Id, input.Minute)));

        // 사용자 말 하나 → 글 조각·계획 변화·선택지를 차례로 (Server-Sent Events)
        plan.MapPost("/chat", (ChatInput input, PlanAssistant assistant, CancellationToken ct) =>
            TypedResults.ServerSentEvents(assistant.SendAsync(input.Text, ct)));

        plan.MapPost("/reset", (PlanAssistant assistant) => { assistant.Reset(); return Results.NoContent(); });

        // "이 계획으로 준비 시작": 계획을 실행 값으로 확정한다 (CX-PLAN-07). 실행할 수 없는 계획이면 이유
        plan.MapPost("/confirm", (PlanAssistant assistant) =>
            assistant.Confirm() is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(assistant.Confirmed));
        // [임시] 건너뛰기 (2026-10-14): 대상이 없으면 오늘 밤 찍을 수 있는 대표 대상 하나를 골라 확정
        plan.MapPost("/temp-skip", (PlanAssistant assistant) =>
            assistant.TempSkip() is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(assistant.Confirmed));
        // 준비 단계가 읽는 확정 계획
        plan.MapGet("/confirmed", (PlanAssistant assistant) =>
            assistant.Confirmed is { } p ? Results.Ok(p) : Results.NotFound());
    }

    /// <summary>고도를 모르면(화면이 안 보냄) 먼저 조회한 뒤 적용</summary>
    private static async IAsyncEnumerable<Astro.Core.Setup.CheckResult> ApplySite(SiteService sites, double lat, double lon, double? elev,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var elevation = elev ?? await sites.ElevationAsync(lat, lon, ct);
        await foreach (var step in sites.ApplyAsync(lat, lon, elevation, ct)) yield return step;
    }

    private static object ScopeView(Scope s) => new
    {
        s.Id, s.Name, s.FocalLength, s.Aperture, s.FocalRatio, s.Reducer, s.Flattener,
        effectiveFocalLength = Math.Round(s.EffectiveFocalLength, 1),
        effectiveFocalRatio = Math.Round(s.EffectiveFocalRatio, 2),
        s.Optics,
    };

    private sealed record ChatInput(string Text);
    private sealed record PlanTargetInput(string Id);
    private sealed record PlanSettingsInput(string? Filter, int? Iso, int? Exposure, int? TotalMinutes);
    private sealed record PlanStartInput(string Id, double? Minute);

    private sealed record RigSelect(string Kind, string? DeviceId, string? Name);
    private sealed record CameraPowerChoice(string? Outlet);
    private sealed record FramingInput(double TurnDeg);
    private sealed record ClientLog(string? Message);
    private sealed record PowerInput(Dictionary<string, string?> Sources);

    private sealed record UpdateDismiss(string Id, string Version);

    private sealed record ShootAnswer(string Choice);

    private sealed record SessionPhase(string Phase, string? ProfileId);

    /// <summary>
    /// 그날 밤 진행 기록 (Session/NightSession). 화면이 단계를 알리고(phase), 장비 연결 뒤 이어서 할지 묻는다(pending → resume / dismiss)
    /// </summary>
    private static void MapSession(RouteGroupBuilder session)
    {
        session.MapPost("/phase", (SessionPhase body, Session.NightSessionStore store, PlanAssistant planner, Prepare.Flow.PrepareFlow flow, Prepare.Flow.IPrepMemory memory) =>
        {
            store.SetPhase(body.Phase, body.ProfileId);
            // 단계가 바뀔 때 바로 한 번 (요약에 닿으면 그 밤은 끝으로)
            if (store.Recording) store.Save(planner, flow, memory);
            return Results.NoContent();
        });
        session.MapGet("/pending", (string? profileId, Session.NightSessionStore store) =>
            store.Pending(profileId) is { } p ? Results.Ok(p) : Results.NoContent());
        // 이어서: ① 장비 멈춤 확인(노출·자동초점·적도의 이동) ② 기록·계획 되살림 ③ 끝낸 작업을 다시 확인해 건너뛰고 갈 화면을 정함
        session.MapPost("/resume", async (Session.NightSessionStore store, PlanAssistant planner, Prepare.Flow.PrepareFlow flow, Prepare.Flow.IPrepMemory memory, Prepare.PrepareStarter starter, CancellationToken ct) =>
        {
            if (await starter.StopForResumeAsync(ct) is { } problem) return Results.Conflict(new { error = problem });
            if (await store.RestoreAsync(planner, flow.ResultsFor(Sky.TonightService.EveningOf(DateTimeOffset.Now)), memory, ct) is not { } s)
                return Results.Conflict(new { error = "이어서 할 기록이 없습니다." });
            var phase = await starter.ResumeAsync(s.Phase, (s.Done ?? []).ToHashSet(), ct);
            return Results.Ok(new { phase });
        });
        session.MapPost("/dismiss", (Session.NightSessionStore store) =>
        {
            store.Dismiss();
            return Results.NoContent();
        });
    }

    private static void MapProfiles(RouteGroupBuilder profiles)
    {
        profiles.MapGet("/", (ProfileStore store) => store.List());

        profiles.MapPost("/", (NewProfile input, ProfileStore store) =>
        {
            var (profile, error) = store.Create(input);
            return profile is null
                ? Results.BadRequest(new { error })
                : Results.Created($"/api/profiles/{profile.Id}", profile);
        });

        profiles.MapPost("/{id}/select", (string id, ProfileStore store, PlanAssistant plan) =>
        {
            if (!store.Select(id)) return Results.NotFound();
            plan.ProfileSelected(id); // 다른 프로필이면 계획 대화를 새로 (CX-PLAN-06)
            return Results.NoContent();
        });

        // 프로필 삭제 (편집 화면, 확인 창 뒤)
        profiles.MapDelete("/{id}", (string id, ProfileStore store) => store.Delete(id) ? Results.NoContent() : Results.NotFound());

        // 별명·메모 고치기 (프로필 선택 화면)
        profiles.MapPut("/{id}", (string id, NewProfile input, ProfileStore store) =>
        {
            var (profile, error) = store.Edit(id, input);
            return profile is null ? Results.BadRequest(new { error }) : Results.Ok(profile);
        });

        // 관측지 추가(고도를 안 보내면 좌표로 조회) · 삭제. 바뀐 프로필을 돌려준다
        profiles.MapPost("/{id}/sites", async (string id, NewSite input, ProfileStore store, SiteService sites, CancellationToken ct) =>
        {
            var elevation = input.Elevation ?? await sites.ElevationAsync(input.Latitude, input.Longitude, ct);
            var (profile, error) = store.AddSite(id, input.Name ?? "", input.Latitude, input.Longitude, elevation);
            return profile is null ? Results.BadRequest(new { error }) : Results.Ok(profile);
        });
        profiles.MapPut("/{id}/sites/{siteId}", (string id, string siteId, NewSite input, ProfileStore store) =>
        {
            var (profile, error) = store.RenameSite(id, siteId, input.Name ?? "");
            return profile is null ? Results.BadRequest(new { error }) : Results.Ok(profile);
        });
        profiles.MapDelete("/{id}/sites/{siteId}",(string id, string siteId, ProfileStore store) =>
            store.RemoveSite(id, siteId) is { } profile ? Results.Ok(profile) : Results.NotFound());

        // 전원 배선 (docs/implementation/POWER_WIRING.md): 모든 장비 종류를 채운 표 + 직접 설정했는지. 설정하지 않았으면 기본값(적도의 먼저)
        profiles.MapGet("/{id}/power", (string id, ProfileStore store) =>
            store.Get(id) is { } p ? Results.Ok(new { sources = (p.Power ?? PowerWiring.Default).Full(), custom = p.Power is not null }) : Results.NotFound());
        profiles.MapPut("/{id}/power", (string id, PowerInput input, ProfileStore store) =>
        {
            if (PowerWiring.Validate(input.Sources) is { } error) return Results.BadRequest(new { error });
            return store.SetPower(id, new PowerWiring(new Dictionary<string, string?>(input.Sources))) ? Results.NoContent() : Results.NotFound();
        });

        // 화면이 가운데를 정사각형으로 잘라 256×256 WebP로 줄여서 보낸다.
        profiles.MapPut("/{id}/image", async (string id, HttpRequest request, ProfileStore store) =>
        {
            if (request.ContentType != "image/webp") return Results.BadRequest(new { error = "WebP 이미지만 받습니다." });
            if (request.ContentLength > ProfileStore.ImageMaxBytes) return Results.BadRequest(new { error = "이미지가 너무 큽니다." });
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            if (buffer.Length is 0 or > ProfileStore.ImageMaxBytes) return Results.BadRequest(new { error = "이미지를 읽지 못했습니다." });
            return store.SaveImage(id, buffer.ToArray()) ? Results.NoContent() : Results.NotFound();
        });

        profiles.MapGet("/{id}/image", (string id, ProfileStore store) =>
            store.ImageFile(id) is { } path ? Results.File(path, "image/webp") : Results.NotFound());
    }

    /// <summary>준비 작업이 진행 중인가 (종료 때 프로그램을 닫기 전에 본다)</summary>
    /// <summary>종료가 프로그램을 닫으면 안 되는 이유 (준비 작업·홈 이동이 돌고 있음). 없으면 null</summary>
    private static string? PrepareBusy(Prepare.Flow.PrepareFlow flow, Prepare.MountHome home)
    {
        if (new[] { flow.Rig, flow.Target, flow.Wrap }.Select(r => r.ActiveTaskTitle).FirstOrDefault(t => t is not null) is { } task)
            return $"{task} 작업이 아직 진행 중이라 프로그램을 닫지 않았습니다.";
        return home.Homing ? "적도의가 아직 홈으로 가는 중이라 프로그램을 닫지 않았습니다." : null;
    }
}
