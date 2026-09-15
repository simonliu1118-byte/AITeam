using AITeam.Models;

namespace AITeam.Services;

public enum MeetingMode
{
    /// <summary>輪流發言：後面的人看得到前面的人說了什麼，會收斂。</summary>
    RoundRobin,

    /// <summary>各自作答：彼此看不到，會發散；適合問「有哪些做法」。</summary>
    Parallel
}

public enum MeetingScale
{
    Short,
    Standard,
    Deep
}

public static class MeetingScaleExtensions
{
    public static string ToFriendlyName(this MeetingScale scale) => scale switch
    {
        MeetingScale.Short => "簡短",
        MeetingScale.Standard => "標準",
        MeetingScale.Deep => "深入",
        _ => scale.ToString()
    };

    /// <summary>
    /// 每一輪的字數上限。AI 感知不到時間，跟它說「你有 60 秒」沒有任何效果；
    /// 字數才是它聽得懂也控制得了的東西。這個上限真正的目的也不是省錢
    /// （省下的只有輸出那一小塊），而是讓使用者讀得完。
    /// </summary>
    public static int WordBudget(this MeetingScale scale) => scale switch
    {
        MeetingScale.Short => 300,
        MeetingScale.Deep => 2000,
        _ => 800
    };

    /// <summary>
    /// 一位發言者最多能想多久。以前三種規模都給 15 分鐘，結果「簡短 300 字」的題目
    /// 也可以讓某家 AI 翻半小時的檔——規模說的是這一輪要投入多少，時間上限也該跟著。
    /// </summary>
    public static TimeSpan HardTimeout(this MeetingScale scale) => scale switch
    {
        MeetingScale.Short => TimeSpan.FromMinutes(5),
        MeetingScale.Deep => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromMinutes(10)
    };

    /// <summary>
    /// 要 AI 讀專案之前，先講清楚該讀多少。不講的話它會把整個 repo 掃過一遍，
    /// 三百字的題目也能花上十分鐘。
    /// </summary>
    public static string ExplorationBudget(this MeetingScale scale) => scale switch
    {
        MeetingScale.Short =>
            "Look at a handful of clearly relevant files at most. Do not survey the repository, "
            + "do not list directories exhaustively, and do not read files that are only tangentially related. "
            + "If something needs deeper investigation than that, say so instead of doing it.",
        MeetingScale.Deep =>
            "You may investigate thoroughly, but stop once you have enough to take a position.",
        _ =>
            "Read the files that matter for this question and stop there; do not survey the whole repository."
    };
}

/// <summary>會議交出去的東西到底是什麼。這會決定我們怎麼跟 Planner 介紹它。</summary>
public enum MeetingConclusionKind
{
    /// <summary>沒有收斂過，只是把每位參與者最後的立場並排起來。彼此可能互相矛盾。</summary>
    UnconvergedSummary,

    /// <summary>某一家 AI 讀完整場會議之後寫出來的定案書。</summary>
    Decision
}

/// <summary>
/// 一場會議談完之後要交給修改管線的東西。會議只負責「談出結論」，
/// 要不要做、對哪個專案做，由使用者在交接時決定。
/// </summary>
public sealed record MeetingConclusion(
    string Topic,
    string? ProjectName,
    string Text,
    MeetingConclusionKind Kind = MeetingConclusionKind.UnconvergedSummary,
    ProviderId? Writer = null)
{
    /// <summary>
    /// 交給 Planner 時掛在需求後面的那一段背景。措辭一定要跟實情一致：
    /// 把「三個人各自的立場」介紹成「使用者確認過的定案」，Planner 就會照著一個
    /// 根本沒定案的東西動手改程式——它不會問，因為我們告訴它不用問。
    /// </summary>
    public string ComposeBackground() => Kind == MeetingConclusionKind.Decision
        ? "以下是先前 AI 四方會議的定案書"
          + (Writer is { } writer ? $"（由 {writer.ToFriendlyName()} 整理）" : "")
          + "，已經由使用者確認，請當作背景採用，不要重新討論這些已經決定好的事。"
          + "但「還沒決定的事」那一段是尚未定案的，遇到那些問題要先問使用者，不要自己決定。"
          + Environment.NewLine + Text
        : "以下是先前 AI 四方會議的討論摘要。這場會議沒有收斂出定案，下面只是每位參與者"
          + "最後的立場，彼此可能互相矛盾。請把它當成參考背景，不要當成已經決定好的事；"
          + "真正要做什麼以使用者的需求為準，有疑問就先問，不要自己挑一個立場執行。"
          + Environment.NewLine + Text;
}

