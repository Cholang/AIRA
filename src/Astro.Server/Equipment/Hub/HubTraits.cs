namespace Astro.Server.Equipment.Hub;

/// <summary>
/// 허브와 함께 쓰는 제조사 프로그램 (예: Wanderer Empire — N.I.N.A.가 허브 드라이버를 연결하면 저절로 켜지고, 출력 전압·열선 자동 제어를 맡는다).
/// Id는 아이라 종료 창의 프로그램 이름표(ProgramCloser), Process는 창·프로세스 이름, Servers는 함께 떠 있는 ASCOM 서버 프로세스
/// </summary>
public sealed record HubApp(
    string Id,
    string Name,
    string Process,
    IReadOnlyList<string> Servers,
    /// <summary>Program Files (x86) 아래 실행 파일 (미리 켜기)</summary>
    string ExeUnderProgramFilesX86,
    /// <summary>켜질 때 "항상 위"로 뜨는 시작 로고 창 제목 (아이라 뒤로 보낼 때). 없으면 null</summary>
    string? SplashTitle)
{
    /// <summary>닫을 때·켜졌는지 볼 때 보는 프로세스 전부</summary>
    public IReadOnlyList<string> AllProcesses => [Process, .. Servers];
}

/// <summary>
/// 전원 허브 특성 (장비 › 전원 허브의 틀, 2026-10-17). 아이라가 N.I.N.A.의 스위치 장치 이름·드라이버로 고른다(<see cref="HubCatalog.Match"/>).
/// 출력 이름·전압 칸처럼 N.I.N.A.가 알려 주는 것은 여기 두지 않는다 — 여기는 제조사마다 다른 것만
/// </summary>
public sealed record HubTraits(
    string Id,
    string Name,
    /// <summary>함께 쓰는 제조사 프로그램. 없으면 null</summary>
    HubApp? App,
    /// <summary>USB 제조사 번호 (연결 전 확인 — 꽂혀 있는지). null = 확인하지 않음</summary>
    string? UsbVendorId,
    /// <summary>기온·습도 센서의 ASCOM ObservingConditions 드라이버 Id. 없으면 null (이슬 여유를 모름)</summary>
    string? WeatherDriverId,
    /// <summary>열선(PWM 출력)이 제조사 프로그램의 자동 제어 중일 때 스위치 설명에 드는 말. 없으면 null (자동 여부를 모름)</summary>
    string? HeaterAutoMark) : IEquipmentModel;

/// <summary>전원 허브 카탈로그: 아이라가 아는 허브 모델. 모르는 허브는 기본 모델 <see cref="Generic"/> — 제조사 프로그램·센서 없음</summary>
public sealed class HubCatalog : IEquipmentCatalog<HubTraits>
{
    private HubCatalog() { }

    /// <summary>N.I.N.A.의 스위치 장치 칸 이름과 같다 (PowerWiring.Hub)</summary>
    public static string Kind => "switch";

    /// <summary>
    /// WandererBox (Plus V3 — 2026-10-08 실기): Wanderer Empire(시작 로고 "startup", ASCOM 서버 WandererBox1·2), CH340 USB(1A86),
    /// 환경 센서 드라이버(DHT22 — N.I.N.A.가 스위치를 연결한 채로도 동시에 읽힘), 열선 PWM 자동이면 설명에 "Automatic control in progress…"
    /// </summary>
    public static readonly HubTraits WandererBox = new("wandererbox", "WandererBox",
        new HubApp("empire", "Wanderer Empire", "Wanderer Empire", ["ASCOM.WandererBox1", "ASCOM.WandererBox2"], @"Wanderer Astro\Wanderer Empire.exe", "startup"),
        "1A86", "ASCOM.WandererBoxEnvironment.ObservingConditions", "Automatic control");

    /// <summary>기본 모델 (모르는 허브)</summary>
    public static readonly HubTraits Generic = new("generic", "모르는 전원 허브", null, null, null, null);

    public static IReadOnlyList<HubTraits> Models { get; } = [WandererBox, Generic];

    public static HubTraits Default => Generic;

    /// <summary>고르기: 이름·드라이버에 "Wanderer"가 있으면 WandererBox (예전 DevicePrecheck 기준)</summary>
    public static HubTraits Match(string? name, string? driver = null)
    {
        var all = (name ?? "") + " " + (driver ?? "");
        return all.Contains("Wanderer", StringComparison.OrdinalIgnoreCase) ? WandererBox : Generic;
    }

    /// <summary>아는 모든 허브의 제조사 프로그램 (창 숨기기·종료처럼 쓰지 않는 제조사의 것이 섞여도 해가 없는 곳에)</summary>
    public static IReadOnlyList<HubApp> AllApps { get; } = [.. Models.Select(m => m.App).OfType<HubApp>()];

    /// <summary>
    /// 지금 허브의 모델: 지금 장비(배운 값)로 고른다. 허브가 없거나(N.I.N.A. 프로필에 등록 안 됨) 아직 모르면 null — 없는 것으로 다룬다
    /// (제조사 프로그램 미리 켜기·날씨 센서·안내 문장 없음, 2026-10-17 사용자: "장비가 없을 때는 없는 걸로")
    /// </summary>
    public static HubTraits? Current(string? currentHub) => currentHub is null ? null : Match(currentHub);
}
