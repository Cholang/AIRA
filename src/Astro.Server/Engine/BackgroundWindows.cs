using System.Diagnostics;
using System.Runtime.InteropServices;
using static Astro.Server.Engine.Win32Windows;

namespace Astro.Server.Engine;

/// <summary>
/// AA가 다른 프로그램을 켜거나(N.I.N.A.) 그 프로그램이 장비 연결 중에 저절로 켜질 때(PHD2·Wanderer Empire),
/// 그 창이 AA 위로 올라와 Alt+Tab을 해야 하는 일이 없게 한다 (2026-10-06 사용자 요청).
/// 2026-10-08 사용자 결정: 새 창을 최소화하면 한 번 보였다 사라져(N.I.N.A. 로고·PHD2 최대화) 오히려 문제처럼 보임 →
/// 지켜보는 동안 AA 창을 잠깐 "항상 위"로 두어 새 창이 AA 뒤에서 열리게 하고, 끝나면 되돌린다. 최소화하지 않는다.
/// 새 창이 포커스를 가져가면 AA로 되돌린다. 시작 옵션의 "최소화"는 WPF 프로그램(N.I.N.A.)이 무시한다.
/// 지켜보는 때만: 그 뒤에 사용자가 직접 연 창은 건드리지 않는다. 지켜보는 프로그램 목록에 없는 창(ASCOM 드라이버 설정 창 등)도 건드리지 않는다.
/// 주의: SharpCap은 최소화하면 창 캡처(PrintWindow)가 비므로 여기 넣지 않는다 (준비 ① 실장비 어댑터에서 따로).
/// </summary>
public static class BackgroundWindows
{
    public const string Nina = "NINA";
    public const string Phd2 = "phd2";
    /// <summary>전원 허브의 제조사 프로그램 창 이름 (장비 › 전원 허브 — 아는 모든 허브: 쓰지 않는 제조사의 것이 섞여도 그 창이 없으면 아무 일 없다)</summary>
    public static string[] HubApps => [.. Equipment.Hub.HubCatalog.AllApps.Select(a => a.Process)];
    /// <summary>
    /// 지켜볼 전원 허브 창: 제조사 프로그램과 그 프로그램이 함께 띄우는 ASCOM 서버(WandererBox1·2 등) — 2026-10-18 실기: 프로필 화면에서
    /// ASCOM 서버 창(완더러박스 아이콘)이 아이라 위로 떴다. 프로그램만 지켜봐서 빠져 있었다
    /// </summary>
    public static string[] HubWindows => [.. Equipment.Hub.HubCatalog.AllApps.SelectMany(a => a.AllProcesses)];

    /// <summary>장비 연결 중 저절로 켜지는 프로그램: 가이더 → PHD2, 전원 허브 → 그 허브의 제조사 프로그램(Wanderer Empire 등). 없으면 null</summary>
    public static IDisposable? ForConnect(string kind) => kind switch
    {
        "guider" => Watch(TimeSpan.FromSeconds(5), Phd2),
        "switch" => Watch(TimeSpan.FromSeconds(5), HubWindows),
        _ => null,
    };

    /// <summary>
    /// 최소화하지 않고 AA 뒤로만 보낸다 (SharpCap — 최소화하면 창 캡처가 빈다). 지켜보는 동안 새 창을 맨 아래로 보내고 AA 창을 앞으로.
    /// AA를 잠깐 "항상 위"로 두어 새 창이 처음부터 AA 뒤에서 열리게 한다 (2026-10-11 실기: SharpCap 창이 하얗게 한 번 위에 떴다가 뒤로 갔다 — N.I.N.A.·PHD2와 같은 방식으로)
    /// </summary>
    public static IDisposable KeepBehind(TimeSpan grace, params string[] processNames) => new Watcher(processNames, grace, minimize: false, onTop: true);

    /// <summary>지금부터 AA를 "항상 위"로 두고 이 프로그램들의 새 창이 포커스를 가져가면 AA로 되돌린다. Dispose 뒤에도 grace 동안 더 (조금 늦게 뜨는 창)</summary>
    public static IDisposable Watch(TimeSpan grace, params string[] processNames) => new Watcher(processNames, grace, minimize: false, onTop: true);

    // AA "항상 위"는 지켜보기가 여럿 겹칠 수 있어(N.I.N.A. 켜기 + 가이더 연결) 마지막 하나가 끝날 때 푼다
    private static readonly Lock TopGate = new();
    private static int _topCount;

