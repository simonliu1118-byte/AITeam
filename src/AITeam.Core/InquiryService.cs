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
    ChangeTaskResult? Change = null);

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
    }

    public async Task<InquiryResult> RunAsync(
        ProjectEntry project,
        string request,
        IReadOnlyList<ProviderId> candidates,
        Func<PlanGatePrompt, CancellationToken, Task<PlanGateResponse>> askUser,
        Action<string> progress,
        Action<TaskProgress> onStage,
        CancellationToken cancellationToken)
    {
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
                    var answer = await RunProviderAsync(provider, workingDirectory, prompt, cancellationToken);
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
                                cancellationToken);

                            var summary = $"{change.Summary}\r\nRisk：{change.Risk}\r\nImplementer：{change.Implementer.ToFriendlyName()}\r\nFinal Review：{change.FinalReviewer.ToFriendlyName()}";
                            return new InquiryResult(RequestIntent.Change, change.FinalReviewer, summary, change);
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
                    progress($"{provider.ToFriendlyName()} 本次失敗，嘗試下一個 AI。");
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

    private async Task<string> RunProviderAsync(
        ProviderId provider,
        string workingDirectory,
        string prompt,
        CancellationToken cancellationToken)
    {
        return provider switch
        {
            ProviderId.Codex => await RunCodexAsync(workingDirectory, prompt, cancellationToken),
            ProviderId.Claude => await RunClaudeAsync(workingDirectory, prompt, cancellationToken),
            ProviderId.Antigravity => await RunAntigravityAsync(workingDirectory, prompt, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private async Task<string> RunCodexAsync(string workingDirectory, string prompt, CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(_runtimeRoot, "temp");
        Directory.CreateDirectory(tempDir);
        var lastMessage = Path.Combine(tempDir, "codex-inquiry-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var result = await _runner.RunAsync(
                _agents.CodexCommand,
                new[]
                {
                    "--sandbox", "read-only",
                    "--ask-for-approval", "never",
                    "-c", "model_reasoning_effort=\"medium\"",
                    "exec",
                    "--skip-git-repo-check",
                    "--output-last-message", lastMessage,
                    "-"
                },
                workingDirectory,
                prompt,
                TimeSpan.FromMinutes(5),
                cancellationToken);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(FirstUsefulLine(result.StandardError, result.StandardOutput));
            if (File.Exists(lastMessage)) return await File.ReadAllTextAsync(lastMessage, cancellationToken);
            return result.StandardOutput;
        }
        finally
        {
            try { if (File.Exists(lastMessage)) File.Delete(lastMessage); } catch { }
        }
    }

    private async Task<string> RunClaudeAsync(string workingDirectory, string prompt, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            _agents.ClaudeCommand,
            new[]
            {
                "-p", prompt,
                "--output-format", "text",
                "--max-turns", "20",
                "--model", "sonnet",
                "--permission-mode", "plan",
                "--no-session-persistence"
            },
            workingDirectory,
            null,
            TimeSpan.FromMinutes(5),
            cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(FirstUsefulLine(result.StandardError, result.StandardOutput));
        return result.StandardOutput;
    }

    private async Task<string> RunAntigravityAsync(string workingDirectory, string prompt, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            @event = "user",
            message = new { content = prompt }
        }) + Environment.NewLine;

        var result = await _runner.RunAsync(
            _agents.AntigravityCommand,
            new[]
            {
                "--dangerously-skip-permissions",
                "--input-format", "stream-json",
                "--output-format", "stream-json",
                "--print-timeout", "5m"
            },
            workingDirectory,
            payload,
            TimeSpan.FromMinutes(6),
            cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(FirstUsefulLine(result.StandardError, result.StandardOutput));

        var extracted = ExtractAntigravityAnswer(result.StandardOutput);
        return string.IsNullOrWhiteSpace(extracted) ? result.StandardOutput : extracted;
    }

    private static string BuildPrompt(ProjectEntry project, string request) => $"""
You are the AITeam read-only request gate for project "{project.Name}".
Work strictly inside the provided isolated copy of the repository. You may inspect files, search code, and reason about the project, but do not intentionally modify files.

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
        if (lines.Count > 0 && lines[0].StartsWith("AITeamIntent:", StringComparison.OrdinalIgnoreCase))
            lines.RemoveAt(0);
        var answer = string.Join(Environment.NewLine, lines).Trim();
        if (answer.Length == 0) answer = intent == RequestIntent.Change ? "已判斷為修改任務。" : text;
        return new InquiryResult(intent, provider, answer);
    }

    private static string ExtractAntigravityAnswer(string stream)
    {
        var candidates = new List<string>();
        foreach (var line in stream.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                CollectStrings(doc.RootElement, candidates);
            }
            catch { }
        }
        return candidates
            .Where(x => x.Contains("AITeamIntent:", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Length)
            .FirstOrDefault()
            ?? candidates.OrderByDescending(x => x.Length).FirstOrDefault()
            ?? string.Empty;
    }

    private static void CollectStrings(JsonElement element, List<string> output)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    (property.NameEquals("content") || property.NameEquals("text") || property.NameEquals("result")))
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) output.Add(value);
                }
                else CollectStrings(property.Value, output);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectStrings(item, output);
        }
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
}
