namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 버튼 단어 표준 (DESIGN.md "버튼 단어 표준", 2026-10-17 사용자 결정) — 같은 뜻이면 늘 같은 단어. 무엇이 되는지·이유·주의는 버튼 밖
/// 설명(버튼 레이아웃 영역, <see cref="ITaskRun.AskAsync(string?, IReadOnlyList{PrepAction}, CancellationToken)"/>의 prompt)에.
/// 웹은 web/src/buttonText.ts에 같은 단어
/// </summary>
public static class ButtonText
{
    /// <summary>방금 실패한 것을 아이라가 다시</summary>
    public const string Retry = "재시도";
    /// <summary>경고·실패를 알고 지금 상태로 넘어감</summary>
    public const string Proceed = "이대로 진행";
    /// <summary>이 단계를 하지 않고 다음으로</summary>
    public const string Skip = "건너뛰기";
    /// <summary>사람이 손으로 한 일을 끝냄 (할 일은 설명에)</summary>
    public const string Done = "완료";
    /// <summary>장비 상태를 다시 읽음</summary>
    public const string Recheck = "다시 확인";
    /// <summary>끝난 단계를 처음부터 다시</summary>
    public const string Redo = "다시 하기";
}
