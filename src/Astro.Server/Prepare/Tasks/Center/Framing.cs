namespace Astro.Server.Prepare.Tasks.Center;

/// <summary>
/// 원하는 구도 (⑤ 센터링 뒤 — 사용자가 사진 위의 틀을 돌려 정함). 각도는 모두 "화면에서 시계 방향 +", 그 사진 기준.
/// BaseAngleDeg = 그 사진의 카메라 방향(솔빙), TurnDeg = 그 사진에서 틀을 돌린 만큼. 대상이 바뀌면 쓰지 않는다(TargetId)
/// </summary>
public sealed record FramingGoal(string TargetId, double BaseAngleDeg, double TurnDeg);

/// <summary>
/// 구도 회전 (OPERATION_PROCEDURE.md ⑤, 2026-10-16 사용자 결정): 틀을 돌려 원하는 구도를 정하면 카메라를 그만큼 돌리게 안내하고,
/// 다시 찍어(센터링) 남은 각도를 잰다. 허용 오차 안이면 맞춘 것. 링 눈금 안내("링을 N으로")는 실기 뒤
/// </summary>
public static class Framing
{
    /// <summary>이 안이면 맞춘 것으로 본다 (2026-10-16 사용자 결정)</summary>
    public const double ToleranceDeg = 2;

    /// <summary>
    /// [실기 확인] 틀을 화면에서 시계 방향으로 1° 돌린 구도가 되면 솔빙의 카메라 방향(N.I.N.A. PositionAngle)이 바뀌는 양 (+1 또는 −1).
    /// 거울상이 아닌 사진(굴절 망원경 + 카메라, 북쪽이 위면 동쪽이 왼쪽)은 시계 방향이 서쪽 쪽이라 −1로 본다.
    /// 틀렸으면: 안내대로 돌리고 다시 찍었을 때 남은 각도가 줄지 않고 늘어난다 → 이 부호만 바꾼다
    /// </summary>
    public const int AnglePerTurn = -1;

    /// <summary>-180 초과 ~ 180 이하로</summary>
    public static double Wrap(double deg)
    {
        var d = deg % 360;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    /// <summary>다시 찍은 사진(카메라 방향 angleNow) 기준으로 아직 남은 회전 (화면 시계 방향 +)</summary>
    public static double Remaining(FramingGoal goal, double angleNow) => Wrap(goal.TurnDeg - AnglePerTurn * Wrap(angleNow - goal.BaseAngleDeg));
}
