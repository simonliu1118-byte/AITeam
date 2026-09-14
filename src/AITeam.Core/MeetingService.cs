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
