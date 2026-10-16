using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Astro.Core;
using Astro.Core.Assistant;
using Astro.Core.Sky;
using Astro.Server.Assistant.Providers;
using Astro.Server.Sky;
using Microsoft.Extensions.Options;

namespace Astro.Server.Assistant;

/// <summary>계획 대화 — AI와 주고받기·시스템 지시문 (PlanAssistant 나눔, 상태 필드는 PlanAssistant.cs)</summary>
public sealed partial class PlanAssistant
{
    /// <summary>사용자 말 하나를 처리한다. 화면으로 보낼 이벤트: notice · text · plan · choices · error · done</summary>
    public async IAsyncEnumerable<SseItem<object>> SendAsync(string text, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!await _busy.WaitAsync(0, ct))
        {
            yield return Event("error", new { message = "앞의 답을 쓰는 중입니다. 잠시 기다려 주세요." });
            yield break;
        }
        try
        {
            if (await EnsureStartedAsync(ct) is { } problem)
            {
                yield return Event("error", new { message = problem });
                yield break;
            }
            var night = _night!;
            _display.Add(new DisplayMessage("user", text));
            _history.Add(ChatMessage.User(text));

            // AI를 쓸 수 없으면(설정·키 없음·오늘 한도) 대화하지 않는다 — 화면이 이름 검색만 보이게 (2026-10-14 사용자 결정 "AI가 없으면 이름 검색만".
            // 예전의 연습 대화는 지운 도구 모양을 기대해 대상을 고르면 오류가 났다 — 자체 리뷰)
            if (PracticeReason is { } reason)
            {
                ForgetLastUserMessage();
                yield return Event("practice", new { reason });
                yield break;
            }
            var model = models.Create();
            var said = new StringBuilder();
            IReadOnlyList<string>? choices = null;

            var askedAt = System.Diagnostics.Stopwatch.StartNew();
            for (var round = 0; round < MaxRounds; round++)
            {
                // 속도 확인용 기록 (2026-10-08 — 답이 느림): 이번 호출에 보낸 양·첫 글자까지·전체 시간
                var sent = SystemPrompt(night, _plan).Length + _history.Sum(m => m.Parts.Sum(p => p is TextPart t ? t.Text.Length : p is ToolResultPart r ? r.Result.GetRawText().Length : 50));
                var roundWatch = System.Diagnostics.Stopwatch.StartNew();
                long firstChunkMs = -1;
                var channel = System.Threading.Channels.Channel.CreateUnbounded<SseItem<object>>();
                ChatMessage? reply = null;
                string? failure = null;
                var failureKind = ChatFailure.Other;

                // 응답 스트림을 읽는 동안 글 조각을 바로 화면으로 흘려 보낸다
                var pump = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var chunk in model.StreamAsync(SystemPrompt(night, _plan), _history, PlanTools.Specs, ct))
                        {
                            if (firstChunkMs < 0) firstChunkMs = roundWatch.ElapsedMilliseconds;
                            if (chunk is TextDelta d)
                            {
                                said.Append(d.Text);
                                await channel.Writer.WriteAsync(Event("text", new { text = d.Text }), ct);
                            }
                            else if (chunk is ChatCompleted c) reply = c.Message;
                        }
                    }
                    catch (ChatModelException e) { failure = e.Message; failureKind = e.Kind; log.LogWarning(e, "AI 호출 실패"); }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
                    {
                        failure = "AI의 답을 받지 못했습니다. 잠시 뒤 다시 보내 주세요.";
                        log.LogWarning(e, "AI 응답 오류");
                    }
                    finally { channel.Writer.Complete(); }
                }, ct);

                await foreach (var item in channel.Reader.ReadAllAsync(ct)) yield return item;
                await pump;
                log.LogInformation("AI 호출 {Round}번째: 보낸 글자 약 {Sent} · 첫 응답 {First}ms · 전체 {Total}ms ({Model})", round + 1, sent, firstChunkMs, roundWatch.ElapsedMilliseconds, model.GetType().Name);

                // 한도 초과·키 문제: 이제부터 이름 검색만 (화면이 바꾼다)
                if (failure is not null && failureKind is ChatFailure.Quota or ChatFailure.Key)
                {
                    // 한도: 다시 차는 시각까지 / 키: 앱을 다시 켤 때까지 (키를 고치면 다시 켜야 하므로)
                    _practiceUntil = failureKind == ChatFailure.Quota ? NextQuotaReset() : DateTimeOffset.MaxValue;
                    _practiceReason = failureKind == ChatFailure.Quota ? "오늘 AI 사용 한도를 다 썼어요." : "AI 키에 문제가 있어요.";
                    if (round == 0) ForgetLastUserMessage();
                    yield return Event("practice", new { reason = _practiceReason });
                    yield break;
                }

                if (failure is not null || reply is null)
                {
                    // 실패한 사용자 말은 기록에서 빼서, 다시 보내면 처음처럼 이어지게 한다
                    if (round == 0 && _history.Count > 0 && _history[^1].Role == ChatRole.User) _history.RemoveAt(_history.Count - 1);
                    if (round == 0 && _display.Count > 0 && _display[^1].Role == "user") _display.RemoveAt(_display.Count - 1);
                    yield return Event("error", new { message = failure ?? "AI가 답하지 않았습니다." });
                    yield break;
                }

                _history.Add(reply);
                var calls = reply.Parts.OfType<ToolCallPart>().ToList();
                if (calls.Count == 0) break;

                var results = new List<ChatPart>();
                // 도구를 쓰는 동안 지금 하는 일을 화면에 (다음 AI 호출을 기다리는 동안 멈춘 것처럼 보이지 않게)
                yield return Event("status", new { message = Doing(calls[^1].Name) });
                foreach (var call in calls)
                {
                    JsonElement result;
                    IReadOnlyList<string>? offered;
                    lock (_planGate) result = tools.Run(call.Name, call.Arguments, _night ?? night, _plan, out offered);
                    log.LogInformation("도구 {Tool} {Args}", call.Name, call.Arguments.GetRawText());
                    results.Add(new ToolResultPart(call.Id, call.Name, result));
                    if (offered is not null)
                    {
                        choices = offered;
                        yield return Event("choices", new { choices });
                    }
                }
                yield return Event("plan", View());
                _history.Add(new ChatMessage(ChatRole.User, results));
            }

            _display.Add(new DisplayMessage("assistant", said.ToString().Trim(), choices));
            log.LogInformation("AI 답 하나: {Ms}ms", askedAt.ElapsedMilliseconds);
            yield return Event("done", new { });
        }
        finally
        {
            _busy.Release();
        }
    }

    private static SseItem<object> Event(string type, object data) => new(data, type);

    /// <summary>무료 한도가 다시 차는 시각: 미국 태평양 시각 자정 (한국 오후 4~5시)</summary>
    private static DateTimeOffset NextQuotaReset()
    {
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, pacific);
        var midnight = now.Date.AddDays(1);
        return new DateTimeOffset(midnight, pacific.GetUtcOffset(midnight));
    }

    private static string Hm(DateTimeOffset t) => t.ToString("HH:mm");

    /// <summary>첫 인사 (AI가 있을 때만). 오늘 밤 조건은 화면 오른쪽 위 한 줄에 늘 있으므로 되풀이하지 않는다</summary>
    private const string Greeting = "찍고 싶은 대상이 있으면 이름을 말해 주세요. 고민되면 오늘 밤 조건에 맞는 대상을 추천해 드릴게요.";

    /// <summary>오늘 밤 한 줄: "오늘은 19:20부터 05:05까지 어두워요. 달(9%)은 19:52에 져요. 01시쯤부터 구름이 많아진다는 예보예요."</summary>
    public static string NightSummary(NightContext n) =>
        $"오늘은 {Hm(n.Window.DarkStart)}부터 {Hm(n.Window.DarkEnd)}까지 어두워요. {MoonLine(n)} {CloudLine(n)}";

    private static string MoonLine(NightContext n)
    {
        var pct = (int)Math.Round(n.Moon.Illumination * 100);
        if (pct < 8 || n.Moon.UpIntervals.Count == 0) return "달이 없는 밤이에요.";
        var (s, e) = n.Moon.UpIntervals[0];
        if (s <= n.Window.DarkStart && e >= n.Window.DarkEnd) return $"달({pct}%)이 밤새 떠 있어요.";
        if (s <= n.Window.DarkStart) return $"달({pct}%)은 {Hm(e)}에 져요.";
        return $"달({pct}%)은 {Hm(s)}에 떠요.";
    }

    /// <summary>어두운 시간 동안의 구름 예보를 한 문장으로 (바뀌는 때만 시각을 붙인다)</summary>
    private static string CloudLine(NightContext n)
    {
        var hours = n.Clouds.Where(c => c.Time >= n.Window.DarkStart.AddMinutes(-30) && c.Time <= n.Window.DarkEnd).OrderBy(c => c.Time).ToList();
        if (hours.Count == 0) return "구름 예보는 받지 못했어요.";
        const int Clear = 30, Cloudy = 60;
        if (hours.All(c => c.Cover < Clear)) return "구름 없는 밤이라는 예보예요.";
        if (hours.All(c => c.Cover >= Cloudy)) return "밤새 구름이 많다는 예보예요.";
        if (hours[0].Cover < Cloudy && hours.FirstOrDefault(c => c.Cover >= Cloudy) is { } worse)
            return $"{worse.Time:HH}시쯤부터 구름이 많아진다는 예보예요.";
        if (hours[0].Cover >= Cloudy && hours.FirstOrDefault(c => c.Cover < Clear) is { } better)
            return $"{better.Time:HH}시쯤부터 구름이 걷힌다는 예보예요.";
        return "구름이 조금 있다는 예보예요.";
    }

    private static readonly JsonSerializerOptions PlanJson = new(JsonSerializerDefaults.Web);

    private string SystemPrompt(NightContext n, ShootingPlan plan)
    {
        var state = JsonSerializer.Serialize(View(), PlanJson);
        return $$"""
            너는 {{Product.Name}} 앱의 천체사진 촬영 비서다. 사용자와 대화해서 오늘 밤 촬영 계획을 함께 정한다. 계획 = 무엇을(대상) + 얼마나(촬영 설정).
            화면: 왼쪽이 대화, 오른쪽 위에 오늘 밤 한 줄("{{NightSummary(n)}}")과 고른 대상의 프레임 미리보기, 가운데에 일몰~일출 고도 그래프(고른 대상 최대 3개, 지금 대상만 밝게),
            아래에 촬영 설정 칸(필터 · 게인 · 노출 · 총 노출 · 촬영 횟수)과 이동 버튼이 있다. 사용자는 화면에서 직접 값을 고칠 수도 있다.
            화면은 이미 이렇게 인사했다: "{{Greeting}}"

            말하기
            - 한국어 존댓말(~요), 한 번에 2~3문장. 목록·표·마크다운을 쓰지 않는다.
            - 시각·고도·화각·장수 같은 숫자는 도구 결과나 아래 '지금 계획 상태'(화면에 보이는 값)에 있는 값만 쓴다. 추측해서 만들지 않는다.
            - 대상 이름은 번호와 한국어 통칭을 함께 (예: M45 플레이아데스).
            - 관측지 위치(위도·경도·지명)는 말하지 않는다.

            진행
            - 사용자가 정해 온 대상이 있으면 추천부터 하지 말고 search_targets로 찾아 pick_target으로 고른다. 추천을 원하면 suggest_targets.
            - 대상을 고르면 언제부터 언제까지 찍을 수 있는지, 화면을 얼마나 채우는지 한 문장으로 알려 준다. 기다려야 하면 그 까닭(어두워짐·고도)을 말한다.
            - 여러 대상을 비교하고 싶어 하면 차례로 pick_target — 그래프에 최대 3개까지 함께 그려진다.
            - 설정을 바꿔 달라고 하면(예: "노출 90초로", "내로우밴드 끼웠어", "2시간만 찍을래") set_settings로 바로 바꾸고 바뀐 결과를 한 문장으로 말한다.
            - 더 높이 올라온 뒤에 시작하고 싶다고 하면 set_start. 시작은 화면 그래프의 손잡이로도 옮길 수 있다고 알려 줘도 좋다.
            - 달이 50% 넘게 밝고 대상과 30° 안쪽이면 그 사실을 알리고, 듀얼 내로우밴드 필터나 달과 먼 대상을 제안한다. 사용자가 그대로 찍겠다면 따른다.
            - 오늘 밤 찍을 수 없는 대상(찍을 수 있는 시간 없음)이면 그렇게 알리고 다른 대상을 제안한다.
            - 질문할 때는 offer_choices로 선택지(2~4개)를 먼저 보여 준 다음 질문한다. 한 번에 한 가지만 묻는다.
            - 구도(대상은 가운데, 카메라 방향은 지금 그대로)와 끝난 뒤(파킹·다크·플랫)는 묻지 않는다.
            - 계획이 정해지면 "오른쪽 아래 이동 버튼을 누르면 적도의가 대상으로 움직여요"라고 짧게 안내한다. 이 대화에서는 적도의를 움직이거나 촬영을 시작하지 않는다.
            - 대화 도중 장비나 시각이 바뀔 수 있다. 장비·화각·계획 값은 앞의 대화가 아니라 아래 '지금 계획 상태'와 get_tonight 결과를 기준으로 한다.
            - 촬영과 관계없는 이야기는 짧게 답하고 계획으로 돌아온다.

            지금 계획 상태(JSON, 화면에 보이는 값): {{state}}
            """;
    }
}
