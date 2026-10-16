
namespace Astro.Server.Prepare.Flow;

/// <summary>준비 진행자 — 작업이 화면을 갱신하는 손잡이 (PrepareRunner 나눔, 상태 필드는 PrepareRunner.cs)</summary>
public sealed partial class PrepareRunner
{
    private sealed class RunHandle(PrepareRunner r, int runId) : ITaskRun
    {
        public PrepContext Context => r._ctx!;
        public int RunId => runId;

        // 작업이 화면에 보이는 것을 기록 파일에도 (2026-10-10 — 모든 단계 로그). 계속 들어오는 측정값(live)은 남기지 않는다
        private string Task => r._cur?.Task.Id ?? "?";

        public void SubStep(int index)
        {
            r.Log.LogInformation("[{Task}] 세부 과정 {Index}", Task, index);
            r.Update(runId, c => c.SubIndex = index);
        }

        public void Guide(string title, string text)
        {
            r.Log.LogInformation("[{Task}] 안내: {Title} — {Text}", Task, title, text);
            r.Update(runId, c => c.Guide = new GuideView(title, text));
        }

        public void Readout(string kind, string big, string caption, Tone tone, IReadOnlyDictionary<string, double>? values = null, bool live = false, bool unverified = false, DateTimeOffset? observedAt = null) =>
            r.Update(runId, c =>
            {
                if (!live) r.Log.LogInformation("[{Task}] 결과 {Kind}: {Big} — {Caption} ({Tone})", Task, kind, big, caption, tone);
                c.Readout = new ReadoutView(kind, big, caption, tone, values ?? new Dictionary<string, double>(), observedAt ?? DateTimeOffset.Now,
                    unverified ? Freshness.Unverified : Freshness.Fresh);
                c.ReadoutLive = live;
            });

        public void ClearReadout() => r.Update(runId, c => { c.Readout = null; c.ReadoutLive = false; });
        public void Status(string? text, Tone tone = Tone.Busy)
        {
            if (text is not null) r.Log.Log(tone is Tone.Fail ? LogLevel.Warning : LogLevel.Information, "[{Task}] 상태({Tone}): {Text}", Task, tone, text);
            r.Update(runId, c => c.Status = text is null ? null : new StatusLine(text, tone));
        }
        public void Live(string kind, string? url = null, object? data = null) => r.Update(runId, c => c.Live = new LiveView(kind, url, data, DateTimeOffset.Now));

        public Task<string> AskAsync(IReadOnlyList<PrepAction> actions, CancellationToken ct) => AskAsync(null, actions, ct);

        public async Task<string> AskAsync(string? prompt, IReadOnlyList<PrepAction> actions, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!r.Update(runId, c =>
                {
                    c.Ask?.TrySetCanceled();
                    c.Ask = tcs;
                    c.Show(actions, prompt);
                    r._rows[c.Task.Id] = (PrepTaskStatus.Waiting, null);
                }))
                throw new OperationCanceledException();
            // 취소되면(작업이 다른 길로 감) 그 버튼을 화면에서 바로 치운다
            await using var reg = ct.Register(() =>
            {
                if (tcs.TrySetCanceled(ct))
                    r.Update(runId, c => { if (c.Ask == tcs) { c.Ask = null; c.Actions = []; } });
            });
            r.Log.LogInformation("[{Task}] 물음: {Actions}", Task, string.Join(" / ", actions.Select(a => a.Label)));
            var picked = await tcs.Task;
            r.Log.LogInformation("[{Task}] 고름: {Picked}", Task, picked);
            return picked;
        }
    }
}