public sealed record MeetingSetup(
    string Topic,
    ProjectEntry? Project,
    MeetingMode Mode,
    MeetingScale Scale);

/// <summary>會議逐字稿裡的一則發言。Speaker 為 null 代表是使用者說的。</summary>
public sealed record MeetingRemark(
    int Round,
    ProviderId? Speaker,
    string Text,
    MeetingScale? SuggestedScale = null,
    bool Failed = false,
    TimeSpan Elapsed = default)
{
    public string SpeakerName => Speaker?.ToFriendlyName() ?? "你";
}

public sealed class MeetingService
{
    private readonly string _runtimeRoot;
    private readonly IProcessRunner _runner;
    private readonly GitRepositoryService _git;
    private readonly ProviderClient _providers;

    private string? _sandboxRoot;
    private string? _workingDirectory;
    private ProjectEntry? _sandboxProject;

    public MeetingService(string runtimeRoot, IProcessRunner runner)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
        _git = new GitRepositoryService(runtimeRoot, runner);
        _providers = new ProviderClient(runtimeRoot, runner, new RuntimeConfigService(runtimeRoot).LoadAgentsConfig());
    }

    /// <summary>
    /// 有選專案時開一個唯讀副本，三家 AI 才能真的打開程式碼來討論，
    /// 而不是憑空談。沒選專案就什麼都不用準備。
    /// </summary>
    public async Task PrepareAsync(ProjectEntry? project, Action<string> progress, CancellationToken cancellationToken)
    {
        await CleanupAsync();
        if (project is null) return;

        if (!Directory.Exists(project.RepoPath))
            throw new DirectoryNotFoundException($"找不到專案 Repo：{project.RepoPath}");

        progress("同步專案並建立唯讀討論副本…");
        await _git.SafeSyncAsync(project.RepoPath, project.DefaultBranch, cancellationToken);

        var sandbox = Path.Combine(_runtimeRoot, "tasks", "meeting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(sandbox)!);
        var add = await _runner.RunAsync(
            "git",
            new[] { "worktree", "add", "--detach", sandbox, "HEAD" },
            project.RepoPath,
            null,
            TimeSpan.FromSeconds(60),
            cancellationToken);
        if (add.ExitCode != 0)
            throw new InvalidOperationException("無法建立會議用的唯讀副本：" + FirstLine(add.StandardError, add.StandardOutput));

        _sandboxRoot = sandbox;
        _sandboxProject = project;
        _workingDirectory = string.IsNullOrWhiteSpace(project.RepoSubpath)
            ? sandbox
            : Path.Combine(sandbox, project.RepoSubpath.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(_workingDirectory))
            throw new DirectoryNotFoundException($"副本裡找不到專案目錄：{project.RepoSubpath}");
    }

    public async Task CleanupAsync()
    {
        if (_sandboxRoot is null || _sandboxProject is null) return;

        var sandbox = _sandboxRoot;
        var project = _sandboxProject;
        _sandboxRoot = null;
        _sandboxProject = null;
        _workingDirectory = null;

        try
        {
            await _runner.RunAsync(
                "git",
                new[] { "worktree", "remove", "--force", sandbox },
                project.RepoPath,
                null,
                TimeSpan.FromMinutes(1),
                CancellationToken.None);
        }
        catch
        {
            // 收不掉副本不該讓使用者看到錯誤，下次開會議會再建一個新的。
        }

        try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true); } catch { }
    }

    /// <summary>
    /// 請某一家 AI 就目前的逐字稿發言一次。逾時由呼叫端決定（畫面上有軟／硬兩段逾時），
    /// 被取消時前面已經吐出來的內容會留在串流紀錄裡，不是整段丟掉。
    /// </summary>
    public async Task<MeetingRemark> AskAsync(
        ProviderId speaker,
        MeetingSetup setup,
        IReadOnlyList<MeetingRemark> transcript,
        int round,
        MeetingScale scale,
        TimeSpan timeout,
        Action<string>? onActivity,
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(speaker, setup, transcript, round, scale);
        var workingDirectory = _workingDirectory ?? _runtimeRoot;

        // 記下每位發言者實際花了多久。三家的速度差很多（Antigravity 光啟動就比另外兩家慢），
        // 把時間寫在發言標題上，慢到不合理的時候使用者一眼就看得出來是哪一家。
        var started = DateTime.UtcNow;
        var answer = await _providers.AskAsync(speaker, workingDirectory, prompt, timeout, onActivity, cancellationToken);
        var (text, suggested) = ParseRemark(answer);
        return new MeetingRemark(round, speaker, text, suggested, Elapsed: DateTime.UtcNow - started);
    }

    /// <summary>
    /// 請一家 AI 把整場會議寫成一份定案書。
    ///
    /// 為什麼要有這一步：以前交給修改管線的是 <see cref="Summarise"/> 湊出來的東西——
    /// 每家最後一則發言壓成一行、砍到 220 字並排。三家意見不同時，那是三段互相打架的話，
    /// 沒有任何一句說「所以我們決定怎麼做」，而且真正的做法細節通常就在被砍掉的後半。
    ///
    /// 只叫一家寫（不是三家各寫一份），因為定案書要的就是「一份」。
    /// </summary>
    public async Task<string> DraftDecisionAsync(
        ProviderId writer,
        MeetingSetup setup,
        IReadOnlyList<MeetingRemark> transcript,
        TimeSpan timeout,
        Action<string>? onActivity,
        CancellationToken cancellationToken)
    {
        var prompt = BuildDecisionPrompt(writer, setup, transcript);
        var workingDirectory = _workingDirectory ?? _runtimeRoot;
        var answer = await _providers.AskAsync(writer, workingDirectory, prompt, timeout, onActivity, cancellationToken);
        return answer.Trim();
    }

    /// <summary>定案書的五個欄位。順序固定，因為使用者要照這個順序讀它。</summary>
    internal static readonly string[] DecisionSections =
    {
        "要做什麼：",
        "為什麼這樣做：",
        "具體做法：",
        "明確不做／已經排除的選項：",
        "還沒決定的事："
    };

    internal static string BuildDecisionPrompt(
        ProviderId writer,
        MeetingSetup setup,
        IReadOnlyList<MeetingRemark> transcript)
    {
        var usable = transcript.Where(r => !r.Failed).ToList();
        var history = usable.Count == 0
            ? "（還沒有任何發言。）"
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                usable.Select(r => $"【第 {r.Round} 輪 · {r.SpeakerName}】{Environment.NewLine}{r.Text}"));

        var sections = string.Join(Environment.NewLine, DecisionSections);

        return $"""
You are acting as the MINUTE-TAKER for an AITeam round-table discussion, in the seat of "{writer.ToFriendlyName()}". Your job is to turn the transcript below into one decision document.

You are NOT defending your own earlier position. You took part in this discussion, so be deliberate about this: where participants disagreed, do not quietly pick your own side. Either the transcript shows the disagreement was settled — then say what was settled and why — or it was not, and it belongs under the last heading as still open.

Use ONLY what is in the transcript. Do not open or read any project files, do not survey the repository, and do not add anything the participants did not actually say. If something important was never decided, that is a finding, not a gap for you to fill in.

This document will be handed to another AI that modifies real code. Anything you write under the first four headings will be treated as already decided and will be acted on without further discussion — so put anything that is not actually settled under the last heading instead.

Topic:
{setup.Topic}

Transcript:
{history}

Write the document in Traditional Chinese (繁體中文), using exactly these five headings, in this order, each on its own line with its content underneath:

{sections}

Rules for the content:
- Be concrete and specific. "改善效能" is useless; "把 X 的查詢改成批次，一次抓 100 筆" is useful.
- Do not write a preamble, a greeting, or a closing remark. Start with the first heading.
- Do not quote the transcript at length; write the conclusion, not a replay of the discussion.
- If a heading genuinely has nothing under it, write 「（無）」 under it rather than deleting the heading.
- Under the last heading, also list anything the participants disagreed about and never resolved, naming who wanted what.
""";
    }

    /// <summary>
    /// 這一輪由誰先講。每輪換人開頭，避免固定某一家永遠定調——第一個發言的人
    /// 對整輪的走向影響最大。
    /// </summary>
    public static IReadOnlyList<ProviderId> SpeakingOrder(IReadOnlyList<ProviderId> participants, int round)
    {
        if (participants.Count == 0) return participants;

        var offset = (round - 1) % participants.Count;
        if (offset < 0) offset += participants.Count;

        return Enumerable.Range(0, participants.Count)
            .Select(i => participants[(i + offset) % participants.Count])
            .ToList();
    }

    internal static string BuildPrompt(
        ProviderId speaker,
        MeetingSetup setup,
        IReadOnlyList<MeetingRemark> transcript,
        int round,
        MeetingScale scale)
    {
        // 失敗或被跳過的那幾則沒有內容（「（這一輪失敗，沒有發言。）」），
        // 放進逐字稿只會變成雜訊，還可能被後面的人拿去發揮。
        var usable = transcript.Where(r => !r.Failed).ToList();

        var visible = setup.Mode == MeetingMode.RoundRobin
            ? usable
            // 各自作答：只看得到使用者說過的話，看不到其他 AI 的發言。
            : usable.Where(r => r.Speaker is null).ToList();

        var history = visible.Count == 0
            ? "（還沒有任何發言。）"
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                visible.Select(r => $"【第 {r.Round} 輪 · {r.SpeakerName}】{Environment.NewLine}{r.Text}"));

        var projectSection = setup.Project is null
            ? "This discussion is not tied to a specific project. Answer from general engineering judgement and say so when something depends on details you cannot see."
            : ChangeTaskService.DescribeProject(setup.Project) + Environment.NewLine
              + "You have a read-only copy of this project. Open the actual files before making claims about it. "
              + scale.ExplorationBudget();

        var modeSection = setup.Mode == MeetingMode.RoundRobin
            ? """
You can see what the other participants said. You are NOT required to agree with them: if you disagree, quote the specific sentence and say why. Repeating or merely endorsing someone else's point wastes this round — add something they did not say.
"""
            : """
You cannot see the other participants' answers this round, and that is deliberate: answer independently so the differences between us are real.
""";

        return $"""
You are a participant in an AITeam round-table discussion, speaking as "{speaker.ToFriendlyName()}". You are one of three AI participants; the human is the chair and speaks last each round. Do not modify any files.

This is a discussion, not a task. You are not implementing anything, not reporting on work, and not coordinating with anyone. Your entire reply IS your spoken contribution — it will be shown verbatim to the other participants and to the human. Never say that you have "already posted" or "already found" something, never say "no action needed" or "nothing to add until the next round", and never describe what you are about to do: just say your piece. This is your first and only turn this round, whatever any earlier context might suggest.

Write in Traditional Chinese (繁體中文). This is required even if the transcript above contains English.

Topic:
{setup.Topic}

{projectSection}

{modeSection}
Length limit for this turn: about {scale.WordBudget()} Traditional Chinese characters. Staying well under it is fine; going far over it is not.

Transcript so far:
{history}

Now write your turn {round} contribution, in Traditional Chinese. Be concrete and take a position; do not summarise the discussion back to us, and do not comment on the discussion process itself.

If — and only if — you believe the length limit is badly wrong for this topic, add exactly one final line in this form (the human decides whether to accept it; it will not change anything by itself):
AITeamScale: SHORT | STANDARD | DEEP
""";
    }

    /// <summary>
    /// 會議的結果摘要。以前直接把最後一位發言者的內容當摘要，但最後一個講的人
    /// 不見得代表這場會議的結論——而且如果他只是補一句話，摘要就變得莫名其妙。
    /// 改成寫成「每個人最後的立場」：這才是看歷史紀錄時真正想知道的事。
    /// </summary>
    public static string Summarise(IReadOnlyList<MeetingRemark> transcript, int rounds, int calls)
    {
        var spoken = transcript.Where(r => r.Speaker is not null && !r.Failed).ToList();
        if (spoken.Count == 0) return "（沒有任何 AI 發言。）";

        var positions = spoken
            .GroupBy(r => r.Speaker!.Value)
            .Select(group => group.Last())
            .OrderBy(r => r.Round)
            .Select(r => $"● {r.SpeakerName}：{TextSummary.OneLine(r.Text, 220)}");

        return $"共 {rounds} 輪 · 呼叫 {calls} 次{Environment.NewLine}{Environment.NewLine}"
               + string.Join(Environment.NewLine, positions);
    }

    internal static (string Text, MeetingScale? SuggestedScale) ParseRemark(string answer)
    {
        var text = (answer ?? "").Trim();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

        MeetingScale? suggested = null;
        for (var i = lines.Count - 1; i >= 0 && i >= lines.Count - 3; i--)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("AITeamScale:", StringComparison.OrdinalIgnoreCase)) continue;

            var value = line["AITeamScale:".Length..].Trim().ToUpperInvariant();
            suggested = value switch
            {
                "SHORT" => MeetingScale.Short,
                "STANDARD" => MeetingScale.Standard,
                "DEEP" => MeetingScale.Deep,
                _ => null
            };
            lines.RemoveAt(i);
            break;
        }

        return (string.Join(Environment.NewLine, lines).Trim(), suggested);
    }

    private static string FirstLine(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var line = candidate?
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
                ?.Trim();
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }
        return "未知錯誤";
    }
}
