using System.Runtime.InteropServices;
using System.Text;

namespace Astro.Server.Engine;

/// <summary>
/// 다른 프로그램(N.I.N.A.·PHD2·Empire) 창 찾기·다루기에 쓰는 Win32 호출 모음 (Gemini 리뷰 6절 2-2 — 같은 선언이 네 곳에 있었다).
/// 창을 어떻게 다룰지는 쓰는 쪽(BackgroundWindows·NinaToastHider·NinaAutofocusWindowHider·Phd2Windows)이 정한다
/// </summary>
internal static class Win32Windows
{
    /// <summary>그 프로세스들의 보이는 창 (맨 위 창 목록 순서 — 위에 있는 창이 먼저)</summary>
    public static List<nint> Visible(ISet<uint> pids)
    {
        var found = new List<nint>();
        if (pids.Count == 0) return found;
        EnumWindows((h, _) =>
        {
            if (pids.Contains(Pid(h)) && IsWindowVisible(h)) found.Add(h);
            return true;
        }, 0);
        return found;
    }

    public static uint Pid(nint h)
    {
        GetWindowThreadProcessId(h, out var pid);
        return pid;
    }

    public static string Title(nint h)
    {
        var title = new StringBuilder(64);
        GetWindowText(h, title, title.Capacity);
        return title.ToString();
    }

    /// <summary>주인 창이 있는 창인가 (대화 상자·딸린 창)</summary>
    public static bool HasOwner(nint h) => GetWindow(h, GwOwner) != 0;

    /// <summary>스스로 "항상 위"인 창인가</summary>
    public static bool IsTopmost(nint h) => (GetWindowLongPtr(h, GwlExStyle) & WsExTopmost) != 0;

    private const uint GwOwner = 4;
    private const int GwlExStyle = -20;
    private const nint WsExTopmost = 0x8;

    private delegate bool EnumProc(nint hWnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hWnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(nint hWnd, int cmd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
}
