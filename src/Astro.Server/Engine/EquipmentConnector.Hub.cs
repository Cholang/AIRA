using Astro.Core;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astro.Core.Setup;
using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

/// <summary>장비 연결 — 전원 허브·배선 학습 (EquipmentConnector 나눔, 상태 필드는 EquipmentConnector.cs)</summary>
public sealed partial class EquipmentConnector
{
    /// <summary>
    /// 허브 뒤 장비인데 허브가 실패함: PC에 보이는지 바로 확인할 수 있고(COM 포트·USB 제조사) 보이면 전원이 있다 → true.
    /// 허브에 전원이 없던 것이면 그 장비는 허브에서 받지 않는다는 증거 (적도의는 DC 전원, 나머지는 자체 전원)
    /// </summary>
    private async Task<bool> PoweredAnywayAsync(Device d, CancellationToken ct)
    {
        if (options.Value.Simulate || d.Id is null || !DevicePrecheck.CanSee(host, d.Slot.Kind, d.Id)) return false;
        if (!(await DevicePrecheck.CheckAsync(host, d.Slot.Kind, d.Id, d.Slot.Role, TimeSpan.Zero, ct)).Ok) return false;
        // 배선은 고치지 않는다: 0V·USB가 보인다는 것만으로는 전원 경로를 알 수 없다 (적도의 출력 꺼짐·케이블 빠짐·USB 전원만 등, CX-20-07)
        return true;
    }

    private const string Hub = "switch";

    public const string Scope = "scope";

    /// <summary>"Regulated 0-13.2V adjustable DC2: DC2" → "DC2"</summary>
    internal static string ShortOutlet(string name) => name.Contains(':') ? name[(name.LastIndexOf(':') + 1)..].Trim() : name;

    /// <summary>
    /// 이번에 꺼져 있던 파워박스 출력을 켜서 나타난 장비 = 파워박스에서 전원을 받음 — 다만 배선은 **실제 연결에 성공한 뒤에만** 조용히 고친다
    /// (CX-20-07, 2026-10-11 사용자 동의). 연결 결과는 ConnectTimedAsync가 본다
    /// </summary>
    private DevicePrecheck.Result LearnIfConnected(DevicePrecheck.Result r, string kind, bool switchedNow, string evidence)
    {
        if (switchedNow) _learnOnPass = (kind, evidence);
        return r;
    }

    /// <summary>장비가 PC에 안 보일 때 배선에 맞춰 덧붙이는 말 (적도의·장비가 파워박스에서 받으면 그 출력부터)</summary>
    private string WiringHint(string kind) =>
        Wiring.SourceOf(kind) == Profiles.PowerWiring.Hub && kind != Hub
            ? $" 전원 설정상 파워박스에서 전원을 받으니, {HubAppName ?? "허브 프로그램"}에서 그 출력이 켜져 있는지도 확인해 주세요."
            : kind == "mount" && Wiring.SourceOf(Hub) == Profiles.PowerWiring.Mount
                ? " 적도의 전원을 켜면 파워박스도 함께 켜집니다."
                : "";

    /// <summary>지금 허브의 제조사 프로그램 이름 (장비 › 전원 허브). 허브가 없거나·아직 모르거나·프로그램이 없는 허브면 null</summary>
    public string? HubAppName => Equipment.Hub.HubCatalog.Current(learned?.Current(Hub))?.App?.Name;

    /// <summary>이보다 낮으면 허브에 전원이 없다고 본다 (12V 장비용 허브 — USB만 꽂혀 있으면 0 근처)</summary>
    private const double NoPowerVolts = 5;

