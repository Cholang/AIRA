using System.Runtime.InteropServices;

namespace Astro.Server.Engine;

/// <summary>
/// 촬영하는 동안 PC가 절전에 들지 않게 (2026-10-14 자체 리뷰 — 배터리 유휴 절전이 오면 촬영 루프가 멈춰 반전·끝 조건·추적 끄기를 아무도 보지 않는다).
/// 화면은 꺼져도 된다(시스템만 깨어 있게). 프로세스 단위 전원 요청(PowerCreateRequest)이라 비동기 작업이 어느 스레드에서 돌든 유지된다.
/// Hold가 돌려준 것을 Dispose하면 풀린다. Windows가 아니거나 요청을 만들지 못하면 아무것도 하지 않는다
/// </summary>
public static class KeepAwake
{
    public static IDisposable Hold(string reason)
    {
        if (!OperatingSystem.IsWindows()) return Nothing.Instance;
        var context = new ReasonContext { Version = 0, Flags = PowerRequestContextSimpleString, Reason = reason };
        var handle = PowerCreateRequest(ref context);
        if (handle == 0 || handle == -1) return Nothing.Instance;
        if (!PowerSetRequest(handle, PowerRequestSystemRequired))
        {
            CloseHandle(handle);
            return Nothing.Instance;
        }
        return new Request(handle);
    }

    private sealed class Request(nint handle) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            PowerClearRequest(handle, PowerRequestSystemRequired);
            CloseHandle(handle);
        }
    }

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();
        public void Dispose() { }
    }

    private const uint PowerRequestContextSimpleString = 0x1;
    private const int PowerRequestSystemRequired = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string Reason;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool PowerSetRequest(nint handle, int requestType);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool PowerClearRequest(nint handle, int requestType);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(nint handle);
}
