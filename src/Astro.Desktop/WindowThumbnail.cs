using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Astro.Desktop;

/// <summary>
/// 다른 프로그램 창의 한 칸을 아이라 창 안에 실시간으로 비춘다 — Windows DWM 썸네일 (작업 표시줄 미리보기와 같은 기능, 2026-10-11 사용자 아이디어:
/// "아이라를 창문처럼 투명하게 해서 뒤의 SharpCap이 비치게"). 사진을 찍어 줄여 보내던 창 캡처(WindowCapture)는 4K 화면에서 SharpCap 창 전체를
/// 4분의 1로 줄여 영상 칸이 약 466×350 픽셀만 남아 깨져 보였다. 이 방법은 영상 칸(UI Automation의 "Image")만 원래 픽셀로 잘라 그래픽 카드가 부드럽게 맞춘다.
/// SharpCap이 다른 창 뒤에 있어도 되지만 최소화하면 비지 않고 안 보인다. 썸네일은 웹 화면 위에 그려지므로, 화면은 글자·버튼이 없는 자리만 넘긴다
/// </summary>
/// 비춘 실제 자리(영상 비율로 맞춘 뒤, CSS 픽셀)를 화면에 알려 준다 — 화면이 그 둘레에 빛 테두리(GlowFrame)를 그린다.
/// waiting = 아직 비춘 적이 없고 최소화된 것도 아님(SharpCap이 켜지는 중·영상 칸이 아직 없음) — 화면은 이때 대체 사진을 묻지 않는다
/// (대체 사진을 저장할 때마다 SharpCap이 영상 위에 "Snapshot captured" 줄을 띄우고 그 줄이 비추는 영상에 남았다 — 2026-10-17 실기)
internal sealed class WindowThumbnail(Window owner, FrameworkElement web, Action<string, bool, Rect?, bool> report)
{
    /// <summary>지원하는 대상: 이름 → (프로세스 이름, 영상 칸을 찾는 UI Automation 경로)</summary>
    private static readonly Dictionary<string, (string Process, string Panel, string Image)> Targets = new()
    {
        ["sharpcap"] = ("SharpCap", "WpfPreviewPanel", "Image"),
    };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _target;
    private Rect? _dest; // 웹 화면 기준 위치 (CSS 픽셀 = WPF 단위)
    private nint _sourceHwnd;
    private nint _thumb;
    private Int32Rect? _source; // 원본 창 왼쪽 위 기준 영상 칸 (실제 픽셀)
    private AutomationElement? _image;
    private bool _locating;
    private DateTime? _lostAt; // 같은 창에서 영상 칸을 못 찾기 시작한 때
    private (bool Shown, Rect? Rect, bool Waiting)? _reported;
    private bool _iconic; // 대상 창이 최소화됨 (비출 수 없다 — 대체 사진이 필요)
    private bool _everShown; // 이 대상을 한 번이라도 비췄나 (Set이 새 대상을 받으면 처음부터)

    /// <summary>모두 멈춘다 (창을 닫을 때): 확인 타이머를 끄고 썸네일을 놓는다</summary>
    public void Stop()
    {
        _timer.Stop();
        Release();
        _target = null;
    }

