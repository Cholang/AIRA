using System.Text.Json;
using System.Text.RegularExpressions;

namespace Astro.Core;

/// <summary>
/// 화면 고정 문구 표 (2026-10-18 사용자 결정 — 게임의 텍스트 키처럼): 버튼 · 상단 제목 · 상태 줄 · 단계 레일의 글자는 코드에 쓰지 않고
/// 키로 부른다. 값은 UiText.json 한 곳에만 있고, 웹(web/src/uiText.ts)도 같은 파일을 읽는다 — 같은 뜻의 버튼은 같은 키를 쓰면 단어가 저절로 같다.
/// 키 이름: button.* · title.* · statusbar.* · rail.* (docs/implementation/ARCHITECTURE.md "화면 문구 키").
/// 바뀌는 자리는 {이름}, 조사가 앞말에 맞춰 바뀌는 자리는 {이름}(으)로 · {이름}을(를) · {이름}이(가) · {이름}은(는) · {이름}와(과).
/// {product}는 제품 이름(Product.Name)으로 저절로 채운다
/// </summary>
public static class UiText
{
    private static readonly IReadOnlyDictionary<string, string> Table = Load();

    /// <summary>모든 키 (시험이 코드에서 부르는 키와 맞춰 본다)</summary>
    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)Table.Keys;

    /// <summary>키의 문구. 없는 키면 키 그대로 (시험이 코드의 모든 키가 표에 있는지 본다)</summary>
    public static string Get(string key) => Fill(Table.TryGetValue(key, out var v) ? v : key);

    /// <summary>바뀌는 자리를 채운 문구 — 예: Format("button.slew.to", ("target", "M31")) → "M31로 이동"</summary>
    public static string Format(string key, params (string Name, object? Value)[] values) =>
        Fill(Table.TryGetValue(key, out var v) ? v : key, values);

    private static readonly Regex Slot = new(@"\{(\w+)\}(\(으\)로|을\(를\)|이\(가\)|은\(는\)|와\(과\))?", RegexOptions.Compiled);

    /// <summary>{이름}과 조사 표시를 채운다. 모르는 이름은 그대로 둔다</summary>
    public static string Fill(string template, params (string Name, object? Value)[] values) =>
        Slot.Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            string? word = name == "product" ? Product.Name : null;
            foreach (var (n, v) in values)
                if (n == name) word = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            if (word is null) return m.Value;
            return m.Groups[2].Value switch
            {
                "(으)로" => Josa.Ro(word),
                "을(를)" => word + Josa.Pick(word, "을", "를"),
                "이(가)" => word + Josa.Pick(word, "이", "가"),
                "은(는)" => word + Josa.Pick(word, "은", "는"),
                "와(과)" => word + Josa.Pick(word, "과", "와"),
                _ => word,
            };
        });

    private static IReadOnlyDictionary<string, string> Load()
    {
        using var s = typeof(UiText).Assembly.GetManifestResourceStream("UiText.json")
            ?? throw new InvalidOperationException("UiText.json이 Astro.Core에 들어 있지 않습니다");
        var all = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? [];
        // "_"로 시작하는 키는 표의 설명 (화면에 쓰지 않는다)
        return all.Where(kv => !kv.Key.StartsWith('_')).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
