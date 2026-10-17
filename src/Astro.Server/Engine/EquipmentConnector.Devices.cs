using Astro.Core;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astro.Core.Setup;
using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

/// <summary>장비 연결 — PHD2 점검·프로필 장비 읽기 (EquipmentConnector 나눔, 상태 필드는 EquipmentConnector.cs)</summary>
public sealed partial class EquipmentConnector
{
    private async Task<Phd2Check.Problem?> CheckPhd2Async(CancellationToken ct)
    {
        var host = "localhost";
        var port = 4400;
        if (await nina.GetActiveProfileAsync(ct) is { ValueKind: JsonValueKind.Object } profile && profile.TryGetProperty("GuiderSettings", out var g))
        {
            if (Text(g, "PHD2ServerUrl") is { } h) host = h;
            if (g.TryGetProperty("PHD2ServerPort", out var p) && p.TryGetInt32(out var n)) port = n;
        }
        var mount = await nina.GetInfoAsync("mount", ct) is { ValueKind: JsonValueKind.Object } m
            && m.TryGetProperty("Connected", out var c) && c.ValueKind == JsonValueKind.True ? Text(m, "Name") : null;
        var problem = await Phd2Check.CheckAsync(host, port, mount, ct);
        if (problem is not { CameraOff: true }) return problem;
        // PHD2 안의 가이드 카메라가 꺼져 있음 → PHD2에 장비 연결을 시키고 알림으로 원인을 본다 (N.I.N.A.가 이미 시켰어도 원인 문장은 받지 못함)
        var (ok, alert) = await Phd2Check.ConnectEquipmentAsync(host, port, ct);
        if (ok) return await Phd2Check.CheckAsync(host, port, mount, ct);
        // 같은 카메라를 다른 USB 자리에 꽂아서 못 찾았으면: PHD2가 기억한 자리를 지금 자리로 고치고 한 번 더 연결 (2026-10-11 사용자 요청)
        var place = Phd2Check.CameraNotFound(alert) ? TryRepoint() : null;
        if (place is { Fixed: true })
        {
            log.LogInformation("PHD2 가이드 카메라 자리를 바꿈: {Saved} → {Now}", place.Saved, place.Now);
            (ok, alert) = await Phd2Check.ConnectEquipmentAsync(host, port, ct);
            if (ok) return await Phd2Check.CheckAsync(host, port, mount, ct);
        }
        if (Phd2Check.CameraNotFound(alert))
            // 왜 못 찾았는지 자리로 알린다 (사용자 요청: 다른 자리에 꽂혔다는 것을 알려 줄 것)
            return place switch
            {
                { Present: 0 } => new Phd2Check.Problem("가이드 카메라가 PC에 연결되어 있지 않습니다",
                    "가이드 카메라의 USB 케이블과 전원을 확인한 뒤 다시 연결을 눌러 주세요."),
                { Present: > 1 } => new Phd2Check.Problem("같은 종류의 카메라가 여러 대 꽂혀 있어 가이드 카메라를 고르지 못했습니다",
                    "PHD2의 장비 연결 창에서 카메라 줄 옆 선택 버튼으로 가이드 카메라를 고른 뒤 연결하고, 창을 닫은 다음 다시 연결을 눌러 주세요."),
                { Moved: true } => new Phd2Check.Problem("가이드 카메라가 전과 다른 USB 자리에 꽂혀 있습니다",
                    $"{Product.Ga} PHD2에 새 자리를 알려 주었지만 연결되지 않았습니다. 가이드 카메라를 전에 쓰던 USB 자리에 다시 꽂거나, PHD2의 장비 연결 창에서 카메라를 다시 고른 뒤 다시 연결을 눌러 주세요."),
                _ => new Phd2Check.Problem("PHD2가 기억해 둔 가이드 카메라를 찾지 못했습니다",
                    "가이드 카메라를 다른 USB 자리에 꽂으면 PHD2가 카메라를 다시 골라야 합니다(PHD2의 동작). PHD2의 장비 연결 창에서 카메라 줄 옆 선택 버튼으로 가이드 카메라를 고른 뒤 연결하고, 창을 닫은 다음 다시 연결을 눌러 주세요."),
            };
        return alert is { Length: > 0 } ? problem with { Fix = $"PHD2 알림: \"{alert}\". {problem.Fix}" } : problem;
    }

