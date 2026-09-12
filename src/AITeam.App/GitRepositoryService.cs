using System.Text.RegularExpressions;
using AITeam.Models;

namespace AITeam.Services;

public sealed record RepoTreeSnapshot(
    string GitHubRepo,
    string RepoPath,
    string DefaultBranch,
    IReadOnlyList<string> Directories);

public sealed class GitRepositoryService
{
    private static readonly Regex RepoPattern = new("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private readonly string _runtimeRoot;
    private readonly ProcessRunner _runner;

    public GitRepositoryService(string runtimeRoot, ProcessRunner runner)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
    }

    public async Task<RepoTreeSnapshot> LoadRemoteTreeAsync(string githubRepo, CancellationToken cancellationToken)
    {
        var repo = NormalizeGitHubRepo(githubRepo);
        var repoPath = await EnsureLocalRepoAsync(repo, cancellationToken);
        await RunGitCheckedAsync(repoPath, new[] { "fetch", "--prune", "origin" }, cancellationToken);
        var branch = await GetRemoteDefaultBranchAsync(repoPath, null, cancellationToken);
        var result = await RunGitCheckedAsync(repoPath, new[] { "ls-tree", "-d", "-r", "--name-only", $"origin/{branch}" }, cancellationToken);

        var directories = result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().Replace('\\', '/'))
            .Where(x => x.Length > 0)
            .Where(x => !x.Split('/').Any(part => part.Equals(".github", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RepoTreeSnapshot(repo, repoPath, branch, directories);
    }

    public async Task<string> SafeSyncAsync(string repoPath, string? preferredBranch, CancellationToken cancellationToken)
    {
        await RunGitCheckedAsync(repoPath, new[] { "fetch", "--prune", "origin" }, cancellationToken);
        var branch = await GetRemoteDefaultBranchAsync(repoPath, preferredBranch, cancellationToken);
        var remoteRef = $"origin/{branch}";

        var dirty = await RunGitCheckedAsync(repoPath, new[] { "status", "--porcelain" }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(dirty.StandardOutput))
        {
            throw new InvalidOperationException("AITeam 管理的本機 Repo 有未提交變更。為避免覆蓋資料，不會自動同步；請先處理該 Repo 的本機變更。");
        }

        var current = await RunGitCheckedAsync(repoPath, new[] { "branch", "--show-current" }, cancellationToken);
        var currentBranch = current.StandardOutput.Trim();
        if (!currentBranch.Equals(branch, StringComparison.OrdinalIgnoreCase))
        {
            var switchResult = await RunGitAsync(repoPath, new[] { "switch", branch }, cancellationToken);
            if (switchResult.ExitCode != 0)
            {
                switchResult = await RunGitAsync(repoPath, new[] { "switch", "-c", branch, "--track", remoteRef }, cancellationToken);
                if (switchResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"無法切換到預設分支 {branch}：{FirstUsefulLine(switchResult)}");
                }
            }
        }

        var counts = await RunGitCheckedAsync(repoPath, new[] { "rev-list", "--left-right", "--count", $"HEAD...{remoteRef}" }, cancellationToken);
        var parts = counts.StandardOutput.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out var localOnly) || !int.TryParse(parts[1], out var remoteOnly))
        {
            throw new InvalidOperationException("無法判斷本機與 GitHub 分支差異。");
        }

        if (localOnly > 0)
        {
            throw new InvalidOperationException($"本機 {branch} 有尚未存在於 GitHub 的 commit。AITeam 不會自動覆蓋或重寫這些 commit。");
        }

        if (remoteOnly > 0)
        {
            await RunGitCheckedAsync(repoPath, new[] { "merge", "--ff-only", remoteRef }, cancellationToken);
        }

        return branch;
    }

    private async Task<string> EnsureLocalRepoAsync(string githubRepo, CancellationToken cancellationToken)
    {
        var reposRoot = Path.Combine(_runtimeRoot, "repos");
        Directory.CreateDirectory(reposRoot);

        var parts = githubRepo.Split('/');
        var owner = parts[0];
        var name = parts[1];
        var primary = Path.Combine(reposRoot, name);

        foreach (var candidate in new[] { primary, Path.Combine(reposRoot, $"{owner}--{name}") })
        {
            if (!Directory.Exists(candidate)) continue;
            var remote = await RunGitAsync(candidate, new[] { "config", "--get", "remote.origin.url" }, cancellationToken);
            if (remote.ExitCode == 0 && RemoteMatches(remote.StandardOutput.Trim(), githubRepo))
            {
                return candidate;
            }
        }

        var destination = !Directory.Exists(primary) ? primary : Path.Combine(reposRoot, $"{owner}--{name}");
        if (Directory.Exists(destination))
        {
            throw new InvalidOperationException($"AITeam 自動分配的 Repo 位置已存在但無法辨識：{destination}");
        }

        var url = $"https://github.com/{githubRepo}.git";
        var clone = await _runner.RunAsync(
            "git",
            new[] { "clone", "--origin", "origin", url, destination },
            reposRoot,
            null,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        if (clone.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git clone 失敗：{FirstUsefulLine(clone)}");
        }

        return destination;
    }

    private async Task<string> GetRemoteDefaultBranchAsync(string repoPath, string? preferredBranch, CancellationToken cancellationToken)
    {
        var head = await RunGitAsync(repoPath, new[] { "symbolic-ref", "--short", "refs/remotes/origin/HEAD" }, cancellationToken);
        if (head.ExitCode == 0 && !string.IsNullOrWhiteSpace(head.StandardOutput))
        {
            return head.StandardOutput.Trim().Replace("origin/", "", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(preferredBranch))
        {
            var preferredCheck = await RunGitAsync(repoPath, new[] { "rev-parse", "--verify", $"refs/remotes/origin/{preferredBranch}" }, cancellationToken);
            if (preferredCheck.ExitCode == 0) return preferredBranch;
        }

        foreach (var candidate in new[] { "main", "master" })
        {
            var check = await RunGitAsync(repoPath, new[] { "rev-parse", "--verify", $"refs/remotes/origin/{candidate}" }, cancellationToken);
            if (check.ExitCode == 0) return candidate;
        }

        throw new InvalidOperationException("無法判斷 GitHub Repo 的預設分支。");
    }

    private async Task<ProcessRunResult> RunGitAsync(string workingDirectory, IEnumerable<string> args, CancellationToken cancellationToken) =>
        await _runner.RunAsync("git", args, workingDirectory, null, TimeSpan.FromMinutes(2), cancellationToken);

    private async Task<ProcessRunResult> RunGitCheckedAsync(string workingDirectory, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(workingDirectory, args, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git 操作失敗：{FirstUsefulLine(result)}");
        }
        return result;
    }

    private static string NormalizeGitHubRepo(string input)
    {
        var repo = input.Trim();
        if (!RepoPattern.IsMatch(repo))
        {
            throw new InvalidOperationException("GitHub Repo 格式應為 owner/repo，例如 simonliu1118-byte/CYapps。");
        }
        return repo;
    }

    private static bool RemoteMatches(string remote, string githubRepo)
    {
        if (string.IsNullOrWhiteSpace(remote)) return false;
        var normalized = remote.Trim().Replace('\\', '/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }
        return normalized.EndsWith("github.com/" + githubRepo, StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("github.com:" + githubRepo, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstUsefulLine(ProcessRunResult result)
    {
        var combined = string.Join(Environment.NewLine, new[] { result.StandardError, result.StandardOutput }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return combined.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            ?? $"exit code {result.ExitCode}";
    }
}
