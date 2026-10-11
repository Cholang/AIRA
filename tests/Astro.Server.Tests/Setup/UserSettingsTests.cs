using Astro.Server.Setup;

namespace Astro.Server.Tests.Setup;

/// <summary>설정 화면의 앱 전체 설정 (2026-10-11): AI 키는 암호화해 저장하고 다시 읽으면 풀린다, 다시 켜도 남는다</summary>
public class UserSettingsTests
{
    private static string Root() => Path.Combine(Path.GetTempPath(), "aira-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 키는_파일에_그대로_남지_않고_다시_켜도_읽힌다()
    {
        var root = Root();
        var s = new UserSettings(root);
        Assert.Null(s.GeminiKey);
        Assert.True(s.AiEnabled); // 처음에는 켜짐
        s.SetGeminiKey("  secret-key  ");
        s.SetAiEnabled(false);
        Assert.DoesNotContain("secret-key", File.ReadAllText(Path.Combine(root, "settings.json")));
        var again = new UserSettings(root);
        Assert.Equal("secret-key", again.GeminiKey);
        Assert.False(again.AiEnabled);
        again.SetGeminiKey(""); // 비우면 지운다
        Assert.Null(new UserSettings(root).GeminiKey);
    }

    [Fact]
    public void 경로는_따옴표를_떼고_비우면_자동으로()
    {
        var s = new UserSettings(Root());
        s.SetSharpCapExe("\"C:\\X\\SharpCap.exe\"");
        Assert.Equal("C:\\X\\SharpCap.exe", s.SharpCapExe);
        s.SetSharpCapExe("  ");
        Assert.Null(s.SharpCapExe);
    }
}
