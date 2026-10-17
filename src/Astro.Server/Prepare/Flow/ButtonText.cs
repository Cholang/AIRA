namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 버튼 단어 표준 (DESIGN.md "버튼 단어 표준", 2026-10-17 사용자 결정) — 같은 뜻이면 늘 같은 단어. 무엇이 되는지·이유·주의는 버튼 밖
/// 설명(버튼 레이아웃 영역, <see cref="ITaskRun.AskAsync(string?, IReadOnlyList{PrepAction}, CancellationToken)"/>의 prompt)에.
/// 단어는 화면 문구 표(Astro.Core.UiText, UiText.json의 button.*)에 있고, 웹(web/src/buttonText.ts)도 같은 키를 읽는다 (2026-10-18)
/// </summary>
public static class ButtonText
{
    /// <summary>방금 실패한 것을 아이라가 다시</summary>
    public static string Retry => Core.UiText.Get("button.retry");
    /// <summary>경고·실패를 알고 지금 상태로 넘어감</summary>
    public static string Proceed => Core.UiText.Get("button.proceed");
    /// <summary>이 단계를 하지 않고 다음으로</summary>
    public static string Skip => Core.UiText.Get("button.skip");
    /// <summary>사람이 손으로 한 일을 끝냄 (할 일은 설명에)</summary>
    public static string Done => Core.UiText.Get("button.done");
    /// <summary>장비 상태를 다시 읽음</summary>
    public static string Recheck => Core.UiText.Get("button.recheck");
    /// <summary>끝난 단계를 처음부터 다시</summary>
    public static string Redo => Core.UiText.Get("button.redo");
}
