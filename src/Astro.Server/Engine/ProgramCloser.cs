using System.Diagnostics;

namespace Astro.Server.Engine;

/// <summary>
/// N.I.N.A.·PHD2·SharpCap·Wanderer Empire 닫기 — 마무리의 장비 정리와 아이라 종료가 함께 쓴다.
/// 아이라가 꺼진 뒤 N.I.N.A.만 남으면 아이라가 숨기던 N.I.N.A. 알림이 그대로 뜬다 (2026-10-09 — 종료 순간의 PHD2 오류 두 개).
/// 종료 창은 지금 떠 있는 프로그램(<see cref="Targets"/>)을 보여 주고 사용자가 고른 것을 하나씩 닫으며(<see cref="CloseOneAsync"/>) 체크한다 (2026-10-10·10-16 사용자 요청)
/// </summary>
public sealed class ProgramCloser(NinaWatcher watcher, ILogger<ProgramCloser> log)
{
    /// <summary>닫을 수 있는 프로그램: 화면에 보이는 이름</summary>
    public sealed record Target(string Id, string Name);

    /// <summary>
    /// 닫는 차례: N.I.N.A.를 먼저 (2026-10-10 기록: PHD2를 먼저 닫자 N.I.N.A.가 "PHD2 서버 연결이 끊어졌습니다" 오류 창을 띄웠고,
    /// 아이라가 꺼지는 중이라 사용자가 내용을 볼 수도 없었다) → PHD2 → SharpCap(극축 정렬 중에 끄면 남아 있을 수 있음) →
    /// 전원 허브의 제조사 프로그램(Wanderer Empire 등 — 장비 › 전원 허브, 그 ASCOM 서버까지 — N.I.N.A.가 닫힌 뒤에만)
    /// </summary>
    private static readonly (string Id, string Name, string[] Processes)[] Order =
    [
        ("nina", "N.I.N.A.", ["NINA"]),
        ("phd2", "PHD2", ["phd2"]),
        ("sharpcap", "SharpCap", ["SharpCap"]),
        .. Equipment.Hub.HubCatalog.AllApps.Select(a => (a.Id, a.Name, a.AllProcesses.ToArray())),
    ];

    /// <summary>허브 제조사 프로그램인가 (아이라가 켜진 뒤 켜진 것만 닫는 특별한 닫기)</summary>
    private static bool IsHubApp(string id) => Equipment.Hub.HubCatalog.AllApps.Any(a => a.Id == id);

    /// <summary>아이라(서버)가 켜진 시각 — 이 뒤에 켜진 Wanderer Empire는 아이라의 장비 연결이 켠 것</summary>
    private static readonly DateTime StartedAt = Process.GetCurrentProcess().StartTime;

    /// <summary>
    /// 지금 떠 있는 프로그램 (닫는 차례대로). onlyOurs면 아이라가 켜진 뒤에 켜진 것만.
    /// 아이라 종료는 누가 켰든 모두 보여 주고 사용자가 닫을 것을 고른다(처음엔 모두 체크 — 2026-10-16 사용자: 켜진 시각으로 "누가 켰는지" 짐작하면
    /// "그래도 종료" 뒤 다시 켰을 때 아이라가 켠 것도 남겼다)
    /// </summary>
    public IReadOnlyList<Target> Targets(bool onlyOurs = true) =>
        Order.Where(o => Running(o.Processes, onlyOurs)).Select(o => new Target(o.Id, o.Name)).ToList();

    /// <summary>N.I.N.A.가 떠 있나</summary>
    public static bool NinaRunning => Running(["NINA"], onlyOurs: false);

