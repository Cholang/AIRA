using System.Diagnostics;
using static Astro.Server.Engine.Win32Windows;

namespace Astro.Server.Engine;

/// <summary>
/// N.I.N.A. 프로세스 번호와 아이라 창 — 창 숨기기·창 지키기(BackgroundWindows)가 0.1~0.3초마다 묻는다. 프로세스 목록 만들기(PC의 모든 프로세스 정보)는 무거워서 2초에 한 번만
/// (2026-10-11 자체 점검 M4: 밤새 초당 10번 모든 프로세스를 훑어 노트북 배터리·CPU를 썼다). N.I.N.A.를 새로 켜면 2초 안에 알아챈다
/// </summary>
internal static class NinaProcesses
{
    private static readonly Lock Gate = new();
    private static HashSet<uint> _ids = [];
    private static nint _own;
    private static long _at = -10_000; // 처음 물을 때 바로 읽게

    /// <summary>지금 켜져 있는 N.I.N.A. 프로세스 번호 (읽기만 — 2초마다 새 묶음으로 바꾼다)</summary>
    public static HashSet<uint> Ids()
    {
        Refresh();
        lock (Gate) return _ids;
    }

    /// <summary>아이라 창 (없으면 0)</summary>
    public static nint OwnWindow()
    {
        Refresh();
        lock (Gate) return _own;
    }

    private static void Refresh()
    {
        var now = Environment.TickCount64;
        lock (Gate) if (now - _at < 2000) return;
        var ids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("NINA"))
            using (p)
                ids.Add((uint)p.Id);
        nint own;
        using (var me = Process.GetCurrentProcess()) own = me.MainWindowHandle;
        lock (Gate)
        {
            _ids = ids;
            _own = own;
            _at = now;
        }
    }
}

/// <summary>
/// N.I.N.A. 알림(토스트) 창 숨기기 (2026-10-09 사용자 요청 — 같은 내용을 아이라가 진단 카드로 보여 준다).
/// 알림은 N.I.N.A. 프로세스의 "NotificationsWindow" 창 하나에 쌓인다(2026-10-09 확인) — 그 창만 숨기고 다른 대화창은 건드리지 않는다.
/// 아이라 창이 앞에 있을 때만 숨긴다: 사용자가 N.I.N.A.로 가 있으면 N.I.N.A. 알림을 그대로 본다.
/// 그리고 최근 10초 안에 아이라가 알림을 만들었을 때만 숨긴다 — 로그에 없거나 해석하지 못한 N.I.N.A. 알림은 사라지지 않고 그대로 보인다 (Codex L09·L03)
/// </summary>
public sealed class NinaToastHider(NinaNotices notices) : BackgroundService
{
    // 숨긴 알림 창과 숨긴 시각 (5초 안에 아이라 알림이 없으면 로그 원문으로 아이라 알림 — 창은 돌려주지 않음)
    private nint _hidden;
    private DateTimeOffset _hiddenAt;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return;
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); } catch (Exception e) when (e is not OperationCanceledException) { /* 창·프로세스가 막 사라짐 등 — 다음에 다시 (H2) */ }
            try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private void Tick()
    {
        // 숨긴 뒤 5초 안에 아이라 알림이 없으면: 해석 못 한 오류·경고 로그 원문으로 아이라 알림을 만든다. 로그에 오류·경고가 없으면 N.I.N.A.의 안내성 알림
        // ("연결되었습니다" 등 — 로그에 오류로 남지 않음)이라 그대로 숨겨 둔다 (2026-10-10 사용자: 어떤 알림은 사라지고 어떤 건 남음 — 남은 것이 돌려준 안내 알림이었음).
        // 다시 보여 주지 않는다 — 돌려준 알림을 닫으면 N.I.N.A.가 아이라 위로 올라옴
        if (_hidden != 0 && DateTimeOffset.Now - _hiddenAt > TimeSpan.FromSeconds(5))
        {
            // 다른 알림이 있었다는 것은 이 창의 경고를 전했다는 증거가 아니다 — 아직 전하지 않은 해석 못 한 경고는 모두 전한다 (CX-20-10)
            notices.AddFallback(_hiddenAt - TimeSpan.FromSeconds(5));
            _hidden = 0;
        }
        var toast = ToastWindow();
        if (toast == 0) return;
        // 사용자가 N.I.N.A. 창을 보고 있을 때만 그대로 둔다 (아이라가 앞이 아니어도 — 다른 프로그램을 보는 중에도 숨김)
        if (IsNinaForeground()) return;
        ShowWindowAsync(toast, SwHide);
        _hidden = toast;
        _hiddenAt = DateTimeOffset.Now;
    }

    /// <summary>지금 맨 앞 창이 N.I.N.A.(알림 창 말고 본 창·대화 창)인가</summary>
    private static bool IsNinaForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == 0) return false;
        return NinaProcesses.Ids().Contains(Pid(fg));
    }

    /// <summary>보이는 N.I.N.A. 알림 창 (없으면 0)</summary>
    private static nint ToastWindow()
    {
        return Visible(NinaProcesses.Ids()).FirstOrDefault(h => Title(h) == "NotificationsWindow");
    }

    private const int SwShowNoActivate = 4;
    private const int SwHide = 0;
}

/// <summary>
/// N.I.N.A. 자동초점 창 숨기기 (2026-10-09 사용자 요청 — 아이라 하늘 화면이 N.I.N.A. 사진과 측정점을 대신 보여 준다).
/// 창 제목은 한국어 N.I.N.A.에서 "오토포커스"(본 창에 딸린 창, 자동초점이 끝나도 남음 — 실기 확인), 영어는 "Autofocus".
/// 아이라 창이 앞에 있을 때만 숨긴다 — 사용자가 N.I.N.A.로 가 있으면 그대로 보인다
/// </summary>
public sealed class NinaAutofocusWindowHider : BackgroundService
{
    private static readonly string[] Titles = ["오토포커스", "Autofocus", "Auto Focus", "AutoFocus"];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return;
        while (!ct.IsCancellationRequested)
        {
            try { HideOnce(); } catch (Exception e) when (e is not OperationCanceledException) { /* 다음에 다시 (H2) */ }
            try { await Task.Delay(300, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static void HideOnce()
    {
        var own = NinaProcesses.OwnWindow();
        if (own == 0 || GetForegroundWindow() != own) return;
        foreach (var h in Visible(NinaProcesses.Ids()))
            if (HasOwner(h) && Titles.Contains(Title(h).Trim(), StringComparer.OrdinalIgnoreCase)) ShowWindowAsync(h, 0);
    }
}
