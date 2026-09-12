using System.Text.Json;
using System.Text.RegularExpressions;
using AITeam.Models;

namespace AITeam.Services;

public enum ChangeRisk
{
    Low,
    Normal,
    High
}

public enum VersionBump
{
    None,
    Patch,
    Minor,
    Major
}

public sealed record ChangeTaskResult(
    ProviderId Implementer,
    ProviderId FinalReviewer,
    ChangeRisk Risk,
    string Version,
    string Tag,
    string Summary);

public sealed class ChangeTaskService
{
    private readonly string _runtimeRoot;
    private readonly ProcessRunner _runner;
    private readonly GitRepositoryService _git;

    public ChangeTaskService(string runtimeRoot, ProcessRunner runner)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
        _git = new GitRepositoryService(runtimeRoot, runner);
    }

    public async Task<ChangeTaskResult> RunAsync(
        ProjectEntry project,
        string request,
        IReadOnlyList<ProviderId> availableProviders,
        Action<string> progress,
        CancellationToken cancellationToken)
    {
        var available = availableProviders.Distinct().ToList();
        if (available.Count < 2)
            throw new InvalidOperationException("修改任務至少需要兩個可用 AI，才能保留獨立實作與審查。請先恢復至少兩個 AI 後再送出。");
        if (!Directory.Exists(project.RepoPath))
            throw new DirectoryNotFoundException($"找不到專案 Repo：{project.RepoPath}");

        progress("同步 GitHub 預設分支並確認本機 Repo 安全狀態…");
        var defaultBranch = await _git.SafeSyncAsync(project.RepoPath, project.DefaultBranch, cancellationToken);
        var baseSha = (await RunGitCheckedAsync(project.RepoPath, new[] { "rev-parse", "HEAD" }, cancellationToken)).StandardOutput.Trim();

        var taskId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        var worktreesRoot = Path.Combine(_runtimeRoot, "worktrees");
        Directory.CreateDirectory(worktreesRoot);
        var worktreeRoot = Path.Combine(worktreesRoot, SafeName(project.Name) + "-" + taskId);
        var taskBranch = "aiteam/task-" + taskId;
        var worktreeAdded = false;
        var formalized = false;

        try
        {
            progress("建立隔離工作區…");
            var add = await _runner.RunAsync(
                "git",
                new[] { "worktree", "add", "-b", taskBranch, worktreeRoot, baseSha },
                project.RepoPath,
                null,
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (add.ExitCode != 0)
                throw new InvalidOperationException("建立修改工作區失敗：" + FirstUsefulLine(add.StandardError, add.StandardOutput));
            worktreeAdded = true;

            var workingDirectory = string.IsNullOrWhiteSpace(project.RepoSubpath)
                ? worktreeRoot
                : Path.Combine(worktreeRoot, project.RepoSubpath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(workingDirectory))
                throw new DirectoryNotFoundException($"隔離工作區內找不到專案目錄：{project.RepoSubpath}");

            var scout = Pick(available, ProviderId.Antigravity, ProviderId.Codex, ProviderId.Claude);
            progress($"{FriendlyProvider(scout)}：Scout / evidence…");
            var scoutReport = await RunReadOnlyAsync(
                scout,
                workingDirectory,
                BuildScoutPrompt(project, request),
                cancellationToken);

            var planner = Pick(available, ProviderId.Codex, ProviderId.Antigravity, ProviderId.Claude);
            progress($"{FriendlyProvider(planner)}：Plan Gate / risk…");
            var plan = await RunReadOnlyAsync(
                planner,
                workingDirectory,
                BuildPlanPrompt(project, request, scoutReport),
                cancellationToken);
            var risk = ParseRisk(plan);
            var bump = ParseVersionBump(plan);
            if (bump == VersionBump.None) bump = VersionBump.Patch;
            progress($"Plan Gate：Risk={risk}，Version={bump}");

            var implementer = Pick(available, ProviderId.Claude, ProviderId.Antigravity, ProviderId.Codex);
            progress($"{FriendlyProvider(implementer)}：開始實作與測試…");
            await RunWriteAsync(
                implementer,
                workingDirectory,
                BuildImplementPrompt(project, request, scoutReport, plan),
                cancellationToken);

            await VerifyWorkingTreeAsync(worktreeRoot, progress, cancellationToken);

            ProviderId challengeProvider = PickDifferent(
                available,
                implementer,
                ProviderId.Antigravity,
                ProviderId.Codex,
                ProviderId.Claude);
            ProviderId finalProvider = PickDifferent(
                available,
                implementer,
                ProviderId.Codex,
                ProviderId.Antigravity,
                ProviderId.Claude);

            string finalReview = string.Empty;
            for (var round = 0; round <= 3; round++)
            {
                var diff = await GetDiffAsync(worktreeRoot, cancellationToken);
                progress($"{FriendlyProvider(challengeProvider)}：獨立 Challenge…");
                var challenge = await RunReadOnlyAsync(
                    challengeProvider,
                    workingDirectory,
                    BuildChallengePrompt(request, plan, diff),
                    cancellationToken);

                progress($"{FriendlyProvider(finalProvider)}：Final Review…");
                finalReview = await RunReadOnlyAsync(
                    finalProvider,
                    workingDirectory,
                    BuildFinalReviewPrompt(request, plan, challenge, diff),
                    cancellationToken);

                if (ReviewPassed(finalReview))
                {
                    progress("Final Review：PASS");
                    break;
                }

                if (round == 3)
                    throw new InvalidOperationException("三輪修正後 Final Review 仍未通過。工作區已保留供檢查，不會合併或推送。");

                var repairer = available.Contains(implementer)
                    ? implementer
                    : Pick(available, ProviderId.Claude, ProviderId.Antigravity, ProviderId.Codex);
                progress($"Final Review 要求修正；{FriendlyProvider(repairer)} 進行第 {round + 1} 輪 Repair…");
                await RunWriteAsync(
                    repairer,
                    workingDirectory,
                    BuildRepairPrompt(request, plan, challenge, finalReview),
                    cancellationToken);
                await VerifyWorkingTreeAsync(worktreeRoot, progress, cancellationToken);
            }

            var (newVersion, tag) = await BumpVersionAsync(project, workingDirectory, bump, cancellationToken);
            progress($"版本：{newVersion}；Tag：{tag}");

            await VerifyWorkingTreeAsync(worktreeRoot, progress, cancellationToken);
            await RunGitCheckedAsync(worktreeRoot, new[] { "add", "--all" }, cancellationToken);
            var status = await RunGitCheckedAsync(worktreeRoot, new[] { "status", "--porcelain" }, cancellationToken);
            if (string.IsNullOrWhiteSpace(status.StandardOutput))
                throw new InvalidOperationException("AI 執行後沒有任何可提交的變更，因此不建立正式版本。");

            var commitMessage = "AITeam: " + OneLine(request, 72);
            await RunGitCheckedAsync(worktreeRoot, new[] { "commit", "-m", commitMessage }, cancellationToken);
            var taskCommit = (await RunGitCheckedAsync(worktreeRoot, new[] { "rev-parse", "HEAD" }, cancellationToken)).StandardOutput.Trim();

            progress("正式化前重新確認 GitHub 預設分支沒有被其他工作更新…");
            await RunGitCheckedAsync(project.RepoPath, new[] { "fetch", "--prune", "origin" }, cancellationToken);
            var remoteSha = (await RunGitCheckedAsync(project.RepoPath, new[] { "rev-parse", $"origin/{defaultBranch}" }, cancellationToken)).StandardOutput.Trim();
            if (!remoteSha.Equals(baseSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("執行期間 GitHub 預設分支已出現新 commit。為避免覆蓋他人工作，本次不會推送；工作區已保留。");

            var tagExists = await RunGitAsync(worktreeRoot, new[] { "rev-parse", "-q", "--verify", $"refs/tags/{tag}" }, cancellationToken);
            if (tagExists.ExitCode == 0)
                throw new InvalidOperationException($"Tag {tag} 已存在，為避免覆蓋既有版本，本次停止正式化。");

            await RunGitCheckedAsync(worktreeRoot, new[] { "tag", "-a", tag, "-m", $"{project.Name} {newVersion}" }, cancellationToken);
            progress("Commit / Tag 完成，原子推送 main + tag 到 GitHub…");
            var push = await _runner.RunAsync(
                "git",
                new[]
                {
                    "push", "--atomic", "origin",
                    $"{taskCommit}:refs/heads/{defaultBranch}",
                    $"refs/tags/{tag}:refs/tags/{tag}"
                },
                worktreeRoot,
                null,
                TimeSpan.FromMinutes(3),
                cancellationToken);
            if (push.ExitCode != 0)
                throw new InvalidOperationException("GitHub 原子推送失敗；main/tag 均未應部分成功。工作區已保留：" + FirstUsefulLine(push.StandardError, push.StandardOutput));

            formalized = true;
            progress("GitHub 正式版本已完成。同步本機預設分支…");
            await _git.SafeSyncAsync(project.RepoPath, project.DefaultBranch, cancellationToken);

            return new ChangeTaskResult(
                implementer,
                finalProvider,
                risk,
                newVersion,
                tag,
                $"修改完成並已正式發布：{project.Name} {newVersion}（{tag}）。");
        }
        finally
        {
            if (worktreeAdded && formalized)
            {
                try
                {
                    await _runner.RunAsync(
                        "git",
                        new[] { "worktree", "remove", "--force", worktreeRoot },
                        project.RepoPath,
                        null,
                        TimeSpan.FromMinutes(1),
                        CancellationToken.None);
                }
                catch { }
                try { if (Directory.Exists(worktreeRoot)) Directory.Delete(worktreeRoot, true); } catch { }
            }
            else if (worktreeAdded && !formalized)
            {
                progress($"本次未正式化；工作區保留於：{worktreeRoot}");
            }
        }
    }

    private async Task VerifyWorkingTreeAsync(string worktreeRoot, Action<string> progress, CancellationToken cancellationToken)
    {
        progress("Verification：檢查 Git diff / whitespace / 工作區狀態…");
        var diffCheck = await RunGitAsync(worktreeRoot, new[] { "diff", "HEAD", "--check" }, cancellationToken);
        if (diffCheck.ExitCode != 0)
            throw new InvalidOperationException("Verification 失敗（git diff HEAD --check）：" + FirstUsefulLine(diffCheck.StandardError, diffCheck.StandardOutput));

        var status = await RunGitCheckedAsync(worktreeRoot, new[] { "status", "--porcelain" }, cancellationToken);
        if (string.IsNullOrWhiteSpace(status.StandardOutput))
            throw new InvalidOperationException("實作階段沒有產生任何變更。");
    }

    private async Task<(string Version, string Tag)> BumpVersionAsync(
        ProjectEntry project,
        string workingDirectory,
        VersionBump bump,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project.VersionFile))
            throw new InvalidOperationException("此專案尚未設定 version_file。AITeam 不會在不知道版本規則時自行猜測，因此已停止正式化並保留工作區。");

        var versionPath = Path.GetFullPath(Path.Combine(workingDirectory, project.VersionFile.Replace('/', Path.DirectorySeparatorChar)));
        var projectRoot = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!versionPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(versionPath))
            throw new InvalidOperationException($"找不到或不允許存取版本檔：{project.VersionFile}");

        var current = (await File.ReadAllTextAsync(versionPath, cancellationToken)).Trim();
        var match = Regex.Match(current, "^(?<maj>\\d+)\\.(?<min>\\d+)\\.(?<pat>\\d+)$");
        if (!match.Success)
            throw new InvalidOperationException($"版本檔目前為「{current}」，不是單純 X.Y.Z。AITeam v0.5.0 不會猜測 prerelease/特殊版號規則，因此已停止正式化並保留工作區。");

        var major = int.Parse(match.Groups["maj"].Value);
        var minor = int.Parse(match.Groups["min"].Value);
        var patch = int.Parse(match.Groups["pat"].Value);
        switch (bump)
        {
            case VersionBump.Major:
                major++; minor = 0; patch = 0;
                break;
            case VersionBump.Minor:
                minor++; patch = 0;
                break;
            default:
                patch++;
                break;
        }

        var next = $"{major}.{minor}.{patch}";
        await File.WriteAllTextAsync(versionPath, next + Environment.NewLine, cancellationToken);
        var prefix = string.IsNullOrWhiteSpace(project.TagPrefix) ? "v" : project.TagPrefix.Trim();
        return (next, prefix + next);
    }

    private async Task<string> GetDiffAsync(string worktreeRoot, CancellationToken cancellationToken)
    {
        var result = await RunGitCheckedAsync(worktreeRoot, new[] { "diff", "--no-ext-diff", "--unified=3" }, cancellationToken);
        var text = result.StandardOutput;
        const int max = 120_000;
        return text.Length <= max ? text : text[..max] + "\n[diff truncated by AITeam]";
    }

    private async Task<string> RunReadOnlyAsync(ProviderId provider, string workingDirectory, string prompt, CancellationToken cancellationToken) =>
        await RunProviderAsync(provider, workingDirectory, prompt, false, cancellationToken);

    private async Task RunWriteAsync(ProviderId provider, string workingDirectory, string prompt, CancellationToken cancellationToken)
    {
        var result = await RunProviderAsync(provider, workingDirectory, prompt, true, cancellationToken);
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException($"{FriendlyProvider(provider)} 沒有回傳實作結果。");
    }

    private async Task<string> RunProviderAsync(
        ProviderId provider,
        string workingDirectory,
        string prompt,
        bool allowWrite,
        CancellationToken cancellationToken)
    {
        return provider switch
        {
            ProviderId.Codex => await RunCodexAsync(workingDirectory, prompt, allowWrite, cancellationToken),
            ProviderId.Claude => await RunClaudeAsync(workingDirectory, prompt, allowWrite, cancellationToken),
            ProviderId.Antigravity => await RunAntigravityAsync(workingDirectory, prompt, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private async Task<string> RunCodexAsync(string workingDirectory, string prompt, bool allowWrite, CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(_runtimeRoot, "temp");
        Directory.CreateDirectory(tempDir);
        var lastMessage = Path.Combine(tempDir, "codex-change-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var args = new List<string>
            {
                "--sandbox", allowWrite ? "workspace-write" : "read-only",
                "--ask-for-approval", "never",
                "-c", "model_reasoning_effort=\"high\"",
                "exec", "--skip-git-repo-check", "--output-last-message", lastMessage, "-"
            };
            var result = await _runner.RunAsync(
                "codex", args, workingDirectory, prompt, TimeSpan.FromMinutes(15), cancellationToken);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(FirstUsefulLine(result.StandardError, result.StandardOutput));
            return File.Exists(lastMessage)
                ? await File.ReadAllTextAsync(lastMessage, cancellationToken)
                : result.StandardOutput;
        }
        finally
        {
            try { if (File.Exists(lastMessage)) File.Delete(lastMessage); } catch { }
        }
    }

    private async Task<string> RunClaudeAsync(string workingDirectory, string prompt, bool allowWrite, CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-p", prompt,
            "--output-format", "text",
            "--max-turns", allowWrite ? "80" : "30",
            "--model", "sonnet",
            "--permission-mode", allowWrite ? "bypassPermissions" : "plan",
            "--no-session-persistence"
        };
        var result = await _runner.RunAsync(
            "claude", args, workingDirectory, null, TimeSpan.FromMinutes(15), cancellationToken);
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
            "agy",
            new[]
            {
                "--dangerously-skip-permissions",
                "--input-format", "stream-json",
                "--output-format", "stream-json",
                "--print-timeout", "15m"
            },
            workingDirectory,
            payload,
            TimeSpan.FromMinutes(16),
            cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(FirstUsefulLine(result.StandardError, result.StandardOutput));
        var extracted = ExtractAntigravityAnswer(result.StandardOutput);
        return string.IsNullOrWhiteSpace(extracted) ? result.StandardOutput : extracted;
    }

    private static string BuildScoutPrompt(ProjectEntry project, string request) => $"""
You are AITeam Scout. Read the repository and gather evidence for this requested change. Do not modify files.
Project: {project.Name}
Request: {request}
Return concise Traditional Chinese with relevant file paths, symbols, current behavior, likely tests, and risks. Do not design an elaborate evidence pipeline; inspect the repo directly.
""";

    private static string BuildPlanPrompt(ProjectEntry project, string request, string scout) => $"""
You are AITeam Plan Gate. Validate the Scout evidence against the repository, then finalize the implementation plan. Do not modify files.
Project: {project.Name}
Request: {request}
Scout report:
{scout}

Your first two lines MUST be exactly in this format:
AITeamRisk: LOW|NORMAL|HIGH
AITeamVersionBump: PATCH|MINOR|MAJOR|NONE
Then provide a compact Traditional Chinese plan: files/symbols to change, behavior, acceptance criteria, tests/verification, and important exclusions. Use PATCH for small fixes, MINOR for functional changes, MAJOR only for a major production milestone.
""";

    private static string BuildImplementPrompt(ProjectEntry project, string request, string scout, string plan) => $"""
You are AITeam Implementer. Work only inside this isolated Git worktree and implement the approved request. You MAY edit files here.
Project: {project.Name}
Request: {request}
Scout:
{scout}
Approved plan:
{plan}

Implement the smallest complete change that satisfies the plan. Run the most relevant tests/build checks available in the repository. Do not commit, tag, push, merge, or modify files outside this worktree; AITeam controller owns Git formalization. At the end summarize changed files and tests run.
""";

    private static string BuildChallengePrompt(string request, string plan, string diff) => $"""
You are AITeam independent Challenger. Do not modify files. Review the implementation against the user request and approved plan. Look for regressions, missing edge cases, unsafe behavior, incorrect assumptions, and insufficient tests.
Request: {request}
Plan:
{plan}
Diff:
{diff}

First line MUST be exactly one of:
AITeamReview: PASS
AITeamReview: REPAIR
Then explain concrete findings in Traditional Chinese. Do not request cosmetic changes unless they materially improve correctness or the requested behavior.
""";

    private static string BuildFinalReviewPrompt(string request, string plan, string challenge, string diff) => $"""
You are AITeam Final Reviewer / adjudicator. Do not modify files. Decide whether this change is safe and complete enough to formalize.
Request: {request}
Plan:
{plan}
Independent challenge:
{challenge}
Diff:
{diff}

First line MUST be exactly one of:
AITeamReview: PASS
AITeamReview: REPAIR
Then give the final rationale in Traditional Chinese. PASS only when the request is satisfied and no blocking correctness/safety issue remains.
""";

    private static string BuildRepairPrompt(string request, string plan, string challenge, string finalReview) => $"""
You are AITeam Repair implementer. Work only inside this isolated Git worktree. Fix the blocking issues identified by review without expanding scope unnecessarily.
Request: {request}
Approved plan:
{plan}
Challenge:
{challenge}
Final review:
{finalReview}

Make the required edits and run relevant tests. Do not commit, tag, push, merge, or modify anything outside this worktree. Summarize repairs and verification performed.
""";

    private static ChangeRisk ParseRisk(string plan)
    {
        if (plan.Contains("AITeamRisk: HIGH", StringComparison.OrdinalIgnoreCase)) return ChangeRisk.High;
        if (plan.Contains("AITeamRisk: LOW", StringComparison.OrdinalIgnoreCase)) return ChangeRisk.Low;
        return ChangeRisk.Normal;
    }

    private static VersionBump ParseVersionBump(string plan)
    {
        if (plan.Contains("AITeamVersionBump: MAJOR", StringComparison.OrdinalIgnoreCase)) return VersionBump.Major;
        if (plan.Contains("AITeamVersionBump: MINOR", StringComparison.OrdinalIgnoreCase)) return VersionBump.Minor;
        if (plan.Contains("AITeamVersionBump: PATCH", StringComparison.OrdinalIgnoreCase)) return VersionBump.Patch;
        return VersionBump.None;
    }

    private static bool ReviewPassed(string review) =>
        review.Contains("AITeamReview: PASS", StringComparison.OrdinalIgnoreCase) &&
        !review.Contains("AITeamReview: REPAIR", StringComparison.OrdinalIgnoreCase);

    private static ProviderId Pick(IReadOnlyCollection<ProviderId> available, params ProviderId[] preferences)
    {
        foreach (var provider in preferences)
            if (available.Contains(provider)) return provider;
        throw new InvalidOperationException("找不到可用 AI。");
    }

    private static ProviderId PickDifferent(IReadOnlyCollection<ProviderId> available, ProviderId excluded, params ProviderId[] preferences)
    {
        foreach (var provider in preferences)
            if (provider != excluded && available.Contains(provider)) return provider;
        throw new InvalidOperationException("缺少可與實作者獨立的審查 AI，因此不會正式化修改。");
    }

    private async Task<ProcessRunResult> RunGitAsync(string workingDirectory, IEnumerable<string> args, CancellationToken cancellationToken) =>
        await _runner.RunAsync("git", args, workingDirectory, null, TimeSpan.FromMinutes(2), cancellationToken);

    private async Task<ProcessRunResult> RunGitCheckedAsync(string workingDirectory, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(workingDirectory, args, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Git 操作失敗：" + FirstUsefulLine(result.StandardError, result.StandardOutput));
        return result;
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
            .Where(x => x.Contains("AITeam", StringComparison.OrdinalIgnoreCase))
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

    private static string SafeName(string text)
    {
        var chars = text.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch).ToArray();
        var value = new string(chars).Trim();
        return value.Length == 0 ? "project" : value;
    }

    private static string OneLine(string text, int max)
    {
        var value = Regex.Replace(text, "\\s+", " ").Trim();
        return value.Length <= max ? value : value[..max].TrimEnd();
    }

    private static string FriendlyProvider(ProviderId provider) => provider switch
    {
        ProviderId.Codex => "GPT / Codex",
        ProviderId.Claude => "Claude",
        ProviderId.Antigravity => "Gemini / Antigravity",
        _ => provider.ToString()
    };
}