    /// <summary>
    /// N.I.N.A.를 남기고 나머지를 닫기 전: N.I.N.A.의 장비 연결을 모두 끊는다 — 연결된 채 PHD2·파워박스(Empire)가 사라지면 N.I.N.A.가 오류 창을 띄운다
    /// (2026-10-16 사용자 제안). 적도의는 마지막에(추적 끄기는 종료 절차가 먼저 함)
    /// </summary>
    /// <returns>
    /// 끊었는지 확인하지 못한 장비 종류 (끊은 뒤 연결 상태를 다시 읽어 "끊김"이 아니면 — 연결됨·읽지 못함). 비어 있어야 N.I.N.A.를 남기고 다른 프로그램을 닫아도 된다
    /// (CX-40-02: 끊기 요청의 결과를 보지 않고 넘겨, 못 끊었는데도 PHD2·Empire를 닫았다). 하나가 실패해도 나머지는 끊는다
    /// </returns>
    public static async Task<IReadOnlyList<string>> DisconnectNinaAsync(Astro.Nina.NinaApiClient nina, CancellationToken ct)
    {
        string[] kinds = ["guider", "camera", "focuser", "filterwheel", "rotator", "flatdevice", "switch", "mount"];
        foreach (var d in kinds)
            try { await nina.RequestAsync($"equipment/{d}/disconnect", TimeSpan.FromSeconds(10), ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
        var left = new List<string>();
        foreach (var d in kinds)
        {
            bool? connected;
            try { connected = await nina.ConnectedStateAsync(d, ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { connected = null; }
            if (connected != false) left.Add(d);
        }
        return left;
    }

    /// <summary>장비 종류의 화면 이름 (끊지 못한 장비 안내용)</summary>
    public static string KindNames(IEnumerable<string> kinds) => string.Join(", ", kinds.Select(k => k switch
    {
        "guider" => "가이더", "camera" => "카메라", "focuser" => "포커서", "filterwheel" => "필터휠", "rotator" => "회전장치",
        "flatdevice" => "플랫패널", "switch" => "전원 허브", "mount" => "적도의", _ => k,
    }));

    /// <summary>
    /// 프로그램 하나를 닫는다 (창을 닫게 하고 15초 기다림). 다 닫혔으면 true (저장 확인 창 등으로 안 닫히면 false).
    /// N.I.N.A.가 남아 있으면 PHD2·SharpCap·Empire는 닫지 않는다 — N.I.N.A.가 연결을 잃었다는 오류 창을 띄우고, 차례 보장이 깨진다 (CX-20-02).
    /// onlyOurs면 아이라가 켜진 뒤에 켜진 것만 (아이라 종료). 마무리의 장비 정리는 모두 닫는다(Empire만 아이라가 켠 것)
    /// </summary>
    public async Task<bool> CloseOneAsync(string id, CancellationToken ct, bool onlyOurs = true, bool ninaDisconnected = false)
    {
        var entry = Order.FirstOrDefault(o => o.Id == id);
        if (entry.Id is null) return false;
        if (id == "nina") watcher.ExpectExit(); // 정상 종료 — 화면에 "N.I.N.A.가 꺼졌어요"를 띄우지 않는다 (CX-APP-R4)
        else if (!ninaDisconnected && Running(["NINA"], onlyOurs: false)) return false; // 장비 연결을 끊은 N.I.N.A.는 남겨도 된다 (DisconnectNinaAsync)
        if (IsHubApp(id))
        {
            CloseHubAppStartedSince(onlyOurs ? StartedAt : DateTime.MinValue, entry.Processes);
            return !Running(entry.Processes, onlyOurs);
        }
        var all = true;
        foreach (var name in entry.Processes)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (onlyOurs && StartedBefore(p)) continue; // 아이라보다 먼저 켜져 있던 것 (사용자가 켠 것)
                    p.CloseMainWindow();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { all = false; log.LogWarning("{Name}이 닫히지 않음 (저장 확인 창 등)", name); }
                }
                catch (InvalidOperationException) { /* 이미 닫힘 */ }
                finally { p.Dispose(); }
            }
        if (all) log.LogInformation("{Name}을 닫음", entry.Name);
        return all;
    }

    /// <summary>
    /// N.I.N.A. → PHD2 → SharpCap을 닫고, 다 닫혔으면 아이라가 켠 Empire까지. 다 닫혔으면 true (마무리의 장비 정리)
    /// </summary>
    public async Task<bool> CloseAsync(CancellationToken ct)
    {
        var all = true;
        watcher.ExpectExit();
        foreach (var id in new[] { "nina", "phd2", "sharpcap" })
            all &= await CloseOneAsync(id, ct, onlyOurs: false);
        if (all)
            foreach (var app in Equipment.Hub.HubCatalog.AllApps) await CloseOneAsync(app.Id, ct);
        return all;
    }

    private static bool Running(string[] processes, bool onlyOurs)
    {
        foreach (var name in processes)
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                {
                    try
                    {
                        if (p.HasExited) continue;
                        if (onlyOurs && StartedBefore(p)) continue;
                        return true;
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
        return false;
    }

    /// <summary>아이라(서버)보다 먼저 켜져 있던 프로세스 — 사용자가 켠 것으로 본다. 시각을 못 읽으면 먼저 켜진 것으로 (닫지 않는 쪽)</summary>
    private static bool StartedBefore(Process p)
    {
        try { return p.StartTime < StartedAt; }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
    }

    /// <summary>
    /// 아이라가 켜진 뒤에 켜진 Wanderer Empire(와 그 ASCOM 서버)를 닫는다 (2026-10-10 사용자 요청 — 아이라가 켰으면 끝낼 때도).
    /// 그 전부터 켜져 있던 Empire(사용자가 켜 둔 것)는 그대로. 파워박스 출력의 켜짐·꺼짐은 장치에 남는다
    /// </summary>
    private void CloseHubAppStartedSince(DateTime since, string[] processes)
    {
        foreach (var name in processes)
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                {
                    try
                    {
                        if (p.StartTime < since) continue;
                        if (!p.CloseMainWindow() || !p.WaitForExit(5000)) p.Kill();
                        p.WaitForExit(5000);
                        log.LogInformation("{Name}을 닫음 (아이라가 켠 것)", name);
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
    }
}
