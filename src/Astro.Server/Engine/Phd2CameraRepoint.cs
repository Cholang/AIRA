using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Astro.Server.Engine;

/// <summary>
/// PHD2가 기억한 가이드 카메라 위치를 지금 꽂힌 자리로 고쳐 쓴다 (2026-10-11 사용자 요청 — 실기: 같은 G3M662M을 다른 USB 자리에 꽂자
/// PHD2가 "선택한 ToupTek 카메라를 찾을 수 없습니다"로 건너뜀. PHD2는 카메라를 USB 자리가 들어간 번호(…vid_0547&amp;pid_15b3#7&amp;2a0686fb&amp;0&amp;2)로 기억한다).
/// PHD2에는 카메라를 고르는 명령이 없어서, PHD2 설정(HKCU\Software\StarkLabs\PHDGuidingV2 → 지금 프로필 → cam_hash\…\whichCamera)을 고친다.
/// PHD2는 연결할 때마다 이 값을 읽는다(디버그 기록 확인). 같은 제조사·모델 번호(VID/PID) 장치가 **지금 하나만** 꽂혀 있을 때만 바꾼다 — 여럿이면 어느 것인지 모르므로 두고 안내
/// </summary>
public static class Phd2CameraRepoint
{
    // 위치 번호 = 다음 # 또는 \ 앞까지. 정규식 안에서 \ 자체는 \\ 로 써야 한다 (2026-10-11 실기: \ 하나여서 ]가 이스케이프돼 정규식이 깨졌고, 이 클래스를 처음 쓰는 순간 예외)
    internal static readonly Regex UsbId = new(@"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})#([^#\\]+)", RegexOptions.IgnoreCase);

    /// <summary>PHD2가 기억한 자리와 지금 자리. Moved = 다른 자리에 꽂혀 있었음, Fixed = PHD2 설정을 지금 자리로 고침, Present = 지금 꽂힌 같은 장치 수</summary>
    public sealed record Result(bool Moved, bool Fixed, int Present, string? Saved, string? Now);

    /// <summary>
    /// 기억한 카메라(들)를 지금 꽂힌 자리와 비교하고, 같은 장치가 하나만 꽂혀 있으면 PHD2 설정을 고친다 (프로필에 기억이 여럿이면 모두).
    /// PHD2 설정이 없거나 USB 카메라가 아니면 null
    /// </summary>
    public static Result? TryRepoint()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\StarkLabs\PHDGuidingV2");
        if (root?.GetValue("currentProfile") is not { } profile) return null;
        using var hashes = Registry.CurrentUser.OpenSubKey($@"Software\StarkLabs\PHDGuidingV2\profile\{profile}\cam_hash", writable: true);
        if (hashes is null) return null;
        Result? result = null;
        foreach (var name in hashes.GetSubKeyNames())
        {
            using var cam = hashes.OpenSubKey(name, writable: true);
            if (cam?.GetValue("whichCamera") is not string saved || UsbId.Match(saved) is not { Success: true } m) continue;
            var (vid, pid, instance) = (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
            var present = PresentInstances(vid, pid);
            if (present.Any(p => string.Equals(p, instance, StringComparison.OrdinalIgnoreCase)))
            {
                result ??= new Result(false, false, present.Count, instance, instance); // 이 기억은 지금 자리와 같다
                continue;
            }
            if (present.Count != 1)
            {
                result = new Result(present.Count > 1, false, present.Count, instance, null);
                continue;
            }
            var now = present[0].ToLowerInvariant();
            cam.SetValue("whichCamera", saved[..m.Groups[3].Index] + now + saved[(m.Groups[3].Index + m.Groups[3].Length)..]);
            result = new Result(true, true, 1, instance, now);
        }
        return result;
    }

    /// <summary>그 VID/PID로 지금 꽂혀 있는 USB 장치들의 위치 번호 (Windows 장치 목록에서, 꽂혀 있는 것만)</summary>
    private static List<string> PresentInstances(string vid, string pid)
    {
        var found = new List<string>();
        var key = $@"USB\VID_{vid.ToUpperInvariant()}&PID_{pid.ToUpperInvariant()}";
        using var devices = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{key}");
        if (devices is null) return found;
        foreach (var instance in devices.GetSubKeyNames())
            if (CM_Locate_DevNodeW(out _, $@"{key}\{instance}", 0) == 0) found.Add(instance); // 0 = 지금 꽂혀 있음 (빠진 장치는 찾지 못함)
        return found;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, int flags);
}
