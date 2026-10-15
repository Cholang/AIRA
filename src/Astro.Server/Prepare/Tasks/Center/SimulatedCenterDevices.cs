using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.Center;

/// <summary>
/// ⑤ 모의 장비: 12.4′ → 2.1′ → 0.4′, 별 크기 2.3 · 별 48개, 카메라 방향 87°. "center.solve"가 걸리면 노출을 늘려도·하늘 전체에서도 못 찾는다, "center.hfr"면 별이 커짐(2.9).
/// 구도 회전: UserTurns로 "사용자가 카메라를 돌렸다"고 치면 다음 사진에서 그 85%만큼 방향이 바뀐다 (한 번에 딱 맞추지 못하는 손)
/// </summary>
public sealed class SimulatedCenterDevices(SimOptions sim, SimFaults faults) : ICenterDevices
{
    private static readonly double[] Errors = [12.4, 2.1, 0.4];
    private int _round;
    private bool _failing;
    private double _error = 99;
    private double _angle = 87;
    private double _turn;

    /// <summary>[모의] 사용자가 카메라를 돌렸다고 친다 (화면 시계 방향 +, 도)</summary>
    public void UserTurns(double turnDeg) => _turn = turnDeg;

    public async Task<CenterAttempt> AttemptAsync(double raDeg, double decDeg, double exposureSeconds, bool blind, CancellationToken ct)
    {
        await sim.Delay(1300, ct);
        if (!_failing && faults.Take("center.solve")) _failing = true;
        if (_failing)
        {
            if (blind) _failing = false; // 이번 판(하늘 전체까지)만 실패
            return new CenterAttempt(false, 0, null);
        }
        _error = Errors[Math.Min(_round++, Errors.Length - 1)];
        if (_error <= CenterTask.TargetArcmin) _round = 0;
        if (_turn != 0)
        {
            _angle = (_angle + Framing.AnglePerTurn * _turn * 0.85 + 360) % 360;
            _turn = 0;
        }
        return new CenterAttempt(true, _error, Math.Round(_angle, 1), faults.IsArmed("center.hfr") ? 2.9 : 2.3, 48);
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await sim.Delay(50, ct);
        return !faults.Take("stop.fail");
    }

    public Task<CenterEndState> ReadEndStateAsync(CancellationToken ct) => Task.FromResult(new CenterEndState(_error, false, true, false));
}
