using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Astro.Core;
using Astro.Server;
using Microsoft.AspNetCore.Builder;

namespace Astro.Desktop;

/// <summary>
/// 얇은 창 껍데기. 코어 서버를 같은 프로세스에서 켜고, 그 화면을 WebView2로 보여 준다.
/// 폰에서 보는 화면과 같은 화면이다.
/// </summary>
public partial class MainWindow : Window
{
    // web/src/index.css의 --bg와 같은 값. 화면이 뜨기 전 순간에 보이는 색이다.
    private static readonly Dictionary<string, string> ThemeBackground = new()
    {
        ["dark"] = "#1E1F21",
        ["night"] = "#000000",
    };

    private static readonly string ThemeFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder, "theme.txt");

    private WebApplication? _server;
    private WindowThumbnail? _thumbnail;

    public MainWindow()
    {
        InitializeComponent();
        Title = Product.Name; // 한 줄 설명(tagline)은 2026-10-10 사용자 요청으로 없앰
        ApplyTheme(ReadSavedTheme());
        Loaded += OnLoaded;
        Closing += OnClosing;
        Application.Current.SessionEnding += OnSessionEnding;
        // 처음부터 전체화면 (제목 표시줄·작업 표시줄 없이). F11로 창 모드와 오간다. 끄기는 Alt+F4.
        // 창 핸들이 생긴 뒤에 모니터 크기를 잴 수 있어 SourceInitialized에서
        SourceInitialized += (_, _) => SetFullScreen(true);
        // 최소화했다 다시 열면 같은 자리·크기로
        StateChanged += (_, _) => { if (WindowState == WindowState.Normal && WindowStyle == WindowStyle.None) FitMonitor(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.F11) return;
            SetFullScreen(WindowStyle != WindowStyle.None);
            e.Handled = true;
        };
    }

    private void SetFullScreen(bool on)
    {
        // 테두리 없는 창을 "최대화"하면 WPF가 화면보다 조금 크게 잡아 오른쪽·아래가 화면 밖으로 밀린다 (2026-10-08 실기: 캡처에 왼쪽·위 경계만 보임)
        // → 최대화하지 않고, 지금 모니터 전체(작업 표시줄 포함) 크기에 정확히 맞춘다
        if (on)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            FitMonitor();
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _server = AppServer.Build([], AppServer.DefaultUrl, AppContext.BaseDirectory);
            await _server.StartAsync();

            await Web.EnsureCoreWebView2Async();
            // 디스크 캐시를 비운다: 예전에 no-cache 없이 받아 둔 index.html이 남아 있으면 WebView2가 새 빌드 대신 옛 화면을 띄운다
            // (2026-10-09 이름을 바꾼 뒤 "AA v0.1.58" 화면이 뜸). 화면 파일은 내 PC의 내부 서버에서 오므로 비워도 느려지지 않는다. 설정(localStorage)은 남음
            await Web.CoreWebView2.Profile.ClearBrowsingDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache);
            // 화면 프로세스가 죽거나 멈추면 다시 불러온다 — 그대로 두면 회색 창에서 ×도 듣지 않았다 (2026-10-11 자체 점검 H4)
            Web.CoreWebView2.ProcessFailed += (_, args) => OnWebProcessFailed(args.ProcessFailedKind);
            // 바깥 링크(설치 안내 등)는 창 안이 아니라 기본 브라우저로 연다.
            Web.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
            };
            // 화면에서 테마를 바꾸면 창 배경도 맞추고, 다음 실행을 위해 기억한다.
            Web.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                    var type = doc.RootElement.GetProperty("type").GetString();
                    // 화면 아래 단계 레일의 "앱 끄기" (전체화면이라 창 닫기 버튼이 없다)
                    if (type == "close")
                    {
                        _closeConfirmed = true; // 화면이 확인했다 (또는 묻지 않아도 되는 화면)
                        Close();
                        return;
                    }
                    // 화면이 창 × 확인을 받았다 (H4 — 답이 없으면 창이 직접 묻는다)
                    if (type == "confirm-ack")
                    {
                        _closeAck = true;
                        return;
                    }
                    // 상태 줄의 "창 내리기" (전체 화면이라 창 버튼이 없다)
                    if (type == "minimize")
                    {
                        WindowState = WindowState.Minimized;
                        return;
                    }
                    // 설정 화면의 "찾아보기": 실행 파일 고르기 창 → 고른 경로를 화면에 돌려준다 (웹 화면은 파일의 전체 경로를 알 수 없어서)
                    if (type == "pick-exe")
                    {
                        var name = doc.RootElement.GetProperty("name").GetString() ?? "";
                        var dialog = new Microsoft.Win32.OpenFileDialog { Title = $"{name} 고르기", Filter = $"{name}|{name}", CheckFileExists = true };
                        var picked = dialog.ShowDialog(this) == true ? dialog.FileName : null;
                        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "picked-exe", name, path = picked }));
                        return;
                    }
                    // 준비 화면의 SharpCap 영상: 화면이 비울 자리(글자·버튼 없는 곳)를 알려 주면 그 자리에 SharpCap 영상 칸을 실시간으로 비춘다 (WindowThumbnail)
                    if (type == "window-view")
                    {
                        var target = doc.RootElement.GetProperty("target").GetString() ?? "";
                        Rect? rect = null;
                        if (doc.RootElement.TryGetProperty("rect", out var r) && r.ValueKind == JsonValueKind.Object)
                            rect = new Rect(r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(), r.GetProperty("w").GetDouble(), r.GetProperty("h").GetDouble());
                        var dpr = doc.RootElement.TryGetProperty("dpr", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 1;
                        _thumbnail ??= new WindowThumbnail(this, Web, (t, shown, at) =>
                        {
                            // 화면이 닫히는 중이거나 죽었으면 보내지 않는다 (M5)
                            if (_stopping || _stopped || _webDead) return;
                            try
                            {
                                Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
                                {
                                    type = "window-view-state",
                                    target = t,
                                    shown,
                                    rect = at is { } v ? new { x = v.X, y = v.Y, w = v.Width, h = v.Height } : null,
                                }));
                            }
                            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
                        });
                        _thumbnail.Set(target, rect, dpr);
                        return;
                    }
                    if (type != "theme") return;
                    var theme = doc.RootElement.GetProperty("theme").GetString();
                    if (theme is null || !ThemeBackground.ContainsKey(theme)) return;
                    ApplyTheme(theme);
                    SaveTheme(theme);
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            };
            Web.Source = new Uri(AppServer.DefaultUrl);
        }
        catch (Exception ex)
        {
            // AA끼리는 App이 하나만 켜게 막는다. 여기 오는 건 다른 프로그램(개발용 서버 등)이 주소를 쓰고 있을 때
            var reason = ex is IOException && ex.Message.Contains("address already in use")
                ? $"다른 프로그램이 {AppServer.DefaultUrl} 주소를 쓰고 있습니다.\n개발용 서버(dotnet run --project src/Astro.Server)가 켜져 있으면 닫고 다시 실행해 주세요."
                : ex.Message;
            MessageBox.Show(this, $"{Product.Reul} 시작하지 못했습니다.\n\n{reason}", Product.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void ApplyTheme(string theme)
    {
        var color = (Color)ColorConverter.ConvertFromString(ThemeBackground[theme]);
        Background = new SolidColorBrush(color);
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
    }

    private static string ReadSavedTheme()
    {
        try
        {
            var t = File.ReadAllText(ThemeFile).Trim();
            return ThemeBackground.ContainsKey(t) ? t : "dark";
        }
        catch (IOException) { return "dark"; }
        catch (UnauthorizedAccessException) { return "dark"; }
    }

    private static void SaveTheme(string theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeFile)!);
            File.WriteAllText(ThemeFile, theme);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// 창 닫기: 내부 서버를 끝까지 멈춘 뒤에 닫는다. async void라 await에서 창이 먼저 닫히고 프로세스가 끝나
    /// 서버의 정리(N.I.N.A. 감시 등 백그라운드 작업 종료·기록)가 잘릴 수 있었다 → 닫기를 한 번 미루고, 멈춘 뒤 다시 닫는다
    /// </summary>
    /// <summary>창을 지금 모니터 전체 크기에 맞춘다 (화면 배율을 고려해 픽셀 → WPF 단위로)</summary>
    private void FitMonitor()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* 가장 가까운 모니터 */), ref info)) return;
        var toDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = toDip.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
        var bottomRight = toDip.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect32 Monitor; public Rect32 Work; public uint Flags; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private bool _stopping, _stopped;
    /// <summary>화면의 종료 절차 없이 닫음(화면 응답 없음·화면 죽음) — 서버를 멈추기 전에 가이딩 멈춤·추적 끄기를 시도한다 (2026-10-14)</summary>
    private bool _forceClose;

    // 화면이 창 × 확인을 받았는지 · 화면(WebView2)이 다시 쓸 수 없게 죽었는지 (H4)
    private bool _closeAck, _webDead;
    private readonly List<DateTime> _webFailures = [];

    /// <summary>
    /// 창 ×를 화면에 물었는데 3초 안에 화면이 받았다는 답이 없으면(화면이 멈춤·죽음) 창이 직접 묻는다 — 그대로 두면 창을 닫을 수 없었다 (H4).
    /// 이때는 화면의 종료 절차(작업 멈춤·추적 끄기·프로그램 닫기)를 거치지 못하므로 그렇다고 알린다
    /// </summary>
    private async Task AskIfPageSilentAsync()
    {
        await Task.Delay(3000);
        if (_closeAck || _closeConfirmed || _stopping || _stopped) return;
        var answer = MessageBox.Show(this,
            "화면이 응답하지 않습니다. 그래도 닫을까요?\n\n화면의 종료 절차(작업 멈춤 · 프로그램 닫기)를 거치지 않습니다. " +
            "가이드 멈춤과 적도의 추적 끄기는 한 번 시도하지만, 꺼졌는지 N.I.N.A.에서 확인해 주세요.",
            Product.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _closeConfirmed = true;
        _forceClose = true;
        Close();
    }

    /// <summary>화면 프로세스가 죽거나 멈춤: 다시 불러온다. 1분 안에 세 번째면(또는 브라우저 전체가 죽음) 다시 켜 달라고 알리고 바로 닫을 수 있게 (H4)</summary>
    private void OnWebProcessFailed(Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind kind)
    {
        Astro.Server.Engine.FileLogProvider.Write("ERROR", "창", $"화면 프로세스 문제: {kind}");
        // 화면이 다시 뜨기 전에 비추던 SharpCap 영상을 먼저 거둔다 — 그대로 두면 새 화면(시작 화면) 위에 남는다 (Codex CX-30-05). 새 화면이 자리를 보내면 다시 비춘다
        _thumbnail?.Stop();
        var now = DateTime.UtcNow;
        _webFailures.RemoveAll(t => now - t > TimeSpan.FromMinutes(1));
        _webFailures.Add(now);
        var browserGone = kind == Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind.BrowserProcessExited;
        if (!browserGone && _webFailures.Count < 3)
        {
            try
            {
                Web.CoreWebView2.Reload();
                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
        _webDead = true;
        MessageBox.Show(this,
            $"{Product.Ga} 화면을 다시 띄우지 못했습니다. {Product.Reul} 다시 켜 주세요.\n\n장비는 그대로이고, 다시 켜면 오늘 밤 진행 기록에서 이어서 할 수 있습니다. " +
            "적도의 추적이 켜져 있으면 N.I.N.A.에서 직접 꺼 주세요.",
            Product.Name, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// Windows가 꺼지거나 다시 시작하거나 로그오프할 때(업데이트 자동 재시작 포함): 화면의 종료 절차를 밟을 시간이 없으니
    /// 가이딩 멈춤·적도의 추적 끄기만 한 번 시도한다(최대 10초). 그동안 Windows 종료 화면에 이유가 보인다 — 사용자가 "그래도 종료"를 고를 수 있다.
    /// 막지는 않는다(업데이트 재시작을 영영 막지 않게). 2026-10-14 자체 리뷰: 밤에 업데이트 재시작이 오면 적도의가 추적을 계속했다
    /// </summary>
    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        Astro.Server.Engine.FileLogProvider.Write("WARN", "창", $"Windows 종료({e.ReasonSessionEnding}) — 추적 끄기를 시도합니다");
        if (_server?.Services.GetService(typeof(Astro.Server.Prepare.MountHome)) is not Astro.Server.Prepare.MountHome home) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != 0) ShutdownBlockReasonCreate(hwnd, $"{Product.Name}: 적도의 추적을 끄는 중입니다");
        try
        {
            var safe = Task.Run(home.ForceSafeAsync);
            if (safe.Wait(TimeSpan.FromSeconds(10)) && safe.Result is { } problem)
                Astro.Server.Engine.FileLogProvider.Write("WARN", "창", $"Windows 종료: {problem}");
        }
        catch (Exception ex) { Astro.Server.Engine.FileLogProvider.Write("WARN", "창", $"Windows 종료: 추적 끄기 실패 {ex.Message}"); }
        finally { if (hwnd != 0) ShutdownBlockReasonDestroy(hwnd); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool ShutdownBlockReasonCreate(nint hwnd, string reason);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShutdownBlockReasonDestroy(nint hwnd);

    // 창의 × 버튼·Alt+F4: 진행 중이면 화면에 "종료할까요?"를 띄운다 (2026-10-08 사용자 결정). 화면이 답("close")하면 닫는다
    private bool _closeConfirmed;

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_closeConfirmed && !_stopping && !_stopped && !_webDead && Web.CoreWebView2 is { } web)
        {
            e.Cancel = true;
            _closeAck = false;
            try { web.PostWebMessageAsJson("{\"type\":\"confirm-close\"}"); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            _ = AskIfPageSilentAsync();
            return;
        }
        if (_stopped) return;
        if (_stopping) { e.Cancel = true; return; } // 멈추는 중에 다시 누름
        if (_server is null) return;
        e.Cancel = true;
        _stopping = true;
        Hide(); // 종료를 확인했으니 창은 바로 감춘다 — 서버가 멈추는 동안 남아 보이지 않게 (2026-10-10)
        _thumbnail?.Stop(); // 비추던 다른 프로그램 영상도 멈춘다 — 닫힌 화면에 상태를 보내지 않게 (M5)
        var server = _server;
        _server = null;
        if (_forceClose || _webDead)
        {
            try
            {
                if (server.Services.GetService(typeof(Astro.Server.Prepare.MountHome)) is Astro.Server.Prepare.MountHome home)
                    await home.ForceSafeAsync();
            }
            catch (Exception) { /* 닫는 중 — 시도만 */ }
        }
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 멈추지 않아도 창은 닫히게
            await server.StopAsync(limit.Token);
            await server.DisposeAsync();
        }
        catch (Exception) { /* 닫는 중 — 더 할 수 있는 일이 없다 */ }
        _stopped = true;
        Close();
    }
}