    /// <summary>Id가 null이면 등록되지 않은 장비. 필수 장비면 실패, 나머지는 Absent(작은 원)</summary>
    /// <summary>Chosen: AA의 장비 변경에서 고른 장비 (N.I.N.A. 프로필과 다를 수 있다)</summary>
    private sealed record Device(Slot Slot, string? Id, string Name, string DriverName, bool Chosen = false)
    {
        public bool Absent => Id is null && Slot.Tier != Required;
    }

    private async Task<List<Device>> ReadDevicesAsync(CancellationToken ct)
    {
        if (await nina.GetActiveProfileAsync(ct) is not { ValueKind: JsonValueKind.Object } profile) return [];
        var list = new List<Device>();
        foreach (var slot in Slots)
        {
            if (slot.Kind == Scope)
            {
                // 칸 아래 작은 글씨: 이름만 (초점거리·F값은 장비 변경의 목록에서, 2026-10-01 사용자 결정)
                list.Add(optics.Current is { } scope
                    ? new Device(slot, scope.Id, scope.Name, scope.Name)
                    : new Device(slot, null, "등록 안 됨", ""));
                continue;
            }
            // AA에서 고른 장비가 있으면 그것을 쓴다 (null = 제거)
            if (overrides.TryGet(slot.Kind, out var chosen))
            {
                list.Add(chosen is null
                    ? new Device(slot, null, "등록 안 됨", "")
                    : new Device(slot, chosen.Id, ShortName(slot.Kind, chosen.Id, chosen.Name), chosen.Name, Chosen: true));
                continue;
            }
            // 가이더는 Id 대신 GuiderName에 들어 있다 (예: PHD2_Single)
            var id = profile.TryGetProperty(slot.ProfileKey, out var s) ? Text(s, slot.Kind == "guider" ? "GuiderName" : "Id") : null;
            if (id is null || id is "No_Device" or "No_Guider")
            {
                // 등록 안 됨: 필수 장비는 실패 칸, 나머지는 작은 원 (장비 변경 모드에서 등록할 수 있게 목록에는 넣는다)
                list.Add(new Device(slot, null, "등록 안 됨", ""));
                continue;
            }
            var last = Text(s, "LastDeviceName");
            list.Add(new Device(slot, id, ShortName(slot.Kind, id, last), last ?? id));
        }
        // 지금 장비 (장비 트리의 배운 값) — 카메라·적도의·포커서·전원 허브를 바꿔 끼우면 그 장비의 모델로 넘어간다.
        // 등록 안 된 장비는 "없음"으로 남긴다 — 전에 쓰던 장비를 계속 들고 있지 않게 (2026-10-17 사용자)
        if (learned is not null)
            foreach (var d in list.Where(d => d.Slot.Kind is "camera" or "mount" or "focuser" or Hub))
                if (d.Id is null) learned.SetAbsent(d.Slot.Kind);
                else learned.SetCurrent(d.Slot.Kind, d.Slot.Kind == Hub ? d.Id : d.DriverName);
        return list;
    }

    /// <summary>칸에 쓸 짧은 이름: "WandererEmpire WandererBox 1 (ASCOM)" → "WandererBox", "X-T5 (gin_X-T5)" → "X-T5"</summary>
    private static string ShortName(string kind, string id, string? last)
    {
        if (kind == "guider") return id.StartsWith("PHD2", StringComparison.OrdinalIgnoreCase) ? "PHD2" : id;
        var name = last ?? id;
        var paren = name.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0) name = name[..paren];
        foreach (var noise in new[] { "WandererEmpire ", "ASCOM " }) name = name.Replace(noise, "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+\d+$", ""); // "WandererBox 1" → "WandererBox"
        return name.Trim();
    }

    /// <summary>PHD2 카메라 자리 고치기 — 레지스트리·장치 목록을 읽다 무엇이 실패해도 연결 확인은 원래 안내로 끝나게 (2026-10-11 실기: 여기서 예외가 나 요청 전체가 오류)</summary>
    private Phd2CameraRepoint.Result? TryRepoint()
    {
        try { return Phd2CameraRepoint.TryRepoint(); }
        catch (Exception e)
        {
            log.LogWarning(e, "PHD2 가이드 카메라 자리를 확인하지 못함");
            return null;
        }
    }

    private static string? Text(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
