using System.Text.RegularExpressions;
using Astro.Core;

namespace Astro.Server.Tests.Core;

/// <summary>화면 문구 표 (UiText.json — 버튼 · 상단 제목 · 상태 줄 · 단계 레일, 2026-10-18): 코드가 부르는 키가 모두 표에 있고, 안 쓰는 키가 없는지</summary>
public class UiTextTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "AA.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("저장소 맨 위(AA.slnx)를 찾지 못함");
    }

    /// <summary>서버(.cs)와 웹(.ts·.tsx) 코드 — 빌드 결과·node_modules 제외</summary>
    private static IEnumerable<(string Path, string Text)> Sources()
    {
        var root = Root();
        foreach (var (dir, exts) in new[] { (Path.Combine(root, "src"), new[] { ".cs" }), (Path.Combine(root, "web", "src"), new[] { ".ts", ".tsx" }) })
            foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                if (!exts.Contains(Path.GetExtension(f)) || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.Contains("wwwroot")) continue;
                yield return (f, File.ReadAllText(f));
            }
    }

    private static readonly Regex KeyLiteral = new(@"[""'`]((?:button|title|statusbar|rail)\.[A-Za-z0-9.]+)[""'`]", RegexOptions.Compiled);

    [Fact]
    public void 코드가_부르는_키는_모두_표에_있다()
    {
        var missing = Sources().SelectMany(s => KeyLiteral.Matches(s.Text).Select(m => (s.Path, Key: m.Groups[1].Value)))
            .Where(x => !UiText.Keys.Contains(x.Key))
            .Select(x => $"{Path.GetFileName(x.Path)}: {x.Key}").Distinct().ToList();
        Assert.True(missing.Count == 0, "표에 없는 키:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void 표의_키는_모두_어딘가에서_쓴다()
    {
        var all = string.Join("\n", Sources().Select(s => s.Text));
        // 웹 단계 이름은 rail.stage.${단계}로 부른다 (appFlow.stageLabel)
        var unused = UiText.Keys.Where(k => !k.StartsWith("rail.stage.") && !all.Contains($"\"{k}\"") && !all.Contains($"'{k}'")).ToList();
        Assert.True(unused.Count == 0, "쓰지 않는 키:\n" + string.Join("\n", unused));
    }

    [Fact]
    public void 바뀌는_자리와_조사를_채운다()
    {
        Assert.Equal("M31로 이동", UiText.Format("button.slew.to", ("target", "M31")));
        Assert.Equal("NGC 7000으로 이동", UiText.Format("button.slew.to", ("target", "NGC 7000")));
        Assert.Equal("안드로메다 은하는 아직 낮습니다", UiText.Format("title.slew.tooLow", ("target", "안드로메다 은하")));
        Assert.Equal("플랫을 찍지 못했습니다", UiText.Format("title.wrap.shootFailed", ("what", "플랫")));
        Assert.Equal("다크를 찍지 못했습니다", UiText.Format("title.wrap.shootFailed", ("what", "다크")));
        Assert.Equal($"{Product.Name} 종료", UiText.Get("button.quit"));
    }
}
