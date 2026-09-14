using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

public enum RequestIntent
{
    Inquiry,
    Change
}

public sealed record InquiryResult(
    RequestIntent Intent,
    ProviderId Provider,
    string Answer,
    ChangeTaskResult? Change = null,
    string Subject = "");

public sealed class ChangePipelineDispatchException : Exception
{
    public ChangePipelineDispatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class InquiryService
{
    private readonly string _runtimeRoot;
    private readonly IProcessRunner _runner;

    // 這一輪任務要把「AI 正在做什麼」送到哪裡；同一時間只會有一個任務在跑。
    private Action<string>? _onActivity;
    private TaskInteraction _interaction = TaskInteraction.None;
    private readonly ProviderClient _providers;
    private readonly ChangeTaskService _changeTaskService;
    private readonly GitRepositoryService _git;
    private readonly AgentsConfig _agents;

    public InquiryService(string runtimeRoot, IProcessRunner runner)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
        _changeTaskService = new ChangeTaskService(runtimeRoot, runner);
        _git = new GitRepositoryService(runtimeRoot, runner);
        _agents = new RuntimeConfigService(runtimeRoot).LoadAgentsConfig();
        // _agents 要先讀好再建 ProviderClient，不然傳進去的是還沒指派的欄位。
        _providers = new ProviderClient(runtimeRoot, runner, _agents);
    }