    /// <summary>
    /// 허브 입력 전압(V). 읽기 전용 칸 중 이름에 입력·전압이 함께 든 것. N.I.N.A.는 연결 직후 몇 초 동안 값을 아직 안 읽어 0일 수 있어
    /// 8초까지 다시 읽어 가장 큰 값 (전원이 있으면 바로 끝남). 그런 칸이 없으면 null (모름 — 막지 않음). 실기 미확인: Empire의 칸 이름
    /// </summary>
    private async Task<double?> HubInputVoltsAsync(CancellationToken ct)
    {
        double? best = null;
        for (var i = 0; i < 9; i++)
        {
            var info = await nina.RequestAsync("equipment/switch/info", TimeSpan.FromSeconds(10), ct);
            var seen = new List<string>();
            if (info.Response is { ValueKind: JsonValueKind.Object } r)
                foreach (var key in new[] { "ReadonlySwitches", "Gauges" })
                    if (r.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array)
                        foreach (var g in list.EnumerateArray())
                        {
                            var name = Text(g, "Name");
                            var has = g.TryGetProperty("Value", out var v) && v.TryGetDouble(out _);
                            seen.Add($"{name}={(has ? v.GetDouble().ToString("0.##") : "?")}");
                            if (name is { } n && IsInputVoltage(n) && has) best = Math.Max(best ?? 0, v.GetDouble());
                        }
            // 기록: 허브가 주는 읽기 전용 칸 이름과 값 — 입력 전압 칸 이름이 맞는지 다음 실행에서 확인하려고 (2026-10-10, 실기 미확인)
            if (i == 0 || best is null && i == 2) log.LogInformation("허브 읽기 칸 ({Try}번째): {Gauges}", i + 1, seen.Count == 0 ? "없음" : string.Join(", ", seen));
            // 전압 칸이 없음 — 칸 목록이 왔는데 전압 이름이 하나도 없으면 바로 (2026-10-11 기록: 이 허브는 DC1·DC3 칸뿐이라 매번 2초를 기다렸다)
            if (best is null && (i >= 2 || seen.Count > 0 && !seen.Any(IsInputVoltage))) return null;
            if (best >= NoPowerVolts) { log.LogInformation("허브 입력 전압 {Volts}V — 전원 있음", best); return best; }
            await Task.Delay(1000, ct);
        }
        return best;
    }

    internal static bool IsInputVoltage(string name) =>
        Regex.IsMatch(name, @"(input|입력).*(volt|전압)|(volt|전압).*(input|입력)", RegexOptions.IgnoreCase);

    /// <summary>
    /// 허브의 "USB" 출력이 꺼져 있으면 켠다. N.I.N.A. 출력 바꾸기의 index는 출력 Id가 아니라 쓰기 가능한 출력 목록의 순서(10/09 실기 —
    /// Id를 넣으면 "Switch value updated"라고 답하고 실제로는 안 바뀜) → 순서로 바꾸고 다시 읽어 확인한다. 못 켜도 연결은 그대로 (뒤 장비 점검이 알린다)
    /// </summary>
    private async Task EnsureHubUsbOnAsync(CancellationToken ct)
    {
        var on = await SetHubOutputAsync(n => Regex.IsMatch(n, @"(^|:\s*)USB\s*$|^USB\b", RegexOptions.IgnoreCase), true, ct);
        if (on is { } done) log.LogInformation(done ? "허브 USB 출력이 켜져 있습니다" : "허브 USB 출력을 켜지 못했습니다 (다시 읽은 값이 꺼짐)");
    }

    /// <summary>허브의 쓰기 가능한 출력 이름들 (장비 변경의 카메라 전원 고르기). 허브가 연결되지 않았으면 빈 목록</summary>
    public async Task<IReadOnlyList<string>> HubOutputsAsync(CancellationToken ct)
    {
        var info = await nina.RequestAsync("equipment/switch/info", TimeSpan.FromSeconds(10), ct);
        if (!info.Ok || info.Response is not { ValueKind: JsonValueKind.Object } r || !r.TryGetProperty("Connected", out var c) || c.ValueKind != JsonValueKind.True
            || !r.TryGetProperty("WritableSwitches", out var ws) || ws.ValueKind != JsonValueKind.Array) return [];
        return ws.EnumerateArray().Select(x => Text(x, "Name")).OfType<string>().ToList();
    }

    /// <summary>
    /// 허브 출력 하나를 켜거나 끈다. N.I.N.A.의 index는 출력 Id가 아니라 쓰기 가능한 출력 목록의 순서(10/09 실기 — Id를 넣으면 "Switch value updated"라고 답하고
    /// 실제로는 안 바뀜). N.I.N.A.는 출력 값을 몇 초마다 새로 읽어서(켠 뒤 2초에는 아직 0) 10초까지 1초마다 다시 읽어 확인한다.
    /// 결과: true = 원하는 상태, false = 바꾸지 못함, null = 그 출력이 없음
    /// </summary>
    private async Task<bool?> SetHubOutputAsync(Func<string, bool> match, bool on, CancellationToken ct)
    {
        var info = await nina.RequestAsync("equipment/switch/info", TimeSpan.FromSeconds(10), ct);
        if (!info.Ok || info.Response is not { ValueKind: JsonValueKind.Object } r || !r.TryGetProperty("WritableSwitches", out var ws) || ws.ValueKind != JsonValueKind.Array) return null;
        var list = ws.EnumerateArray().ToList();
        var index = list.FindIndex(x => Text(x, "Name") is { } n && match(n));
        if (index < 0) return null;
        static bool IsOn(JsonElement x) => x.TryGetProperty("Value", out var v) && v.TryGetDouble(out var d) && d >= 1;
        if (IsOn(list[index]) == on) return true;
        await nina.RequestAsync($"equipment/switch/set?index={index}&value={(on ? 1 : 0)}", TimeSpan.FromSeconds(10), ct);
        if (on) _switchedOnAt = DateTime.UtcNow;
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1000, ct);
            var after = await nina.RequestAsync("equipment/switch/info", TimeSpan.FromSeconds(10), ct);
            if (after.Response is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty("WritableSwitches", out var w2) && w2.ValueKind == JsonValueKind.Array
                && w2.EnumerateArray().ElementAtOrDefault(index) is { ValueKind: JsonValueKind.Object } sw && IsOn(sw) == on) return true;
        }
        return false;
    }

    /// <summary>PHD2 주소는 N.I.N.A. 프로필의 가이더 설정에서(없으면 localhost:4400), 비교할 적도의 이름은 N.I.N.A.가 연결한 적도의</summary>
    /// <summary>
    /// N.I.N.A.가 그 장비를 장비 목록에 올릴 때까지 기다린다 (최대 30초). N.I.N.A.를 막 켰을 때는 몇 초 동안 장비 목록을 만드는 중이라
    /// connect?to=가 "목록에 없음"(Sequence contains no matching element)으로 실패한다 (2026-10-08 실기: 켜고 9초 뒤 전원 허브 연결 실패, 목록은 11초 뒤 완성).
    /// 끝내 안 나타나면 그냥 연결을 시도해 원래 오류를 보인다
    /// </summary>
    private async Task<bool?> WaitListedAsync(string kind, string id, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var sawList = false;
        while (DateTime.UtcNow < deadline)
        {
            var list = await nina.ListDevicesAsync(kind, ct);
            if (list.Any(x => x.Id == id)) return true;
            sawList |= list.Count > 0;
            await Task.Delay(1500, ct);
        }
        // 목록은 받았는데 끝내 없음 = N.I.N.A.가 이 장비를 찾지 못함 (Codex B05). 목록 자체를 못 받았으면 모름
        return sawList ? false : null;
    }
}
