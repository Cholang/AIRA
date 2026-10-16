using Astro.Core.Assistant;
using Astro.Server.Assistant.Providers;
using Microsoft.Extensions.Options;

namespace Astro.Server.Assistant;

public sealed class AssistantOptions
{
    /// <summary>gemini · scripted(AI 없음 — 계획 화면은 이름 검색만). 새 회사를 붙이면 여기에 이름을 더한다 (ChatModelFactory)</summary>
    public string Provider { get; set; } = "gemini";

    /// <summary>앞에서부터 시도한다. 과부하면 다음 모델로</summary>
    public string[] Models { get; set; } = [];

    /// <summary>생각의 깊이: low · medium · high. 회사마다 이름이 달라 번역기가 맞춘다 (대화는 low면 충분하고 빠르다)</summary>
    public string Effort { get; set; } = "low";

    /// <summary>회사별 키. appsettings.json에는 두지 않고 사용자 비밀 저장소·환경 변수에서만 읽는다</summary>
    public Dictionary<string, string> ApiKeys { get; set; } = [];
}

/// <summary>설정의 Provider 값으로 AI 구현을 고른다. 회사를 바꾸려면 설정 한 줄만 바꾼다.</summary>
public sealed class ChatModelFactory(IOptions<AssistantOptions> options, IHttpClientFactory httpFactory, Setup.UserSettings? settings = null)
{
    /// <summary>쓸 키: 설정 화면에서 넣은 키가 먼저, 없으면 사용자 비밀 저장소·환경 변수 (2026-10-11 설정 화면)</summary>
    private string Key(AssistantOptions o) =>
        (o.Provider == "gemini" ? settings?.GeminiKey : null) ?? o.ApiKeys.GetValueOrDefault(o.Provider) ?? "";

    /// <summary>실제 AI를 쓸 수 없는 이유 (설정이 AI 없음이거나 키가 없음) — 화면에 "… 대상은 이름으로 찾을 수 있어요" 앞에 붙는다. 쓸 수 있으면 null</summary>
    public string? PracticeReason
    {
        get
        {
            var o = options.Value;
            if (o.Provider == "scripted") return "AI 비서를 쓰지 않는 설정이에요.";
            if (settings is { AiEnabled: false }) return "설정에서 AI 비서를 꺼 두었어요.";
            if (string.IsNullOrWhiteSpace(Key(o))) return "AI 키가 없어요(설정에서 넣을 수 있어요).";
            return null;
        }
    }

    public IChatModel Create()
    {
        var o = options.Value;
        var key = Key(o);
        switch (o.Provider)
        {
            case "gemini":
                var http = httpFactory.CreateClient("assistant");
                http.Timeout = TimeSpan.FromSeconds(90);
                return new GeminiChatModel(http, key, o.Models.Length > 0 ? o.Models : ["gemini-flash-latest"], o.Effort);
            default:
                throw new ChatModelException($"모르는 AI 회사입니다: {o.Provider}", ChatFailure.Key);
        }
    }

}

/// <summary>화면에 보여 줄 대화 한 줄 (도구 호출 등은 빼고 글만)</summary>
public sealed record DisplayMessage(string Role, string Text, IReadOnlyList<string>? Choices = null);