    /// <summary>화면이 보낸 자리. rect가 null이면 감춘다</summary>
    public void Set(string target, Rect? cssRect, double dpr)
    {
        if (!Targets.ContainsKey(target)) return;
        if (_target != target) { Release(); _everShown = false; }
        _target = target;
        _dest = cssRect;
        if (_dest is null)
        {
            _timer.Stop();
            Release();
            _reported = null;
            _everShown = false;
            return;
        }
        if (!_timer.IsEnabled)
        {
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();
            OnTick(null, EventArgs.Empty);
        }
        Apply();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_locating || _target is not { } target) return;
        _locating = true;
        // 결과는 창 스레드로 돌려받는다 (Dispatcher — 웹 메시지 처리 중에는 SynchronizationContext가 없을 수 있어 그걸 쓰지 않는다)
        var dispatcher = owner.Dispatcher;
        Task.Run(() =>
        {
            (nint, Int32Rect?, bool) found;
            try { found = Locate(target); }
            catch (Exception) { found = (0, null, false); }
            dispatcher.InvokeAsync(() =>
            {
                _locating = false;
                var (hwnd, source, iconic) = found;
                _iconic = iconic;
                // 같은 창에서 영상 칸을 잠깐 못 찾은 것(SharpCap이 다시 그리는 중 등)은 3초까지 앞 자리를 그대로 쓴다 (2026-10-11 실기:
                // 바로 "안 보임"으로 알리면 화면이 대체 사진으로 바뀌고, 대체 사진을 저장할 때마다 SharpCap이 영상 위에 "Snapshot captured" 줄을 띄웠다)
                if (source is null && hwnd != 0 && hwnd == _sourceHwnd && _source is not null)
                {
                    _lostAt ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - _lostAt < TimeSpan.FromSeconds(3)) return;
                }
                else _lostAt = null;
                if (hwnd != _sourceHwnd) Release();
                _sourceHwnd = hwnd;
                _source = source;
                Apply();
            });
        });
    }

    /// <summary>대상 창과 그 안의 영상 칸 (창 왼쪽 위 기준). 창이 없거나 최소화됐거나 칸을 못 찾으면 (0, null) — 최소화면 iconic</summary>
    private (nint, Int32Rect?, bool Iconic) Locate(string target)
    {
        var (processName, panelId, imageId) = Targets[target];
        nint hwnd = 0;
        foreach (var p in Process.GetProcessesByName(processName))
            using (p)
                if (hwnd == 0 && p.MainWindowHandle != 0) hwnd = p.MainWindowHandle;
        if (hwnd == 0) return (0, null, false);
        if (IsIconic(hwnd)) return (0, null, true);
        // 원본 창 좌표는 실제 픽셀로 (UI Automation도 실제 픽셀) — 이 프로그램의 배율 가상화와 섞이지 않게 이 스레드만 모니터별 배율 인식
        var oldDpi = SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        try
        {
            if (!GetWindowRect(hwnd, out var win)) return (0, null, false);
            var bounds = Bounds(_image);
            if (bounds is null)
            {
                // 영상 칸을 다시 찾는다 (카메라를 바꾸면 크기가 바뀌고, 창을 다시 열면 요소가 바뀐다)
                var root = AutomationElement.FromHandle(hwnd);
                var panel = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, panelId));
                _image = panel?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, imageId));
                bounds = Bounds(_image);
            }
            if (bounds is not { } b) return (hwnd, null, false);
            return (hwnd, new Int32Rect((int)b.X - win.Left, (int)b.Y - win.Top, (int)b.Width, (int)b.Height), false);
        }
        catch (Exception e) when (e is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            _image = null;
            return (hwnd, null, false);
        }
        finally { SetThreadDpiAwarenessContext(oldDpi); }
    }

    private static Rect? Bounds(AutomationElement? el)
    {
        if (el is null) return null;
        try
        {
            var r = el.Current.BoundingRectangle;
            return r.IsEmpty || r.Width < 16 || r.Height < 16 ? null : r;
        }
        catch (ElementNotAvailableException) { return null; }
    }

    private void Apply()
    {
        var target = _target;
        if (target is null) return;
        if (_dest is not { } dest || _source is not { } src || _sourceHwnd == 0)
        {
            Hide();
            Report(target, false, null, waiting: !_everShown && !_iconic);
            return;
        }
        var ownerHwnd = new WindowInteropHelper(owner).Handle;
        if (ownerHwnd == 0) return;
        if (_thumb == 0 && DwmRegisterThumbnail(ownerHwnd, _sourceHwnd, out _thumb) != 0)
        {
            _thumb = 0;
            Report(target, false, null, waiting: false);
            return;
        }
        // 영상 비율을 지키며 자리 안 가운데에 (CSS 픽셀 = WPF 단위)
        var scale = Math.Min(dest.Width / src.Width, dest.Height / src.Height);
        double w = src.Width * scale, h = src.Height * scale;
        double cx = dest.X + (dest.Width - w) / 2, cy = dest.Y + (dest.Height - h) / 2;
        // 썸네일 자리는 이 창의 장치 좌표로 — WPF가 아는 배율(TransformToDevice)로 바꾼다 (이 창이 쓰는 좌표와 같게)
        var toDevice = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        var webAt = web.TranslatePoint(new Point(0, 0), owner);
        double x = (webAt.X + cx) * toDevice.M11, y = (webAt.Y + cy) * toDevice.M22;
        double right = (webAt.X + cx + w) * toDevice.M11, bottom = (webAt.Y + cy + h) * toDevice.M22;
        var props = new ThumbnailProperties
        {
            Flags = RectDestination | RectSource | Visible | SourceClientAreaOnly | Opacity,
            Destination = new RectInt((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(right), (int)Math.Round(bottom)),
            Source = new RectInt(src.X, src.Y, src.X + src.Width, src.Y + src.Height),
            Opacity = 255,
            Visible = true,
            SourceClientAreaOnly = false,
        };
        if (DwmUpdateThumbnailProperties(_thumb, ref props) != 0)
        {
            Release();
            Report(target, false, null, waiting: false);
            return;
        }
        // 웹 화면 기준 CSS 픽셀로 (화면이 테두리를 그릴 자리)
        _everShown = true;
        Report(target, true, new Rect(Math.Round(cx, 1), Math.Round(cy, 1), Math.Round(w, 1), Math.Round(h, 1)), waiting: false);
    }

    private void Hide()
    {
        if (_thumb == 0) return;
        var props = new ThumbnailProperties { Flags = Visible, Visible = false };
        DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    private void Release()
    {
        if (_thumb != 0) DwmUnregisterThumbnail(_thumb);
        _thumb = 0;
    }

    private void Report(string target, bool shown, Rect? rect, bool waiting)
    {
        if (_reported is { } last && last.Shown == shown && last.Rect == rect && last.Waiting == waiting) return;
        _reported = (shown, rect, waiting);
        report(target, shown, rect, waiting);
    }

    // ── Win32 / DWM ─────────
    private const uint RectDestination = 0x1, RectSource = 0x2, Opacity = 0x4, Visible = 0x8, SourceClientAreaOnly = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct RectInt(int left, int top, int right, int bottom)
    {
        public int Left = left, Top = top, Right = right, Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public uint Flags;
        public RectInt Destination;
        public RectInt Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint dest, nint src, out nint thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumb, ref ThumbnailProperties props);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out RectInt rect);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
}
