using System.Text.Json.Serialization;
using Astro.Nina;
using Astro.Server.Assistant;
using Astro.Server.Engine;
using Astro.Server.Profiles;
using Astro.Server.Setup;
using Astro.Server.Sky;
using Microsoft.Extensions.Options;

namespace Astro.Server;

/// <summary>
/// 코어 서버. 단독 실행(Program.cs)과 데스크톱 창(Astro.Desktop) 안에서 똑같이 만들어 쓴다.
/// 화면(web/ 빌드 결과)은 wwwroot에서 제공한다.
/// </summary>
public static class AppServer
{
    public const string DefaultUrl = "http://localhost:5210";

    /// <param name="contentRoot">
    /// 설정 파일과 wwwroot를 찾을 폴더. 단독 실행(dotnet run)은 비워 두면 프로젝트 폴더를 쓰므로
    /// web/을 다시 빌드하면 바로 반영된다. 데스크톱 창은 자기 실행 폴더를 넘긴다.
    /// </param>
    public static WebApplication Build(string[] args, string? url = null, string? contentRoot = null)
    {
        Astro.Core.Product.MoveDataFromPreviousName(); // 개발 서버로 켤 때도 (데스크톱은 이미 옮겼으면 그대로)
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot,
        });
        if (url is not null) builder.WebHost.UseUrls(url);
        // 모든 단계 기록을 파일로 (logs/aira-날짜.log — 2026-10-10 사용자 요청, FileLogProvider)
        builder.Logging.AddProvider(new FileLogProvider());
        // AI API 키 등 비밀 값: 이 PC의 사용자 비밀 저장소에서 읽는다 (git·화면에 남지 않음).
        // 기본 설정은 개발 모드에서만 읽으므로 항상 읽도록 직접 추가. 환경 변수가 있으면 환경 변수가 이긴다.
        builder.Configuration.AddUserSecrets(typeof(AppServer).Assembly, optional: true);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.Configure<NinaOptions>(builder.Configuration.GetSection("Nina"));
        builder.Services.Configure<SetupOptions>(builder.Configuration.GetSection("Setup"));
        builder.Services.Configure<UpdateOptions>(builder.Configuration.GetSection("Updates"));
        builder.Services.Configure<EquipmentOptions>(builder.Configuration.GetSection("Equipment"));
        builder.Services.AddHttpClient<NinaApiClient>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NinaOptions>>().Value.BaseUrl);
            // 요청별 대기 시간은 부르는 쪽이 정한다 (조회 5초, 장비 연결 60초, 사진 받기·반전·가이딩 시작 3분 — NinaRig.Long). 여기는 그보다 넉넉한 안전선.
            // 예전 90초는 3분짜리 요청을 90초에 잘랐다 (2026-10-11 자체 점검 L3)
            http.Timeout = NinaApiClient.MaxRequestTimeout + TimeSpan.FromSeconds(30);
        });
        builder.Services.Configure<NetworkOptions>(builder.Configuration.GetSection("Network"));
        builder.Services.AddHttpClient<InternetCheck>();
        builder.Services.AddTransient<SetupChecker>();
        // 새 버전 알림 (프로필 화면): 최신 버전은 하루 한 번만 받아 데이터 폴더에 기억
        builder.Services.AddSingleton(sp => new UpdateChecker(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IOptions<NinaOptions>>(), sp.GetRequiredService<IOptions<UpdateOptions>>(),
            sp.GetRequiredService<ILogger<UpdateChecker>>(), builder.Configuration["App:DataDir"]));
        builder.Services.AddTransient<EngineStarter>();
        builder.Services.AddSingleton<NinaWatcher>();
        builder.Services.AddTransient<EquipmentConnector>();
        builder.Services.AddSingleton<EquipmentRun>();
        builder.Services.AddSingleton<ProgramCloser>();
        builder.Services.AddSingleton<DevicePrecheck.IHostDevices, DevicePrecheck.WindowsHost>();
        builder.Services.AddSingleton<CameraPowerStore>();
        builder.Services.AddSingleton<EquipmentSimulation>();
        builder.Services.AddSingleton<EquipmentChoices>();
        builder.Services.AddSingleton(new RigOverrides(builder.Configuration["App:DataDir"]));
        builder.Services.AddSingleton<LiveDevices>();
        builder.Services.AddSingleton(new OpticsStore(builder.Configuration["App:DataDir"]));
        builder.Services.AddTransient<RigSetup>();

        // 촬영 계획: 대상 목록(N.I.N.A. 데이터베이스), 오늘 밤 정보, AI 비서 (회사는 Assistant:Provider로 고름)
        builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection("Assistant"));
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton(new DsoCatalog(builder.Configuration["Sky:NinaDatabase"] is { Length: > 0 } db ? db : null));
        builder.Services.AddSingleton<TonightService>();
        builder.Services.AddSingleton<PlanTools>();
        // 앱 전체 설정 (설정 화면 — AI 비서·프로그램 경로). 시험은 App:DataDir로 다른 폴더를 쓴다
        builder.Services.AddSingleton(_ => new Setup.UserSettings(builder.Configuration["App:DataDir"]));
        builder.Services.AddSingleton<ChatModelFactory>();
        builder.Services.AddSingleton<PlanAssistant>();
        // 촬영 준비: 작업별로 나눈 구조, 지금은 모의 장비만 (docs/implementation/PREPARE.md — 실제 장비는 P3)
        // 장비 연결이 실제(Equipment:Simulate=false)면 준비 단계도 실제 장비 (P3, 2026-10-06)
        Prepare.PrepareSetup.AddPrepare(builder.Services, simulate: builder.Configuration.GetValue("Equipment:Simulate", true));
        // 데이터 폴더(기본 %LOCALAPPDATA%\<product.json의 dataFolder>)는 설정 App:DataDir로 바꿀 수 있다 (테스트용).
        builder.Services.AddSingleton(new ProfileStore(builder.Configuration["App:DataDir"]));
        builder.Services.AddSingleton<PowerWiringStore>();
        // 그날 밤 진행 기록: 중간에 꺼져도 다시 켜면 이어서 할지 묻는다 (2026-10-08)
        builder.Services.AddSingleton(new Session.NightSessionStore(builder.Configuration["App:DataDir"]));
        // 백그라운드 서비스(기록·N.I.N.A. 로그·창 숨기기)의 예외 하나가 서버 전체를 멈추지 않게 (.NET 기본은 서버 정지 — 2026-10-11 자체 점검 H2).
        // 각 서비스는 예외를 기록하고 계속 돈다
        builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
        builder.Services.AddHostedService<Session.NightSessionRecorder>();
        // N.I.N.A. 오류를 아이라 말로 (로그를 따라 읽음) · N.I.N.A. 알림 창은 아이라가 앞에 있을 때 숨김
        builder.Services.AddSingleton<NinaNotices>();
        builder.Services.AddHostedService<NinaLogWatcher>();
        builder.Services.AddHostedService<NinaToastHider>();
        builder.Services.AddHostedService<NinaAutofocusWindowHider>();
        builder.Services.AddTransient<SiteService>();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        app.Services.GetRequiredService<Setup.UserSettings>(); // 처음에 읽어 둔다 — 지정한 N.I.N.A. 경로를 엔진 켜기 전에 쓰도록
        // 새 버전 확인은 앱이 켜지자마자 시작한다 — 프로필 화면이 열릴 때 결과가 준비돼 있게 (오늘 이미 받았으면 기억한 값)
        _ = Task.Run(() => app.Services.GetRequiredService<UpdateChecker>().CheckAsync(CancellationToken.None));
        // 서버를 멈출 때 끝나지 않는 실시간 연결(SSE, 화면의 EventSource)을 바로 끊는다 — 안 그러면 Kestrel이 이 연결들이 끝나길
        // 멈춤 한도(데스크톱 10초)까지 기다려, 프로그램을 다 닫은 뒤에도 아이라 창이 한참 늦게 닫혔다 (2026-10-10)
        var stopping = app.Lifetime.ApplicationStopping;
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Headers.Accept.ToString().Contains("text/event-stream")) { await next(ctx); return; }
            using var _ = stopping.Register(ctx.Abort);
            await next(ctx);
        });
        app.UseDefaultFiles();
        // index.html은 캐시하지 않는다: 웹을 다시 빌드해도 WebView2가 옛 index.html(옛 화면)을 띄우던 문제. assets는 이름에 해시가 있어 그대로 캐시
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = c =>
            {
                if (c.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    c.Context.Response.Headers.CacheControl = "no-cache";
            },
        });

        var api = app.MapGroup("/api");

        // 항목이 확인될 때마다 한 건씩 보낸다 (Server-Sent Events).
        api.MapGet("/setup/check", (SetupChecker checker, CancellationToken ct) =>
            TypedResults.ServerSentEvents(checker.RunAsync(ct), eventType: "check"));
        // N.I.N.A. 감시: 지금 상태를 먼저, 바뀔 때마다 (Running · Exited · NotResponding)
        // N.I.N.A. 오류·경고를 풀어 쓴 알림 (after보다 뒤의 것 — 화면이 2초마다 묻는다)
        api.MapGet("/nina/notices", (long? after, NinaNotices notices) => notices.Since(after ?? 0));
        api.MapGet("/nina/watch", (NinaWatcher watcher, CancellationToken ct) =>
            TypedResults.ServerSentEvents(watcher.WatchAsync(ct).Select(s => s.ToString()), eventType: "nina"));
        // 아이라 프로필 화면이 뜨면 N.I.N.A.·PHD2·Empire를 미리 켠다 (EngineStarter.PreLaunch)
        api.MapPost("/engine/prelaunch", (EngineStarter starter) =>
        {
            starter.PreLaunch();
            return Results.NoContent();
        });
        api.MapGet("/engine/start", (EngineStarter starter, CancellationToken ct) =>
            TypedResults.ServerSentEvents(starter.RunAsync(ct), eventType: "check"));

        // 전체를 한 번에 (프로필을 고른 직후: 모두 설치돼 있으면 0단계 화면을 건너뛴다)
        api.MapGet("/setup/status", (SetupChecker checker) => checker.CheckAll());

        // 새 버전 알림: 알릴 것만 (확인 실패면 빈 목록), 이 버전은 다시 알리지 않기
        api.MapGet("/updates", (UpdateChecker updates, CancellationToken ct) => updates.CheckAsync(ct));
        api.MapPost("/updates/dismiss", (UpdateDismiss body, UpdateChecker updates) =>
        {
            updates.Dismiss(body.Id, body.Version);
            return Results.NoContent();
        });

        // 한 항목만 다시 확인 (쉐브론 안의 새로고침)
        api.MapGet("/setup/check/{id}", (string id, SetupChecker checker) =>
            checker.CheckOne(id) is { } result ? Results.Ok(result) : Results.NotFound());

        // 장비 연결: 연결할 장비 목록(칸을 미리 그리기 용) → 차례로 연결 → 한 장비만 다시 연결
        api.MapGet("/equipment/plan", (EquipmentConnector connector, CancellationToken ct) => connector.PlanAsync(ct));
        api.MapGet("/equipment/connect", (EquipmentConnector connector, CancellationToken ct) =>
            TypedResults.ServerSentEvents(connector.RunAsync(ct), eventType: "check"));
        // 출발 전 점검을 마침 ("포커서 0점" 확인) → 다음 초점 작업이 포커서 0점을 잡는다
        // 늘 잡는다 (2026-10-12 사용자 — 포커서는 늘 떼어 보관하고 노브를 돌리면 드로튜브가 움직여, "지난번 0에 두고 끝냄"을 믿을 수 없다)
        api.MapPost("/prepare/focuser-zero", (Prepare.Tasks.Focus.FocuserZeroRequest zero) =>
        {
            zero.Pending = true;
            return Results.NoContent();
        });
        // 아이라 종료 마지막: 아이라가 켠 N.I.N.A.면 PHD2와 함께 닫는다 (촬영 중이면 두기). 사용자가 켜 둔 N.I.N.A.는 그대로
        // 아이라 종료: 닫을 프로그램 목록(아이라가 N.I.N.A.를 켰고 촬영 중이 아닐 때만) → 화면이 하나씩 닫으며 체크 (2026-10-10)
        // 준비 작업이 돌고 있으면 닫지 않는다 — 종료 화면이 먼저 /prepare/stop-for-quit로 멈춘다 (CX-20-02)
        // 작업이 아직 돌고 있으면 빈 목록이 아니라 held(이유) — 화면이 "닫을 것 없음"으로 알고 바로 꺼지지 않게 (CX-21-02)
        api.MapGet("/engine/close", (ProgramCloser programs, Shoot.ShootSession shoot, Prepare.Flow.PrepareFlow flow, Prepare.MountHome home) =>
            PrepareBusy(flow, home) is { } held
                ? Results.Ok(new { apps = Array.Empty<ProgramCloser.Target>(), held })
                : Results.Ok(new { apps = programs.LaunchedNina && !shoot.Active ? programs.Targets() : [], held = (string?)null }));
        api.MapPost("/engine/close/{id}", async (string id, ProgramCloser programs, Shoot.ShootSession shoot, Prepare.Flow.PrepareFlow flow, Prepare.MountHome home, CancellationToken ct) =>
            Results.Ok(new { closed = programs.LaunchedNina && !shoot.Active && PrepareBusy(flow, home) is null && await programs.CloseOneAsync(id, ct) }));
        // 아이라 종료 전: 진행 중인 준비 작업과 계속 도는 가이딩을 멈추고 확인 (CX-20-02). 촬영 중이면 그대로 — 촬영은 N.I.N.A.가 이어 간다
        // 홈 버튼으로 시작한 홈 이동도 멈추고 확인한다 (CX-21-02)
        api.MapPost("/prepare/stop-for-quit", async (Prepare.Flow.PrepareFlow flow, Prepare.MountHome home, Shoot.ShootSession shoot) =>
        {
            // 촬영 중이면 지금 사진이 끝나기를 기다려 촬영을 끝낸 뒤 평소 종료 절차로 (CX-30-01 — 아이라가 꺼지면 다음 사진·반전을 볼 주체가 없다.
            // X-T5는 일반 노출을 중단할 수 없어 끝까지 찍는다 — ShootSession.StopForQuitAsync)
            if (shoot.Active && !await shoot.StopForQuitAsync())
                return Results.Ok(new { stopped = false, shooting = false, problem = (string?)"촬영이 끝나기를 기다렸지만 끝나지 않았거나, 가이딩·적도의가 멈췄는지 확인하지 못했습니다. N.I.N.A.·PHD2와 적도의를 직접 확인해 주세요." });
            foreach (var runner in new[] { flow.Rig, flow.Target, flow.Wrap })
                if (!await runner.StopForQuitAsync()) return Results.Ok(new { stopped = false, shooting = false, problem = (string?)null });
            if (!await home.StopForQuitAsync())
                return Results.Ok(new { stopped = false, shooting = false, problem = (string?)"적도의 홈 이동을 멈췄는지 확인하지 못했습니다. 적도의가 멈춰 있는지 직접 확인해 주세요." });
            return Results.Ok(new { stopped = true, shooting = false, problem = (string?)null });
        });
        // 화면의 단계 이동·버튼 기록 (같은 로그 파일에 "화면"으로)
        api.MapPost("/log", (ClientLog input) =>
        {
            FileLogProvider.Write("INFO", "화면", (input.Message ?? "").Length > 500 ? input.Message![..500] : input.Message ?? "");
            return Results.NoContent();
        });
        // 아이라 종료 전에 포커서를 0으로 보내기 (못 하면 이유, 종료는 그대로)
        api.MapPost("/focuser/park", async (Prepare.FocuserPark park, CancellationToken ct) => Results.Ok(new { problem = await park.ParkAsync(ct) }));
        // 진행 표시 아래 "적도의 홈": 지금 보낼 수 있는지(이유) · 보내기
        api.MapGet("/mount/home", async (Prepare.MountHome home, CancellationToken ct) => Results.Ok(await home.StateAsync(ct)));
        // 홈이 실패해 적도의가 멈췄는지도 확인하지 못했을 때: 사용자가 직접 확인했다고 알림 (CX-20-03)
        // 아이라 종료 전: 적도의 추적 끄기 (촬영 중·미연결이면 할 일 없음). ok=false면 끄지 못함
        api.MapPost("/mount/tracking-off-for-quit", async (Prepare.MountHome home, CancellationToken ct) =>
            Results.Ok(new { ok = await home.TrackingOffForQuitAsync(ct) != false }));
        api.MapPost("/mount/home/confirm", (Prepare.MountHome home) => { home.Confirm(); return Results.NoContent(); });
        api.MapPost("/mount/home", async (Prepare.MountHome home, CancellationToken ct) =>
            await home.StartAsync(ct) is { } why ? Results.Conflict(new { error = why }) : Results.Accepted());
        // 카메라 전원: 배터리(outlet=null) 또는 전원 허브 출력. 고를 수 있는 출력은 허브가 연결돼 있을 때의 쓰기 가능한 출력들
        api.MapGet("/equipment/camera-power", async (CameraPowerStore power, EquipmentConnector connector, CancellationToken ct) =>
            Results.Ok(new { outlet = power.Outlet, outlets = await connector.HubOutputsAsync(ct) }));
        // 출력을 고르는 것은 화면이 "Empire에서 그 출력 전압을 카메라 어댑터에 맞췄나요?"를 확인받은 뒤에만
        api.MapPost("/equipment/camera-power", (CameraPowerChoice input, CameraPowerStore power) =>
        {
            power.Set(string.IsNullOrWhiteSpace(input.Outlet) ? null : input.Outlet);
            return Results.Ok(new { outlet = power.Outlet });
        });
        // 종료 전: 장비 연결 중이면 멈추고 이번에 연결하던 장비를 끊은 것을 확인 (끊지 못한 장비 이름 목록 — 비면 정상)
        // 촬영 중이면 끊지 않는다 — 촬영은 N.I.N.A.가 이어 간다 (답을 못 받은 연결 요청이 남아 있어도, CX-24-02)
        api.MapPost("/equipment/abort", async (EquipmentConnector connector, Shoot.ShootSession shoot, CancellationToken ct) =>
            Results.Ok(new { notDisconnected = shoot.Active ? [] : await connector.AbortAsync(ct) }));
        // 장비 연결이 모의인가 — 화면의 [임시] 모의 전용 버튼을 실장비에서 숨기려고
        api.MapGet("/equipment/simulated", (IOptions<EquipmentOptions> eq) => Results.Ok(new { simulated = eq.Value.Simulate }));
        // simulateFail=true: [임시] 화면 설계용 실패 만들기 (Equipment:Simulate가 켜져 있을 때만 동작)
        api.MapGet("/equipment/connect/{id}", async (string id, bool? simulateFail, EquipmentConnector connector, CancellationToken ct) =>
            await connector.RetryAsync(id, simulateFail ?? false, ct) is { } result ? Results.Ok(result) : Results.NotFound());

        // 장비 변경 모드: 설치된 드라이버 목록, 장비 고르기·제거 (deviceId가 null이면 제거)
        api.MapGet("/equipment/devices/{kind}", async (string kind, RigSetup rig, CancellationToken ct) =>
            await rig.DevicesAsync(kind, ct) is { } list ? Results.Ok(list) : Results.NotFound());
        api.MapPost("/equipment/select", async (RigSelect input, RigSetup rig, CancellationToken ct) =>
            await rig.SelectAsync(input.Kind, input.DeviceId, input.Name, ct) is { } result ? Results.Ok(result) : Results.BadRequest());

        // 망원경 목록 (장비 변경 모드의 망원경 원). 고르기는 /equipment/select (kind=scope)
        api.MapGet("/optics", (OpticsStore optics) =>
        {
            var (scopes, currentId) = optics.List();
            return new { scopes = scopes.Select(ScopeView), currentId, max = OpticsStore.Max };
        });
        api.MapPost("/optics", (NewScope input, OpticsStore optics) =>
        {
            var (scope, error) = optics.Add(input);
            return scope is null ? Results.BadRequest(new { error }) : Results.Ok(ScopeView(scope));
        });
        api.MapPut("/optics/{id}", (string id, NewScope input, OpticsStore optics) =>
        {
            var (scope, error) = optics.Edit(id, input);
            return scope is null ? Results.BadRequest(new { error }) : Results.Ok(ScopeView(scope));
        });
        api.MapDelete("/optics/{id}", (string id, OpticsStore optics) =>
            optics.Remove(id) is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

        // 준필수 장비(포커서·가이더)를 "없이 진행"
        api.MapPost("/equipment/skip/{id}", async (string id, EquipmentConnector connector, CancellationToken ct) =>
            await connector.GoWithoutAsync(id, ct) is { } result ? Results.Ok(result) : Results.NotFound());

        // 관측지: 지금 N.I.N.A. 값 / 적용(N.I.N.A. 저장 → 적도의에 보내기, 단계별) / 좌표의 고도
        api.MapGet("/site/current", async (SiteService sites, CancellationToken ct) =>
            await sites.CurrentAsync(ct) is { } site ? Results.Ok(site) : Results.NoContent());
        api.MapGet("/site/apply", (double lat, double lon, double? elev, SiteService sites, CancellationToken ct) =>
            TypedResults.ServerSentEvents(ApplySite(sites, lat, lon, elev, ct), eventType: "check"));
        api.MapGet("/site/elevation", async (double lat, double lon, SiteService sites, CancellationToken ct) =>
            Results.Ok(new { elevation = await sites.ElevationAsync(lat, lon, ct) }));

        // 화면 설정: 카카오맵 JavaScript 키 (사용자 비밀 저장소 Map:KakaoJavaScriptKey. 없으면 지도 없이)
        api.MapGet("/config", (IConfiguration config) => new { kakaoKey = config["Map:KakaoJavaScriptKey"] is { Length: > 0 } k ? k : null });

        MapPlan(api.MapGroup("/plan"));
        MapPrepare(api.MapGroup("/prepare"));
        MapSettings(api.MapGroup("/settings"));
        MapShoot(api.MapGroup("/shoot"), api.MapGroup("/night"));
        MapProfiles(api.MapGroup("/profiles"));
        MapSession(api.MapGroup("/session"));

        app.MapFallbackToFile("index.html");
        return app;
    }

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
        // 중단 (진행 표시 아래) — 하던 작업을 멈추고 정지를 확인
        prepare.MapPost("/{group}/abort", async (string group, Prepare.Flow.PrepareFlow flow) =>
            flow.Runner(group) is not { } r ? Results.NotFound()
            : await r.AbortAsync() ? Results.NoContent() : Results.Conflict(new { error = "장비가 멈췄는지 확인하지 못했습니다. 장비 상태를 확인해 주세요." }));

        // [임시] 건너뛰기 — 극축 정렬·캘리브레이션을 바로 넘김 (2026-10-10, PrepareRunner.TempSkipAsync — 지울 때 같이)
        prepare.MapPost("/{group}/temp-skip", async (string group, Prepare.Flow.PrepareFlow flow) =>
            flow.Runner(group) is not { } r ? Results.NotFound()
            : await r.TempSkipAsync() is { } problem ? Results.BadRequest(new { error = problem }) : Results.NoContent());

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
            problem is not null ? Results.BadRequest(new { error = problem }) : Results.Ok(new { plan = a.View(), chart = a.Chart() });
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
