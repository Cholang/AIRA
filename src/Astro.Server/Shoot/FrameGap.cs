using Astro.Server.Prepare;

namespace Astro.Server.Shoot;

/// <summary>
/// 장 사이 쉬는 시간 재기 (장비 › 카메라의 배운 값, 2026-10-17): 한 장의 노출 시작부터 다음 장의 노출 시작까지에서 노출을 뺀 시간 —
/// 내려받기·저장·등급 매기기·디더링이 모두 들어간다(계획이 쓰는 값과 같은 뜻). 그 사이에 자오선 반전·초점 다시 맞추기·가이드 별 잃음·사진 실패·멈춤이
/// 끼면 그 간격은 버린다(<see cref="Spoil"/>). 촬영이 끝나면 그 밤의 중앙값을 지금 카메라의 배운 값으로 남긴다(Equipment.Camera.CameraLearned)
/// </summary>
public sealed class FrameGap
{
    /// <summary>이보다 길면 쉬는 시간이 아니라 무언가 기다린 것 (버림)</summary>
    public const double MaxSeconds = 120;
    /// <summary>이만큼은 재야 그 밤의 값으로 남긴다</summary>
    public const int MinSamples = 3;

    private DateTimeOffset? _start;
    private int _exposure;
    private bool _clean;
    private readonly List<double> _samples = [];

    public IReadOnlyList<double> Samples => _samples;

    /// <summary>한 장의 노출을 시작한다 — 앞 장부터 이어진 깨끗한 간격이면 쉬는 시간을 하나 남긴다</summary>
    public void Begin(DateTimeOffset now, int exposureSeconds)
    {
        if (_start is { } s && _clean)
        {
            var gap = (now - s).TotalSeconds - _exposure;
            if (gap is >= 0 and <= MaxSeconds) _samples.Add(gap);
        }
        (_start, _exposure, _clean) = (now, exposureSeconds, true);
    }

    /// <summary>지금 간격에 다른 일이 끼었다 — 이 간격은 재지 않는다</summary>
    public void Spoil() => _clean = false;

    public void Reset()
    {
        (_start, _clean) = (null, false);
        _samples.Clear();
    }

    /// <summary>그 밤의 쉬는 시간(중앙값). 모자라면 null</summary>
    public double? Median => _samples.Count >= MinSamples ? PrepTimes.Median(_samples) : null;
}
