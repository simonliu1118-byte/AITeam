using System.Text;
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

public enum PlanGateStage
{
    NeedsInput,
    ReadyForConfirmation
}

public enum PlanGateAction
{
    Reply,
    Finalize,
    HandOff
}

public sealed record PlanGatePrompt(PlanGateStage Stage, string Body);

public sealed record PlanGateResponse(PlanGateAction Action, string? Text = null);

public sealed record ChangeTaskResult(
    ProviderId Implementer,
    ProviderId FinalReviewer,
    ChangeRisk Risk,
    string Version,
    string Tag,
    string Summary,
    bool DegradedReview = false);

public sealed class ChangeTaskService
{
    private readonly string _runtimeRoot;
    private readonly IProcessRunner _runner;
    private readonly GitRepositoryService _git;
    private readonly WorkflowSettings _workflow;
    private readonly AgentsConfig _agents;

    public ChangeTaskService(string runtimeRoot, IProcessRunner runner)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
        _git = new GitRepositoryService(runtimeRoot, runner);
        var config = new RuntimeConfigService(runtimeRoot);
        _workflow = config.LoadWorkflowSettings();
        _agents = config.LoadAgentsConfig();
    }

    public async Task<ChangeTaskResult> RunAsync(
        ProjectEntry project,
        string request,
        IReadOnlyList<ProviderId> availableProviders,
        Func<PlanGatePrompt, CancellationToken, Task<PlanGateResponse>> askUser,
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
            progress($"{scout.ToFriendlyName()}：Scout / evidence…");
            var scoutReport = await RunReadOnlyAsync(
                scout,
                workingDirectory,
                BuildScoutPrompt(project, request),
                cancellationToken);

            var planner = Pick(available, ProviderId.Codex, ProviderId.Antigravity, ProviderId.Claude);
            var (plan, risk, bump) = await RunPlanGateDiscussionAsync(
                project, request, scoutReport, planner, workingDirectory, askUser, progress, cancellationToken);

            progress("計畫已定案；重新同步 GitHub 預設分支並重新鎖定基準版本…");
            var refreshedBranch = await _git.SafeSyncAsync(project.RepoPath, project.DefaultBranch, cancellationToken);
            var refreshedBaseSha = (await RunGitCheckedAsync(project.RepoPath, new[] { "rev-parse", "HEAD" }, cancellationToken)).StandardOutput.Trim();
            if (!refreshedBaseSha.Equals(baseSha, StringComparison.OrdinalIgnoreCase))
            {
                progress("偵測到討論期間 GitHub 預設分支已有新 commit，重新建立工作區以基於最新版本繼續…");
                await _runner.RunAsync(
                    "git",
                    new[] { "worktree", "remove", "--force", worktreeRoot },
                    project.RepoPath,
                    null,
                    TimeSpan.FromMinutes(1),
                    cancellationToken);
                baseSha = refreshedBaseSha;
                defaultBranch = refreshedBranch;
                var recreate = await _runner.RunAsync(
                    "git",
                    new[] { "worktree", "add", "-B", taskBranch, worktreeRoot, baseSha },
                    project.RepoPath,
                    null,
                    TimeSpan.FromMinutes(2),
                    cancellationToken);
                if (recreate.ExitCode != 0)
                    throw new InvalidOperationException("重新建立修改工作區失敗：" + FirstUsefulLine(recreate.StandardError, recreate.StandardOutput));
            }

            var implementer = Pick(available, ProviderId.Claude, ProviderId.Antigravity, ProviderId.Codex);
            progress($"{implementer.ToFriendlyName()}：開始實作與測試…");
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

            var degradedReview = !TryPickDifferentFromAny(
                available,
                new[] { implementer, challengeProvider },
                new[] { ProviderId.Codex, ProviderId.Antigravity, ProviderId.Claude },
                out var finalProvider);
            if (degradedReview)
            {
                finalProvider = challengeProvider;
                progress($"⚠️ 僅 {available.Count} 個 AI 上線，Challenge 與 Final Review 將由同一個 AI（{challengeProvider.ToFriendlyName()}）執行，獨立性下降。");
            }

            if (degradedReview && risk == ChangeRisk.Low)
            {
                risk = ChangeRisk.Normal;
                progress("⚠️ 獨立審查被削弱本身就是風險因子，有效風險等級提升一級：LOW → NORMAL。");
            }

            string finalReview = string.Empty;
            var lastFinalReviewer = finalProvider;
            var lastChallenger = challengeProvider;
            for (var round = 0; round <= _workflow.MaxRepairRounds; round++)
            {
                // 3 個 AI 都在線時，從第 2 輪起讓 Challenger / Final Reviewer 互換身分，
                // 讓「第二意見」來自不同視角，而不是同一個審查者重複審自己說過的話。
                var roundChallenger = !degradedReview && round % 2 == 1 ? finalProvider : challengeProvider;
                var roundFinal = !degradedReview && round % 2 == 1 ? challengeProvider : finalProvider;
                lastFinalReviewer = roundFinal;
                lastChallenger = roundChallenger;
                if (degradedReview)
                    progress($"⚠️ 僅 2 個 AI 上線，本輪 Challenge 與 Final Review 為同一 AI，獨立性下降。");

                var diff = await GetDiffAsync(worktreeRoot, cancellationToken);
                progress($"{roundChallenger.ToFriendlyName()}：獨立 Challenge…");
                var challenge = await RunReadOnlyAsync(
                    roundChallenger,
                    workingDirectory,
                    BuildChallengePrompt(request, plan, diff),
                    cancellationToken);

                progress($"{roundFinal.ToFriendlyName()}：Final Review…");
                finalReview = await RunReadOnlyAsync(
                    roundFinal,
                    workingDirectory,
                    BuildFinalReviewPrompt(request, plan, challenge, diff, bump, selfReview: roundChallenger == roundFinal),
                    cancellationToken);

                if (ReviewPassed(finalReview))
                {
                    progress("Final Review：PASS");
                    break;
                }

                if (round == _workflow.MaxRepairRounds)
                    throw new InvalidOperationException($"{_workflow.MaxRepairRounds} 輪修正後 Final Review 仍未通過。工作區已保留供檢查，不會合併或推送。");

                var repairer = available.Contains(implementer)
                    ? implementer
                    : Pick(available, ProviderId.Claude, ProviderId.Antigravity, ProviderId.Codex);
                progress($"Final Review 要求修正；{repairer.ToFriendlyName()} 進行第 {round + 1} 輪 Repair…");
                var repairResult = await RunWriteAsync(
                    repairer,
                    workingDirectory,
                    BuildRepairPrompt(request, plan, challenge, finalReview),
                    cancellationToken);

                var disputeReason = ParseRepairDispute(repairResult);
                if (disputeReason is not null)
                {
                    progress($"{repairer.ToFriendlyName()} 認為審查意見可能誤判，提出反駁：{OneLine(disputeReason, 200)}");
                    progress($"{roundFinal.ToFriendlyName()}：重新裁決 Repairer 的反駁…");
                    finalReview = await RunReadOnlyAsync(
                        roundFinal,
                        workingDirectory,
                        BuildDisputeReviewPrompt(request, plan, challenge, finalReview, disputeReason, diff, bump),
                        cancellationToken);

                    if (ReviewPassed(finalReview))
                    {
                        progress("Final Review：反駁成立，PASS");
                        break;
                    }

                    progress("Final Review：反駁不成立，仍要求修正。");
                    continue;
                }

                await VerifyWorkingTreeAsync(worktreeRoot, progress, cancellationToken);
            }

            var confirmedBump = ParseVersionBumpConfirm(finalReview);
            if (confirmedBump != VersionBump.None && confirmedBump != bump)
            {
                progress($"Final Reviewer 依實際變更重新確認版號等級：{bump} → {confirmedBump}");
                bump = confirmedBump;
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

            progress($"推送任務分支 {taskBranch} 並開啟 PR…");
            var pushTask = await _runner.RunAsync(
                "git",
                new[] { "push", "-u", "origin", $"{taskBranch}:refs/heads/{taskBranch}" },
                worktreeRoot,
                null,
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (pushTask.ExitCode != 0)
                throw new InvalidOperationException("推送任務分支失敗：" + FirstUsefulLine(pushTask.StandardError, pushTask.StandardOutput));

            var prBody = BuildPrBody(request, plan, risk, degradedReview, implementer, lastChallenger, lastFinalReviewer, newVersion, tag);
            var prNumber = await CreatePullRequestAsync(project, taskBranch, defaultBranch, commitMessage, prBody, cancellationToken);
            var prUrl = $"https://github.com/{project.GitHubRepo}/pull/{prNumber}";
            progress($"已開啟 PR #{prNumber}：{prUrl}");

            for (var ciRound = 0; ; ciRound++)
            {
                progress($"等待 {project.GitHubRepo} 的 CI 檢查結果…");
                var (ciPassed, ciSummary) = await WaitForPrChecksAsync(project, prNumber, cancellationToken);
                if (ciPassed)
                {
                    progress("CI 檢查通過。");
                    break;
                }

                if (ciRound == _workflow.MaxCiRepairRounds)
                    throw new InvalidOperationException(
                        $"CI 連續 {_workflow.MaxCiRepairRounds} 輪修正後仍未通過。PR 已保留供人工檢查，不會自動合併：{prUrl}");

                progress($"CI 檢查失敗；{implementer.ToFriendlyName()} 依失敗訊息進行第 {ciRound + 1} 輪修正…");
                await RunWriteAsync(
                    implementer,
                    workingDirectory,
                    BuildCiRepairPrompt(request, plan, ciSummary),
                    cancellationToken);
                await VerifyWorkingTreeAsync(worktreeRoot, progress, cancellationToken);
                await RunGitCheckedAsync(worktreeRoot, new[] { "add", "--all" }, cancellationToken);
                var ciFixStatus = await RunGitCheckedAsync(worktreeRoot, new[] { "status", "--porcelain" }, cancellationToken);
                if (string.IsNullOrWhiteSpace(ciFixStatus.StandardOutput))
                    throw new InvalidOperationException($"CI 修正沒有產生任何變更，無法繼續。PR 已保留：{prUrl}");

                await RunGitCheckedAsync(worktreeRoot, new[] { "commit", "-m", $"AITeam: fix CI (round {ciRound + 1})" }, cancellationToken);
                var pushFix = await _runner.RunAsync(
                    "git",
                    new[] { "push", "origin", taskBranch },
                    worktreeRoot,
                    null,
                    TimeSpan.FromMinutes(2),
                    cancellationToken);
                if (pushFix.ExitCode != 0)
                    throw new InvalidOperationException("推送 CI 修正失敗：" + FirstUsefulLine(pushFix.StandardError, pushFix.StandardOutput));
            }

            if (risk == ChangeRisk.High)
            {
                progress($"⚠️ 高風險變更，需要人工確認合併：{prUrl}");
                await WaitForManualMergeAsync(project, prNumber, cancellationToken);
                progress("偵測到 PR 已由人工合併。");
            }
            else
            {
                progress("風險等級允許自動合併，執行合併…");
                await MergePullRequestAsync(project, prNumber, project.MergeStrategy, cancellationToken);
                progress("PR 已自動合併。");
            }

            progress("合併完成；重新同步並打 tag…");
            await RunGitCheckedAsync(project.RepoPath, new[] { "fetch", "--prune", "origin" }, cancellationToken);
            var mergeSha = (await RunGitCheckedAsync(project.RepoPath, new[] { "rev-parse", $"origin/{defaultBranch}" }, cancellationToken)).StandardOutput.Trim();

            var tagExists = await RunGitAsync(project.RepoPath, new[] { "rev-parse", "-q", "--verify", $"refs/tags/{tag}" }, cancellationToken);
            if (tagExists.ExitCode == 0)
                throw new InvalidOperationException($"Tag {tag} 已存在，為避免覆蓋既有版本，本次停止打 tag（PR 已合併，僅 tag 未完成）。");

            await RunGitCheckedAsync(project.RepoPath, new[] { "tag", "-a", tag, mergeSha, "-m", $"{project.Name} {newVersion}" }, cancellationToken);
            var pushTag = await _runner.RunAsync(
                "git",
                new[] { "push", "origin", $"refs/tags/{tag}:refs/tags/{tag}" },
                project.RepoPath,
                null,
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (pushTag.ExitCode != 0)
                throw new InvalidOperationException("Tag 推送失敗（PR 已合併，僅 tag 未完成）：" + FirstUsefulLine(pushTag.StandardError, pushTag.StandardOutput));

            formalized = true;
            progress("GitHub 正式版本已完成。同步本機預設分支…");
            await _git.SafeSyncAsync(project.RepoPath, project.DefaultBranch, cancellationToken);

            var summary = $"修改完成並已正式發布：{project.Name} {newVersion}（{tag}），PR：{prUrl}";
            if (degradedReview)
                summary += "\r\n⚠️ 本次任務僅 2 個 AI 上線，Challenge 與 Final Review 為同一 AI，獨立性下降（有效風險等級已提升）。";

            return new ChangeTaskResult(
                implementer,
                lastFinalReviewer,
                risk,
                newVersion,
                tag,
                summary,
                degradedReview);
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
                try
                {
                    await _runner.RunAsync(
                        "git",
                        new[] { "branch", "-D", taskBranch },
                        project.RepoPath,
                        null,
                        TimeSpan.FromSeconds(30),
                        CancellationToken.None);
                }
                catch { }
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

    private async Task<(string Plan, ChangeRisk Risk, VersionBump Bump)> RunPlanGateDiscussionAsync(
        ProjectEntry project,
        string request,
        string scoutReport,
        ProviderId planner,
        string workingDirectory,
        Func<PlanGatePrompt, CancellationToken, Task<PlanGateResponse>> askUser,
        Action<string> progress,
        CancellationToken cancellationToken)
    {
        var discussion = new StringBuilder();
        var mustFinalize = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress($"{planner.ToFriendlyName()}：Plan Gate / risk…");
            var reply = await RunReadOnlyAsync(
                planner,
                workingDirectory,
                BuildPlanPrompt(project, request, scoutReport, discussion.ToString(), mustFinalize),
                cancellationToken);

            if (!mustFinalize && IsPlanGateNeedsInput(reply))
            {
                var question = ExtractPlanGateBody(reply);
                progress("Plan Gate 提出問題，等待使用者回覆…");
                var response = await askUser(new PlanGatePrompt(PlanGateStage.NeedsInput, question), cancellationToken);

                if (response.Action == PlanGateAction.HandOff)
                {
                    progress("使用者選擇交給 AI 全權判斷，Plan Gate 下一次回覆必須定案。");
                    discussion.AppendLine("使用者：（選擇交給 AI 全權判斷，請直接定案，不要再提問）");
                    mustFinalize = true;
                }
                else
                {
                    discussion.AppendLine("Plan Gate 提問：");
                    discussion.AppendLine(question);
                    discussion.AppendLine("使用者回覆：");
                    discussion.AppendLine(response.Text ?? string.Empty);
                }
                continue;
            }

            var plan = ExtractPlanGateBody(reply);
            var risk = ParseRisk(reply);
            var bump = ParseVersionBump(reply);
            if (bump == VersionBump.None) bump = VersionBump.Patch;

            if (mustFinalize)
            {
                progress($"Plan Gate：Risk={risk}，Version={bump}（使用者已交給 AI 全權定案）");
                return (plan, risk, bump);
            }

            progress("Plan Gate 認為計畫已可定案，等待使用者確認…");
            var confirmation = await askUser(new PlanGatePrompt(PlanGateStage.ReadyForConfirmation, plan), cancellationToken);

            if (confirmation.Action == PlanGateAction.Finalize)
            {
                progress($"Plan Gate：Risk={risk}，Version={bump}（使用者已確認定案）");
                return (plan, risk, bump);
            }

            if (confirmation.Action == PlanGateAction.HandOff)
            {
                progress($"Plan Gate：Risk={risk}，Version={bump}（使用者交給 AI 全權判斷，採用目前計畫）");
                return (plan, risk, bump);
            }

            discussion.AppendLine("Plan Gate 提出的定案計畫：");
            discussion.AppendLine(plan);
            discussion.AppendLine("使用者補充：");
            discussion.AppendLine(confirmation.Text ?? string.Empty);
        }
    }

    private async Task<int> CreatePullRequestAsync(
        ProjectEntry project,
        string taskBranch,
        string defaultBranch,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(_runtimeRoot, "temp");
        Directory.CreateDirectory(tempDir);
        var bodyFile = Path.Combine(tempDir, "pr-body-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            await File.WriteAllTextAsync(bodyFile, body, cancellationToken);
            var result = await _runner.RunAsync(
                "gh",
                new[]
                {
                    "pr", "create",
                    "--repo", project.GitHubRepo,
                    "--base", defaultBranch,
                    "--head", taskBranch,
                    "--title", title,
                    "--body-file", bodyFile
                },
                project.RepoPath,
                null,
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("開啟 PR 失敗：" + FirstUsefulLine(result.StandardError, result.StandardOutput));
            return ParsePrNumberFromUrl(result.StandardOutput);
        }
        finally
        {
            try { if (File.Exists(bodyFile)) File.Delete(bodyFile); } catch { }
        }
    }

    private async Task<(bool Passed, string Summary)> WaitForPrChecksAsync(
        ProjectEntry project,
        int prNumber,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "gh",
            new[] { "pr", "checks", prNumber.ToString(), "--repo", project.GitHubRepo, "--watch", "--fail-fast" },
            project.RepoPath,
            null,
            TimeSpan.FromMinutes(20),
            cancellationToken);
        var summary = string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
        return (result.ExitCode == 0, summary);
    }

    private async Task MergePullRequestAsync(
        ProjectEntry project,
        int prNumber,
        string? mergeStrategy,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "gh",
            new[] { "pr", "merge", prNumber.ToString(), "--repo", project.GitHubRepo, ResolveMergeFlag(mergeStrategy), "--delete-branch" },
            project.RepoPath,
            null,
            TimeSpan.FromMinutes(2),
            cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("合併 PR 失敗：" + FirstUsefulLine(result.StandardError, result.StandardOutput));
    }

    private async Task WaitForManualMergeAsync(ProjectEntry project, int prNumber, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var view = await _runner.RunAsync(
                "gh",
                new[] { "pr", "view", prNumber.ToString(), "--repo", project.GitHubRepo, "--json", "state" },
                project.RepoPath,
                null,
                TimeSpan.FromSeconds(30),
                cancellationToken);
            if (view.ExitCode == 0)
            {
                var state = ParsePrState(view.StandardOutput);
                if (string.Equals(state, "MERGED", StringComparison.OrdinalIgnoreCase)) return;
                if (string.Equals(state, "CLOSED", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"PR #{prNumber} 已被關閉但未合併，任務中止。");
            }
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        }
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

    private async Task<string> RunWriteAsync(ProviderId provider, string workingDirectory, string prompt, CancellationToken cancellationToken)
    {
        var result = await RunProviderAsync(provider, workingDirectory, prompt, true, cancellationToken);
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException($"{provider.ToFriendlyName()} 沒有回傳實作結果。");
        return result;
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
                _agents.CodexCommand, args, workingDirectory, prompt, TimeSpan.FromMinutes(15), cancellationToken);
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
            _agents.ClaudeCommand, args, workingDirectory, null, TimeSpan.FromMinutes(15), cancellationToken);
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

    private static string BuildPlanPrompt(ProjectEntry project, string request, string scout, string discussion, bool mustFinalize)
    {
        var discussionSection = discussion.Length == 0 ? "（尚無先前討論）" : discussion;
        var decisionInstruction = mustFinalize
            ? "The user has chosen to hand off the final decision to you. This reply MUST output AITeamPlanStatus: READY with a complete plan — do not ask any more questions."
            : "If anything about the request is unclear or could materially change the implementation approach, output AITeamPlanStatus: NEEDS_INPUT and ask concrete question(s) instead of guessing — do not provide the full plan yet. Only output AITeamPlanStatus: READY once you are confident the plan is well-defined. There is no limit on how many rounds of questions you may ask.";

        return $"""
You are AITeam Plan Gate. Validate the Scout evidence against the repository, discuss with the user when needed, then finalize the implementation plan. Do not modify files.
Project: {project.Name}
Request: {request}
Scout report:
{scout}

Discussion with the user so far:
{discussionSection}

{decisionInstruction}

Your first line MUST be exactly one of:
AITeamPlanStatus: NEEDS_INPUT
AITeamPlanStatus: READY

If NEEDS_INPUT: after that first line, write your question(s) to the user in Traditional Chinese. Do not include AITeamRisk/AITeamVersionBump lines in this case.

If READY: your next two lines MUST be exactly in this format:
AITeamRisk: LOW|NORMAL|HIGH
AITeamVersionBump: PATCH|MINOR|MAJOR|NONE
Then provide a compact Traditional Chinese plan: files/symbols to change, behavior, acceptance criteria, tests/verification, and important exclusions. Use PATCH for small fixes, MINOR for functional changes, MAJOR only for a major production milestone.
""";
    }

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

    private static string BuildFinalReviewPrompt(string request, string plan, string challenge, string diff, VersionBump plannedBump, bool selfReview) => $"""
You are AITeam Final Reviewer / adjudicator. Do not modify files. Decide whether this change is safe and complete enough to formalize.
Request: {request}
Plan:
{plan}
Independent challenge:
{challenge}
Diff:
{diff}
{(selfReview ? "\nNote: you already wrote the independent challenge above in this same round (only two AI are currently online). Now switch fully into the independent-reviewer role: be skeptical of your own earlier challenge and look for anything it missed or was too lenient about.\n" : "")}
First line MUST be exactly one of:
AITeamReview: PASS
AITeamReview: REPAIR
Second line MUST be exactly one of:
AITeamVersionBumpConfirm: PATCH
AITeamVersionBumpConfirm: MINOR
AITeamVersionBumpConfirm: MAJOR
The Plan Gate originally classified the version bump as {plannedBump}; re-confirm it against the actual diff above instead of repeating the planned value blindly — implementation or repair rounds may have changed the scope.
Then give the final rationale in Traditional Chinese. PASS only when the request is satisfied and no blocking correctness/safety issue remains. If you PASS, AITeam will separately apply a purely mechanical version-file edit afterwards based on your AITeamVersionBumpConfirm value; you do not need to review that follow-up edit.
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

Note: the plan and evidence above were produced at the start of this task. The code may have changed since then, from your own earlier implementation or repair rounds. Re-check the actual current file contents before editing — do not blindly trust descriptions written before those changes.

If, after re-checking the actual code, you believe the review's requested change is a false positive (the concern does not really apply, or is already handled), you may dispute it instead of making a speculative edit: make no file changes and start your entire response with exactly this first line:
AITeamRepairStance: DISPUTE
Then explain your reasoning in Traditional Chinese for why no change is needed. AITeam will send your reasoning back to the reviewer for a final decision; if they disagree, you will be asked to make the edit next round.

Otherwise, make the required edits and run relevant tests. Do not commit, tag, push, merge, or modify anything outside this worktree. Summarize repairs and verification performed.
""";

    private static string BuildDisputeReviewPrompt(
        string request, string plan, string challenge, string finalReview, string disputeReason, string diff, VersionBump plannedBump) => $"""
You are AITeam Final Reviewer / adjudicator, re-adjudicating your own earlier verdict. Do not modify files.
Request: {request}
Plan:
{plan}
Independent challenge:
{challenge}
Your earlier verdict (requested REPAIR):
{finalReview}
The Repair implementer disputes this verdict instead of making changes. Their reasoning:
{disputeReason}
Diff (unchanged since your earlier verdict, no edits were made):
{diff}

Decide again with an open mind: if the implementer's reasoning is correct and there is no real blocking issue, change your verdict to PASS. If the concern still stands, keep REPAIR.
First line MUST be exactly one of:
AITeamReview: PASS
AITeamReview: REPAIR
Second line MUST be exactly one of:
AITeamVersionBumpConfirm: PATCH
AITeamVersionBumpConfirm: MINOR
AITeamVersionBumpConfirm: MAJOR
The Plan Gate originally classified the version bump as {plannedBump}.
Then give your rationale in Traditional Chinese.
""";

    private static string BuildPrBody(
        string request, string plan, ChangeRisk risk, bool degradedReview,
        ProviderId implementer, ProviderId challenger, ProviderId finalReviewer,
        string newVersion, string tag)
    {
        var degradedNote = degradedReview
            ? "\n\n⚠️ 本次任務僅 2 個 AI 上線，Challenge 與 Final Review 為同一 AI，獨立性下降（有效風險等級已提升一級）。"
            : "";
        return $"""
## AITeam 自動化變更

**需求**：{request}

**版本**：{newVersion}（{tag}）
**風險等級**：{risk}{degradedNote}

**執行角色**
- Implementer：{implementer.ToFriendlyName()}
- Challenger：{challenger.ToFriendlyName()}
- Final Reviewer：{finalReviewer.ToFriendlyName()}

**計畫摘要**

{plan}
""";
    }

    private static string BuildCiRepairPrompt(string request, string plan, string ciSummary) => $"""
You are AITeam CI-repair implementer. Work only inside this isolated Git worktree. The pull request's CI checks failed; fix the cause without expanding scope unnecessarily.
Request: {request}
Approved plan:
{plan}
CI check summary:
{ciSummary}

Re-check the actual current file contents before editing. Make the required edits and run relevant tests/build locally if possible. Do not commit, tag, push, merge, or modify anything outside this worktree; AITeam controller owns Git operations. Summarize the fix and how it addresses the CI failure.
""";

    internal static ChangeRisk ParseRisk(string plan)
    {
        if (plan.Contains("AITeamRisk: HIGH", StringComparison.OrdinalIgnoreCase)) return ChangeRisk.High;
        if (plan.Contains("AITeamRisk: LOW", StringComparison.OrdinalIgnoreCase)) return ChangeRisk.Low;
        return ChangeRisk.Normal;
    }

    internal static VersionBump ParseVersionBump(string plan)
    {
        if (plan.Contains("AITeamVersionBump: MAJOR", StringComparison.OrdinalIgnoreCase)) return VersionBump.Major;
        if (plan.Contains("AITeamVersionBump: MINOR", StringComparison.OrdinalIgnoreCase)) return VersionBump.Minor;
        if (plan.Contains("AITeamVersionBump: PATCH", StringComparison.OrdinalIgnoreCase)) return VersionBump.Patch;
        return VersionBump.None;
    }

    internal static bool ReviewPassed(string review) =>
        review.Contains("AITeamReview: PASS", StringComparison.OrdinalIgnoreCase) &&
        !review.Contains("AITeamReview: REPAIR", StringComparison.OrdinalIgnoreCase);

    internal static VersionBump ParseVersionBumpConfirm(string review)
    {
        if (review.Contains("AITeamVersionBumpConfirm: MAJOR", StringComparison.OrdinalIgnoreCase)) return VersionBump.Major;
        if (review.Contains("AITeamVersionBumpConfirm: MINOR", StringComparison.OrdinalIgnoreCase)) return VersionBump.Minor;
        if (review.Contains("AITeamVersionBumpConfirm: PATCH", StringComparison.OrdinalIgnoreCase)) return VersionBump.Patch;
        return VersionBump.None;
    }

    internal static bool IsPlanGateNeedsInput(string reply) =>
        reply.TrimStart().StartsWith("AITeamPlanStatus: NEEDS_INPUT", StringComparison.OrdinalIgnoreCase);

    internal static string ExtractPlanGateBody(string reply)
    {
        var lines = reply.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        if (lines.Count > 0 && lines[0].StartsWith("AITeamPlanStatus:", StringComparison.OrdinalIgnoreCase))
            lines.RemoveAt(0);
        return string.Join(Environment.NewLine, lines).Trim();
    }

    internal static int ParsePrNumberFromUrl(string output)
    {
        var matches = Regex.Matches(output, @"/pull/(\d+)");
        if (matches.Count == 0)
            throw new InvalidOperationException("無法從 gh pr create 的輸出解析 PR 編號：" + FirstUsefulLine(output));
        return int.Parse(matches[^1].Groups[1].Value);
    }

    internal static string ResolveMergeFlag(string? mergeStrategy) => mergeStrategy?.Trim().ToLowerInvariant() switch
    {
        "squash" => "--squash",
        "rebase" => "--rebase",
        _ => "--merge"
    };

    internal static string? ParsePrState(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("state", out var state) ? state.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? ParseRepairDispute(string repairResult)
    {
        var text = repairResult.TrimStart();
        if (!text.StartsWith("AITeamRepairStance: DISPUTE", StringComparison.OrdinalIgnoreCase))
            return null;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        lines.RemoveAt(0);
        var reason = string.Join(Environment.NewLine, lines).Trim();
        return reason.Length == 0 ? "（未提供理由）" : reason;
    }

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

    internal static bool TryPickDifferentFromAny(
        IReadOnlyCollection<ProviderId> available,
        IReadOnlyCollection<ProviderId> excluded,
        IReadOnlyList<ProviderId> preferences,
        out ProviderId result)
    {
        foreach (var provider in preferences)
        {
            if (!excluded.Contains(provider) && available.Contains(provider))
            {
                result = provider;
                return true;
            }
        }
        result = default;
        return false;
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
}
