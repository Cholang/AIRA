namespace Astro.Server.Prepare.Tasks.TestShot;

/// <summary>아홉 칸의 한 칸: 별 수, 별 크기(HFD 중앙값, px), 길쭉함(이심률 중앙값, 0 둥긂 ~ 1). 별이 없으면 null</summary>
public sealed record StarTile(int Stars, double? Hfd, double? Eccentricity);

/// <summary>
/// 사진의 아홉 칸 — 화면에 보이는 방향으로 왼쪽 위부터 가로로 0~8 (4 = 가운데).
/// Alignment = 길쭉함을 잰 별들(가운데 칸, 별이 적으면 전체)의 길쭉한 방향이 한쪽으로 모인 정도 (0 제각각 ~ 1 모두 같은 방향)
/// </summary>
public sealed record StarField(IReadOnlyList<StarTile> Tiles, double Alignment);

/// <summary>
/// 아홉 칸 판정 (OPERATION_PROCEDURE.md ⑦): 귀퉁이 넷과 가운데의 별 크기를 견준다 — 한쪽이 크면 기울어짐(틸트),
/// 귀퉁이가 가운데보다 고르게 크면 백포커스·상 굽음, 아니면 고름. 기준은 [임시] — 실제 딥스카이 사진으로 정한다
/// </summary>
public static class StarFieldRules
{
    public const int MinStars = 5;          // [임시] 칸마다 별이 이만큼은 있어야 그 칸을 본다
    public const double TiltRatio = 1.2;    // [임시] 가장 큰 귀퉁이가 가장 작은 귀퉁이의 이 배를 넘으면 기울어짐
    public const double CornerRatio = 1.25; // [임시] 귀퉁이 평균이 가운데의 이 배를 넘으면 백포커스·상 굽음

    public enum Kind { Unknown, Even, Tilted, Corners }

    /// <summary>Side = 기울어짐일 때 별이 큰 쪽 ("오른쪽 위" · "위" …)</summary>
    public sealed record Verdict(Kind Kind, string Caption, string? Side = null);

    private static readonly int[] Corners = [0, 2, 6, 8];
    private static readonly string[] Names = ["왼쪽 위", "위", "오른쪽 위", "왼쪽", "가운데", "오른쪽", "왼쪽 아래", "아래", "오른쪽 아래"];

    public static Verdict Judge(StarField f)
    {
        if (f.Tiles.Count != 9 || Corners.Append(4).Any(k => f.Tiles[k].Stars < MinStars || f.Tiles[k].Hfd is null))
            return new(Kind.Unknown, "아홉 칸은 별이 적어 판정 못 함");
        double Hfd(int k) => f.Tiles[k].Hfd!.Value;
        var big = Corners.MaxBy(Hfd);
        var small = Corners.Min(Hfd);
        if (Hfd(big) > small * TiltRatio)
        {
            var side = Side(big, k => Hfd(k) > small * TiltRatio);
            return new(Kind.Tilted, $"기울어짐 의심({side} 별이 큼)", side);
        }
        if (Corners.Average(Hfd) > Hfd(4) * CornerRatio) return new(Kind.Corners, "귀퉁이 별이 큼");
        return new(Kind.Even, "아홉 칸 고름");
    }

    /// <summary>가장 큰 귀퉁이와 같은 가로줄(또는 세로줄)의 귀퉁이도 크면 그 변, 아니면 그 귀퉁이</summary>
    private static string Side(int big, Func<int, bool> large)
    {
        int row = big / 3, col = big % 3;
        if (large(row * 3 + (2 - col))) return Names[row * 3 + 1]; // 위 · 아래
        if (large((2 - row) * 3 + col)) return Names[3 + col];     // 왼쪽 · 오른쪽
        return Names[big];
    }
}
