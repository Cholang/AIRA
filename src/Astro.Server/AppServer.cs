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
public static partial class AppServer
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
        // 카메라 내부 프로필: 지금 카메라와 카메라마다 써 보며 잰 값 (cameras.json)
        builder.Services.AddSingleton(new Camera.CameraProfiles(builder.Configuration["App:DataDir"]));
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
        // 준비 시간 기록 (이동~시험 사진, 프로필마다) — 계획의 준비 시간
        builder.Services.AddSingleton(new Prepare.PrepTimes(builder.Configuration["App:DataDir"]));
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
        // 2026-10-16 사용자: 누가 켰든 지금 떠 있는 것을 모두 보여 주고(처음엔 모두 체크) 고른 것만 닫는다. N.I.N.A.를 남기면 그 장비 연결을 먼저 끊는다.
        // 모의 장비로 돌 때는 닫지 않는다 — 시연 중 켜 둔 실제 프로그램을 건드리지 않게
        api.MapGet("/engine/programs", (ProgramCloser programs, Shoot.ShootSession shoot, Prepare.PrepareMode mode) =>
            Results.Ok(new { apps = shoot.Active || mode.Simulate ? [] : programs.Targets(onlyOurs: false) }));
        api.MapGet("/engine/close", (ProgramCloser programs, Shoot.ShootSession shoot, Prepare.Flow.PrepareFlow flow, Prepare.MountHome home, Prepare.PrepareMode mode) =>
            PrepareBusy(flow, home) is { } held
                ? Results.Ok(new { apps = Array.Empty<ProgramCloser.Target>(), held })
                : Results.Ok(new { apps = shoot.Active || mode.Simulate ? [] : programs.Targets(onlyOurs: false), held = (string?)null }));
        api.MapPost("/engine/close/{id}", async (string id, ProgramCloser programs, Shoot.ShootSession shoot, Prepare.Flow.PrepareFlow flow, Prepare.MountHome home, Prepare.PrepareMode mode, NinaApiClient nina, CancellationToken ct) =>
        {
            if (mode.Simulate || shoot.Active || PrepareBusy(flow, home) is not null) return Results.Ok(new { closed = false });
            var keepNina = id != "nina" && ProgramCloser.NinaRunning;
            if (keepNina) await ProgramCloser.DisconnectNinaAsync(nina, ct);
            return Results.Ok(new { closed = await programs.CloseOneAsync(id, ct, onlyOurs: false, ninaDisconnected: keepNina) });
        });
        // 아이라 종료 전: 진행 중인 준비 작업과 계속 도는 가이딩을 멈추고 확인 (CX-20-02). 촬영 중이면 그대로 — 촬영은 N.I.N.A.가 이어 간다
        // 홈 버튼으로 시작한 홈 이동도 멈추고 확인한다 (CX-21-02)
        api.MapPost("/prepare/stop-for-quit", async (Prepare.Flow.PrepareFlow flow, Prepare.MountHome home, Shoot.ShootSession shoot) =>
        {
            // 촬영 중이면 지금 사진이 끝나기를 기다려 촬영을 끝낸 뒤 평소 종료 절차로 (CX-30-01 — 아이라가 꺼지면 다음 사진·반전을 볼 주체가 없다.
            // X-T5는 일반 노출을 중단할 수 없어 끝까지 찍는다 — ShootSession.StopForQuitAsync)
            // 촬영이 끝났지만 적도의가 멈춘 것을 확인하지 못한 상태(반전 실패 등)면 그대로 끄지 않는다 (2026-10-14 자체 리뷰)
            if (!shoot.Active && shoot.MountUnconfirmed)
                return Results.Ok(new { stopped = false, shooting = false, problem = (string?)"촬영 중 적도의가 멈췄는지 확인하지 못한 채 끝났습니다. 촬영 화면에서 \"장비 상태 다시 확인\"을 누르거나 적도의를 직접 확인해 주세요." });
            if (shoot.Active && !await shoot.StopForQuitAsync())
                return Results.Ok(new { stopped = false, shooting = false, problem = (string?)"촬영이 끝나기를 기다렸지만 끝나지 않았거나, 가이드·적도의가 멈췄는지 확인하지 못했습니다. N.I.N.A.·PHD2와 적도의를 직접 확인해 주세요." });
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
        // "그래도 종료": 확인 없이 끄기 전에 가이딩 멈춤·추적 끄기를 한 번씩 시도 (끄지 못했으면 problem)
        api.MapPost("/quit/force-safe", async (Prepare.MountHome home) => Results.Ok(new { problem = await home.ForceSafeAsync() }));
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
}
