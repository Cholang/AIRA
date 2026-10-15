namespace Astro.Server.Prepare.Real;

/// <summary>
/// 카메라의 실제 밝기 눈금 (2026-10-15): 포화되는 값(White)과 빛이 없을 때의 바닥값(Black, 블랙 레벨).
/// N.I.N.A.의 "비트 수"는 사용자 설정이라 틀릴 수 있다 — 사용자 프로필은 16인데 X-T5 원본은 14비트(0~16383, 바닥 1024)라,
/// 65535로 나누면 밝기를 4배 낮게 읽어 배경 판정이 걸리지 않고 플랫은 50%에 닿지 못했다.
/// 그래서 시험 사진(Fits.Analyze)에서 알아내 그날 밤 플랫에도 쓴다. 아직 모르면 null — 쓰는 쪽이 N.I.N.A. 비트 수로 대신한다
/// </summary>
public sealed class CameraScale
{
    private readonly Lock _gate = new();
    private (int White, int Black)? _scale;

    public (int White, int Black)? Current
    {
        get { lock (_gate) return _scale; }
    }

    public void Learn(int white, int black)
    {
        lock (_gate) _scale = (white, black);
    }

    /// <summary>바닥 위 밝기 비율 0~1 (바닥값을 빼고 바닥~포화 사이에서)</summary>
    public static double Fraction(double value, int white, int black) =>
        white > black ? Math.Clamp((value - black) / (white - black), 0, 1) : 0;
}
