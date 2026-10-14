namespace Astro.Server.Sky;

/// <summary>
/// 잘 알려진 대상의 한국어 통칭 (N.I.N.A. 목록에는 영문 통칭만 있다). 화면 이름과 한국어 이름 검색에 쓴다.
/// 키는 DsoCatalog의 대표 이름(M31, NGC 7000 …). 없으면 영문 통칭을 그대로 쓴다. AI가 정한 이름이 있으면 그쪽이 먼저.
/// </summary>
public static class KoreanNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["M1"] = "게 성운", ["M8"] = "석호 성운", ["M11"] = "야생오리 성단", ["M13"] = "헤라클레스 대성단",
        ["M16"] = "독수리 성운", ["M17"] = "오메가 성운", ["M20"] = "삼렬 성운", ["M27"] = "아령 성운",
        ["M31"] = "안드로메다 은하", ["M33"] = "삼각형자리 은하", ["M42"] = "오리온 대성운", ["M44"] = "벌집 성단",
        ["M45"] = "플레이아데스", ["M51"] = "소용돌이 은하", ["M57"] = "고리 성운", ["M63"] = "해바라기 은하",
        ["M64"] = "검은눈 은하", ["M76"] = "작은 아령 성운", ["M78"] = "M78 반사 성운", ["M81"] = "보데 은하",
        ["M82"] = "시가 은하", ["M97"] = "올빼미 성운", ["M101"] = "바람개비 은하", ["M104"] = "솜브레로 은하",
        ["NGC 7000"] = "북아메리카 성운", ["NGC 6960"] = "서쪽 베일 성운", ["NGC 6992"] = "동쪽 베일 성운",
        ["NGC 2237"] = "장미 성운", ["NGC 2244"] = "장미 성운 성단", ["NGC 869"] = "이중 성단", ["NGC 884"] = "이중 성단",
        ["NGC 2024"] = "불꽃 성운", ["NGC 7635"] = "거품 성운", ["NGC 6888"] = "초승달 성운", ["NGC 253"] = "조각가자리 은하",
        ["NGC 7293"] = "나선 성운", ["NGC 1499"] = "캘리포니아 성운", ["NGC 281"] = "팩맨 성운", ["NGC 2264"] = "크리스마스트리 성단",
        ["NGC 4565"] = "바늘 은하", ["NGC 891"] = "NGC 891 은하", ["NGC 7380"] = "마법사 성운",
        ["IC 434"] = "말머리 성운", ["IC 1396"] = "코끼리 코 성운", ["IC 1805"] = "하트 성운", ["IC 1848"] = "영혼 성운",
        ["IC 5070"] = "펠리컨 성운", ["IC 5146"] = "누에고치 성운", ["IC 405"] = "불타는 별 성운", ["IC 2118"] = "마녀 머리 성운",
    };

    /// <summary>대표 이름 또는 다른 번호(NGC·IC) 가운데 처음 맞는 한국어 이름</summary>
    public static string? Of(Dso d) =>
        Map.GetValueOrDefault(d.Name) ?? d.Designations.Select(n => Map.GetValueOrDefault(n)).FirstOrDefault(n => n is not null);

    /// <summary>한국어로 찾기: 이름에 검색어가 들어 있는 대상의 대표 이름들 (공백 무시)</summary>
    public static IReadOnlyList<string> Find(string query)
    {
        var q = query.Replace(" ", "");
        if (q.Length == 0) return [];
        return Map.Where(kv => kv.Value.Replace(" ", "").Contains(q, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
    }
}