    public async Task<InquiryResult> RunAsync(
        ProjectEntry project,
        string request,
        IReadOnlyList<ProviderId> candidates,
        Func<PlanGatePrompt, CancellationToken, Task<PlanGateResponse>> askUser,
        Action<string> progress,
        Action<TaskProgress> onStage,
        Action<string> onActivity,
        TaskInteraction interaction,
        CancellationToken cancellationToken)
    {
        _onActivity = onActivity;
        _interaction = interaction;

        if (candidates.Count == 0)
            throw new InvalidOperationException("目前沒有可用的 AI。請先重新檢查 AI 狀態。");
        if (!Directory.Exists(project.RepoPath))
            throw new DirectoryNotFoundException($"找不到專案 Repo：{project.RepoPath}");

        var sandboxRoot = Path.Combine(_runtimeRoot, "tasks", "inquiry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(sandboxRoot)!);
        var worktreeAdded = false;

        try
        {
            onStage(new TaskProgress(TaskKind.Inquiry, TaskStage.Prepare, TaskActivity.Running, "同步 GitHub 預設分支、確認本機 Repo 狀態"));
            progress("同步 GitHub 預設分支並確認本機 Repo 安全狀態…");
            await _git.SafeSyncAsync(project.RepoPath, project.DefaultBranch, cancellationToken);

            onStage(new TaskProgress(TaskKind.Inquiry, TaskStage.Prepare, TaskActivity.Running, "建立唯讀查詢隔離區"));
            progress("建立唯讀查詢隔離區…");
            var add = await _runner.RunAsync(
                "git",
                new[] { "worktree", "add", "--detach", sandboxRoot, "HEAD" },
                project.RepoPath,
                null,
                TimeSpan.FromSeconds(60),
                cancellationToken);
            if (add.ExitCode != 0)
                throw new InvalidOperationException("無法建立查詢隔離區：" + FirstUsefulLine(add.StandardError, add.StandardOutput));
            worktreeAdded = true;

            var workingDirectory = string.IsNullOrWhiteSpace(project.RepoSubpath)
                ? sandboxRoot
                : Path.Combine(sandboxRoot, project.RepoSubpath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(workingDirectory))
                throw new DirectoryNotFoundException($"隔離區內找不到專案目錄：{project.RepoSubpath}");

            var prompt = BuildPrompt(project, request);
            var failures = new List<string>();

            foreach (var provider in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onStage(new TaskProgress(TaskKind.Inquiry, TaskStage.Inquire, TaskActivity.Running, "讀取專案並判斷需求", provider));
                progress($"{provider.ToFriendlyName()} 正在讀取專案並判斷需求…");
                try
                {
                    var answer = await RunWithNotesAsync(provider, workingDirectory, prompt, progress, cancellationToken);
                    if (string.IsNullOrWhiteSpace(answer))
                        throw new InvalidOperationException("AI 沒有回傳可用內容。");

                    var parsed = Parse(provider, answer);
                    if (parsed.Intent == RequestIntent.Change)
                    {
                        progress("已辨識為修改任務，切換到完整多 AI 修改管線…");
                        try
                        {
                            var change = await _changeTaskService.RunAsync(
                                project,
                                request,
                                candidates,
                                askUser,
                                progress,
                                onStage,
                                onActivity,
                                interaction,
                                cancellationToken);

                            var summary = $"{change.Summary}\r\nRisk：{change.Risk}\r\nImplementer：{change.Implementer.ToFriendlyName()}\r\nFinal Review：{change.FinalReviewer.ToFriendlyName()}";
                            return new InquiryResult(RequestIntent.Change, change.FinalReviewer, summary, change, parsed.Subject);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            throw new ChangePipelineDispatchException(ex.Message, ex);
                        }
                    }

                    return parsed;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not ChangePipelineDispatchException)
                {
                    failures.Add($"{provider.ToFriendlyName()}：{ex.Message}");
                    // 失敗原因一定要當場寫進 log：原本只收進 failures，而 failures 只有在
                    // 「所有 AI 都失敗」時才會顯示，只要有別的 AI 接手成功，原因就永遠看不到了。
                    progress($"{provider.ToFriendlyName()} 本次失敗，改試下一個 AI。原因：{OneLine(ex.Message, 300)}");
                }
            }

            throw new InvalidOperationException("所有可用 AI 都無法完成本次工作。\r\n" + string.Join("\r\n", failures));
        }
        finally
        {
            if (worktreeAdded)
            {
                try
                {
                    await _runner.RunAsync(
                        "git",
                        new[] { "worktree", "remove", "--force", sandboxRoot },
                        project.RepoPath,
                        null,
                        TimeSpan.FromSeconds(45),
                        CancellationToken.None);
                }
                catch { }
            }
            try
            {
                if (Directory.Exists(sandboxRoot)) Directory.Delete(sandboxRoot, true);
            }
            catch { }
        }
    }

    /// <summary>
    /// 派工前先把使用者排隊中的補充併進提示；選「立刻套用」時中斷這一步、帶著補充重來。
    /// </summary>
    private async Task<string> RunWithNotesAsync(
        ProviderId provider,
        string workingDirectory,
        string basePrompt,
        Action<string> progress,
        CancellationToken cancellationToken)
    {
        var carried = new List<string>();

        for (var attempt = 0; ; attempt++)
        {
            carried.AddRange(_interaction.Notes.Take());
            var prompt = ChangeTaskService.ComposeWithNotes(basePrompt, carried);

            var step = _interaction.Notes.BeginStep(cancellationToken);
            try
            {
                return await RunProviderAsync(provider, workingDirectory, prompt, step.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 5)
            {
                progress($"依你的要求中斷 {provider.ToFriendlyName()} 這一步，帶著你的補充重新開始…");
            }
            finally
            {
                _interaction.Notes.EndStep(step);
            }
        }
    }

    private Task<string> RunProviderAsync(
        ProviderId provider,
        string workingDirectory,
        string prompt,
        CancellationToken cancellationToken) =>
        _providers.AskAsync(
            provider,
            workingDirectory,
            prompt,
            provider == ProviderId.Antigravity ? TimeSpan.FromMinutes(6) : TimeSpan.FromMinutes(5),
            _onActivity,
            cancellationToken);

    private static string BuildPrompt(ProjectEntry project, string request) => $"""
You are the AITeam read-only request gate for project "{project.Name}".
{ChangeTaskService.DescribeProject(project)}
Work strictly inside the provided isolated copy of the repository. You may inspect files, search code, and reason about the project, but do not intentionally modify files.

After the intent line below, always output one subject line in this exact form, used as the title of this task in the history list:
AITeamSubject: <8-20 個字的繁體中文短主旨，描述這次要做／要查的是什麼，不要加句號>

Classify the user's request first:
- If the user asks only to inspect, explain, diagnose, compare, answer a question, or review existing code without changing it, output exactly this first line:
AITeamIntent: INQUIRY
Then answer the request in Traditional Chinese. Be concrete and cite file paths / symbols when useful.
- If the user asks to add, edit, delete, fix, refactor, build, release, commit, or otherwise change the project, output exactly this first line:
AITeamIntent: CHANGE
Then provide only a short Traditional Chinese summary of the requested change. Do not implement it in this inquiry run.

User request:
{request}
""";

    internal static InquiryResult Parse(ProviderId provider, string raw)
    {
        var text = raw.Trim();
        var intent = text.Contains("AITeamIntent: CHANGE", StringComparison.OrdinalIgnoreCase)
            ? RequestIntent.Change
            : RequestIntent.Inquiry;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

        // 開頭的兩行標記（意圖、主旨）只是給程式讀的，不要留在顯示給人看的答案裡。
        var subject = "";
        for (var i = 0; i < lines.Count && i < 4; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("AITeamSubject:", StringComparison.OrdinalIgnoreCase))
            {
                subject = line["AITeamSubject:".Length..].Trim();
                lines.RemoveAt(i);
                break;
            }
        }
        if (lines.Count > 0 && lines[0].Trim().StartsWith("AITeamIntent:", StringComparison.OrdinalIgnoreCase))
            lines.RemoveAt(0);

        var answer = string.Join(Environment.NewLine, lines).Trim();
        if (answer.Length == 0) answer = intent == RequestIntent.Change ? "已判斷為修改任務。" : text;
        return new InquiryResult(intent, provider, answer, null, subject);
    }


    private static string FirstUsefulLine(params string[] texts)
    {
        foreach (var text in texts)
        {
            var line = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
        }
        return "未知錯誤";
    }

    /// <summary>把多行訊息壓成一行並截長度，避免一則 log 洗掉整個畫面。</summary>
    internal static string OneLine(string text, int max) => TextSummary.OneLine(text, max);
}
