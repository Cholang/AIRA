using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;

namespace Astro.Server.Tests.Prepare;

/// <summary>버튼 레이아웃 영역 설명(prompt)이 버튼과 함께 화면까지 가는지 (2026-10-17 버튼 단어 표준)</summary>
public class ButtonPromptTests
{
    [Fact]
    public async Task 이동_버튼과_함께_적도의가_움직인다는_설명이_간다()
    {
        var (flow, _) = Harness.Flow();
        var ctx = Harness.TargetContext(new PrepResults(), new InMemoryPrepMemory(), Harness.Plan(DateTimeOffset.Now.AddSeconds(-30)));
        await flow.ResumeTargetAsync(ctx, new HashSet<string>());
        var v = await flow.Target.Until(x => x.Has("move"), "이동 묻기");
        Assert.Contains("적도의", v.Current!.Center.Prompt);
    }

    [Fact]
    public void 설명은_두_조각을_줄을_바꿔_잇는다()
    {
        Assert.Equal("가?\n나.", PrepareRunner.JoinPrompt("가?", "나."));
        Assert.Equal("가?", PrepareRunner.JoinPrompt("가?", null));
        Assert.Equal("나.", PrepareRunner.JoinPrompt(null, "나."));
        Assert.Null(PrepareRunner.JoinPrompt(null, null));
    }

    [Fact]
    public void 표준_단어는_한_곳에서()
    {
        Assert.Equal("재시도", ButtonText.Retry);
        Assert.Equal("이대로 진행", ButtonText.Proceed);
        Assert.Equal("건너뛰기", ButtonText.Skip);
        Assert.Equal("완료", ButtonText.Done);
    }
}