    private static void HoldTop(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (TopGate)
        {
            _topCount += on ? 1 : -1;
            if (on ? _topCount != 1 : _topCount != 0) return;
            if (NinaProcesses.OwnWindow() is var own and not 0)
                SetWindowPos(own, on ? HwndTopmost : HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    private sealed class Watcher : IDisposable
    {
        private static readonly TimeSpan MaxWatch = TimeSpan.FromMinutes(3); // 안전선: Dispose를 잊어도 끝난다
        private readonly CancellationTokenSource _stop = new();
        private readonly TimeSpan _grace;

        private readonly bool _onTop;
        private int _released;

        public Watcher(string[] names, TimeSpan grace, bool minimize, bool onTop = false)
        {
            _grace = grace;
            _onTop = onTop;
            if (!OperatingSystem.IsWindows()) return;
            // 사용자가 방금(2분 안) 직접 창을 바꿨으면 다시 앞을 차지하지 않는다 (다음 장비 연결이 아이라를 또 끌어올리지 않게)
            if (DateTime.UtcNow - _userSwitchedAt < TimeSpan.FromMinutes(2)) return;
            if (onTop) HoldTop(true);
            // 지켜보기 전부터 있던 창(사용자가 열어 둔 것)은 건드리지 않는다
            var before = Windows(names).ToHashSet();
            _stop.CancelAfter(MaxWatch);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        // 사용자가 Alt(Alt+Tab)·Windows 키로 직접 창을 바꾸면 지키기를 그만둔다 — 고른 창을 다시 끌어내리지 않게
                        // (2026-10-09 실기: 장비 연결 중 종료 확인 창에서 Alt+Tab을 눌러도 다른 창이 올라오지 않음)
                        if (UserSwitching())
                        {
                            _userSwitchedAt = DateTime.UtcNow;
                            break;
                        }
                        // 아이라 "항상 위"를 지켜보는 내내 확인한다 — 시작할 때 아이라 창을 아직 못 찾았으면(0) 아무것도 안 하고 지나가
                        // N.I.N.A.가 아이라를 덮었다 (2026-10-11 실기: 아이라가 켜질 때 미리 켜기). 창이 잡히는 대로 건다
                        if (_onTop && NinaProcesses.OwnWindow() is var me and not 0 && !IsTopmost(me))
                            SetWindowPos(me, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                        // 지켜보는 동안 다시 펼쳐지는 창(프로그램이 로딩 끝에 창을 다시 띄움)도 다시 내린다
                        foreach (var h in Windows(names))
                        {
                            if (before.Contains(h)) continue;
                            if (minimize)
                            {
                                if (!IsIconic(h)) ShowWindowAsync(h, SwShowMinNoActive);
                            }
                            else if (_onTop)
                            {
                                // 스스로 "항상 위"인 창(Wanderer Empire 시작 로고 등 — 2026-10-10 사용자: 장비 연결 중 화면을 가림)은 그 표시를 풀어
                                // 아이라("항상 위") 아래로 보낸다. 나머지 창은 그대로 두고 포커스만 아이라로
                                var own = NinaProcesses.OwnWindow(); // 2초마다 한 번만 새로 읽은 값 (자체 점검 L4 — 0.15초마다 프로세스 객체를 만들고 놓지 않았다)
                                // "항상 위"를 푸는 것은 확인된 시작 로고(제목 "startup")만 — 사용자가 연 다른 "항상 위" 창은 건드리지 않는다.
                                // 시작 로고는 원래대로("항상 위") 되돌리지 않는다 — 지키기가 끝날 때 아직 떠 있으면 아이라 위로 다시 올라와
                                // 로고가 두 번 뜬 것처럼 보였다 (2026-10-11 사용자 실행). 사용자 창은 애초에 건드리지 않는다 (CX-20-11)
                                if (IsTopmost(h) && IsSplash(h))
                                {
                                    SetWindowPos(h, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                                    if (own != 0) SetWindowPos(own, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                                }
                                if (GetForegroundWindow() == h && own != 0) SetForegroundWindow(own);
                            }
                            else if (GetForegroundWindow() == h)
                            {
                                SetWindowPos(h, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                                if (NinaProcesses.OwnWindow() is var own and not 0) SetForegroundWindow(own);
                            }
                        }
                        await Task.Delay(150, _stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                finally { Release(); }
            });
        }

        private void Release()
        {
            if (_onTop && Interlocked.Exchange(ref _released, 1) == 0) HoldTop(false);
        }

        public void Dispose() => _stop.CancelAfter(_grace);
    }

    /// <summary>Alt나 Windows 키가 눌려 있는가 (Alt+Tab·Win+Tab·작업 표시줄 — 사용자가 직접 창을 바꾸는 중)</summary>
    private static bool UserSwitching() =>
        (GetAsyncKeyState(VkMenu) & 0x8000) != 0 || (GetAsyncKeyState(VkLWin) & 0x8000) != 0 || (GetAsyncKeyState(VkRWin) & 0x8000) != 0;

    private static DateTime _userSwitchedAt = DateTime.MinValue;

    private const int VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    /// <summary>그 프로세스들의 보이는 맨 위 창 (주인 창이 없는 것만 — 대화 상자는 주인 창과 함께 움직인다)</summary>
    private static IEnumerable<nint> Windows(string[] names)
    {
        var pids = new HashSet<uint>();
        foreach (var name in names)
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                    pids.Add((uint)p.Id);
        // 주인 창이 없는 창 + 주인이 있어도 스스로 "항상 위"인 창 (Wanderer Empire 시작 로고 "startup" — 숨은 창에 딸려 있어 전에는 빠졌음, 2026-10-10 실측)
        return Visible(pids).Where(h => !HasOwner(h) || IsTopmost(h));
    }

    private const int SwShowMinNoActive = 7;
    private static readonly nint HwndBottom = 1, HwndTopmost = -1, HwndNoTopmost = -2;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    /// <summary>허브 제조사 프로그램의 시작 로고 창인가 (허브 모델의 SplashTitle — Wanderer Empire: 2026-10-10 실측 제목 "startup", 숨은 창에 딸린 "항상 위" WinForms 창)</summary>
    private static bool IsSplash(nint h)
    {
        var title = Title(h).Trim();
        return Equipment.Hub.HubCatalog.AllApps.Any(a => a.SplashTitle is { } s && string.Equals(title, s, StringComparison.OrdinalIgnoreCase));
    }
}
