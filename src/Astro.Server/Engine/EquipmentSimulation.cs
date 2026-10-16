namespace Astro.Server.Engine;

public sealed class EquipmentOptions
{
    /// <summary>
    /// [임시] 장비 없이 개발할 때: 지금 N.I.N.A. 프로필에 있는 장비가 모두 연결된 것으로 간주한다.
    /// 실제 연결은 하지 않는다. 장비를 연결할 수 있게 되면 false.
    /// </summary>
    public bool Simulate { get; set; }

    /// <summary>
    /// [임시] 시뮬레이션에서 연결 실패로 시작할 장비 (switch, mount, camera, focuser, filterwheel, rotator, flatdevice, guider). "*"는 전부.
    /// 화면에 들어올 때마다 이 목록으로 다시 시작하고, 장비별 "다시 연결"을 누르면 그 장비는 성공한다.
    /// </summary>
    public string[] SimulateFailing { get; set; } = [];
}

/// <summary>
/// [임시] 시뮬레이션의 "아직 해결 안 된 장비" 목록. 화면이 여러 번 요청해도 유지되도록 서버에 하나만 둔다.
/// </summary>
public sealed class EquipmentSimulation
{
    private readonly Lock _gate = new();
    private HashSet<string> _failing = [];
    private readonly HashSet<string> _fixed = [];
    private bool _failAll;

    /// <summary>화면에 들어올 때: 실패로 시작할 장비를 다시 정하고, 해결 기록은 지운다.</summary>
    public void Reset(string[] failing)
    {
        lock (_gate)
        {
            _failAll = failing.Contains("*");
            _failing = [.. failing];
            _fixed.Clear();
        }
    }

    public bool IsFailing(string id)
    {
        lock (_gate) return !_fixed.Contains(id) && (_failAll || _failing.Contains(id));
    }

    /// <summary>"다시 연결"을 누르면 그 장비는 해결된 것으로 본다.</summary>
    public void Fix(string id)
    {
        lock (_gate) _fixed.Add(id);
    }
}

/// <summary>
/// 사용자가 "없이 진행"을 고른 준필수 장비 (포커서·가이더). 뒤 단계(계획 추천 등)가 읽는다.
/// 장비 연결 화면에 들어올 때마다 비운다.
/// </summary>
public sealed class EquipmentChoices
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _without = [];

    public void Reset()
    {
        lock (_gate) _without.Clear();
    }

    public void GoWithout(string id)
    {
        lock (_gate) _without.Add(id);
    }

    public void Connected(string id)
    {
        lock (_gate) _without.Remove(id);
    }

    public bool IsWithout(string id)
    {
        lock (_gate) return _without.Contains(id);
    }
}
