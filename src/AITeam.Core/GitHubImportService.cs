using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

/// <summary>GitHub 帳號看得到的一個 Repo。</summary>
public sealed record GitHubRepoListing(string FullName, string Description, bool IsPrivate, bool IsArchived);

/// <summary>匯入時在某個 Repo 裡找到的一個專案（還沒登錄）。</summary>
public sealed record ImportCandidate(
    string Name,
    string GitHubRepo,
    string RepoPath,
    string RepoSubpath,
    string DefaultBranch,
    string Description);

/// <summary>
/// 從 GitHub 匯入專案：先用 gh 列出帳號看得到的所有 Repo，使用者勾選後，
/// 把每個 Repo 下載到本機，找出裡面有 VERSION 檔的資料夾當成一個個專案
/// （共通規則：每個可發行專案的根目錄都有 VERSION）。說明先帶入 README 的第一段。
/// </summary>
public sealed class GitHubImportService
{
    private const int MaxDescriptionLength = 400;

    private readonly string _runtimeRoot;
    private readonly IProcessRunner _runner;
    private readonly GitRepositoryService _git;

    public GitHubImportService(string runtimeRoot, IProcessRunner runner)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
        _git = new GitRepositoryService(runtimeRoot, runner);
    }

    public async Task<IReadOnlyList<GitHubRepoListing>> ListRepositoriesAsync(CancellationToken cancellationToken)
    {
        ProcessRunResult result;
        try
        {
            result = await _runner.RunAsync(
                "gh",
                new[]
                {
                    "api", "--paginate",
                    "user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member",
                    "--jq", ".[] | {full_name, description, private, archived}"
                },
                _runtimeRoot,
                null,
                TimeSpan.FromMinutes(2),
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException("這台電腦還沒有安裝 GitHub CLI（gh），沒辦法列出你的 Repo。");
        }

        if (result.ExitCode != 0)
        {
            var text = $"{result.StandardError}\n{result.StandardOutput}";
            if (text.Contains("auth login", StringComparison.OrdinalIgnoreCase)
                || text.Contains("not logged", StringComparison.OrdinalIgnoreCase)
                || text.Contains("401", StringComparison.Ordinal))
                throw new InvalidOperationException("GitHub CLI 還沒登入。請先在命令列執行 gh auth login 登入 GitHub，再回來匯入。");
            throw new InvalidOperationException("列出 GitHub Repo 失敗：" + FirstLine(text));
        }

        return ParseRepositoryLines(result.StandardOutput);
    }

    /// <summary>gh api --jq 每行輸出一個 JSON 物件；壞掉的行略過，已封存的 Repo 不列。</summary>
    internal static IReadOnlyList<GitHubRepoListing> ParseRepositoryLines(string output)
    {
        var repos = new List<GitHubRepoListing>();
        foreach (var raw in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (!line.StartsWith('{')) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var name = root.TryGetProperty("full_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                if (name.Length == 0) continue;
                var description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
                var isPrivate = root.TryGetProperty("private", out var p) && p.ValueKind == JsonValueKind.True;
                var archived = root.TryGetProperty("archived", out var a) && a.ValueKind == JsonValueKind.True;
                if (archived) continue;
                if (repos.Any(r => r.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                repos.Add(new GitHubRepoListing(name, description.Trim(), isPrivate, archived));
            }
            catch (JsonException)
            {
            }
        }
        return repos;
    }

    /// <summary>
    /// 下載（或更新）這個 Repo，找出裡面的專案。已經登錄過的專案（同一個 Repo、同一個資料夾）不會再列出來。
    /// </summary>
    public async Task<IReadOnlyList<ImportCandidate>> FindProjectsAsync(
        string githubRepo, IReadOnlyList<ProjectEntry> registered, CancellationToken cancellationToken)
    {
        var snapshot = await _git.LoadRemoteTreeAsync(githubRepo, cancellationToken);
        var branch = await _git.SafeSyncAsync(snapshot.RepoPath, snapshot.DefaultBranch, cancellationToken);

        var files = await RunGitAsync(snapshot.RepoPath, new[] { "ls-tree", "-r", "--name-only", $"origin/{branch}" }, cancellationToken);
        var folders = FindProjectFolders(files.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));
        var repoName = snapshot.GitHubRepo.Split('/')[1];

        var candidates = new List<ImportCandidate>();
        foreach (var folder in folders)
        {
            if (registered.Any(p => p.GitHubRepo.Equals(snapshot.GitHubRepo, StringComparison.OrdinalIgnoreCase)
                                    && p.RepoSubpath.Trim('/').Equals(folder, StringComparison.OrdinalIgnoreCase)))
                continue;

            var name = folder.Length == 0 ? repoName : folder.Split('/')[^1];
            var description = ReadReadmeSummary(Path.Combine(snapshot.RepoPath, folder.Replace('/', Path.DirectorySeparatorChar)));
            candidates.Add(new ImportCandidate(name, snapshot.GitHubRepo, snapshot.RepoPath, folder, branch, description));
        }
        return candidates;
    }

    /// <summary>
    /// 有 VERSION 檔的資料夾就是一個專案；整個 Repo 都沒有 VERSION 的話，就把整個 Repo 當成一個專案。
    /// 回傳 Repo 內的相對路徑（"/" 分隔，根目錄是空字串）。
    /// </summary>
    internal static IReadOnlyList<string> FindProjectFolders(IEnumerable<string> trackedFiles)
    {
        var folders = trackedFiles
            .Select(f => f.Trim().Replace('\\', '/'))
            .Where(f => f.Length > 0)
            .Where(f => !f.Split('/').Any(part => part.Equals(".github", StringComparison.OrdinalIgnoreCase)))
            .Where(f => Path.GetFileName(f).Equals("VERSION", StringComparison.Ordinal))
            .Select(f => f.Contains('/') ? f[..f.LastIndexOf('/')] : "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return folders.Count > 0 ? folders : new[] { "" };
    }

    private static string ReadReadmeSummary(string folder)
    {
        try
        {
            var readme = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "README*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => Path.GetFileName(f).Equals("README.md", StringComparison.OrdinalIgnoreCase)
                                         || Path.GetFileNameWithoutExtension(f).Equals("README", StringComparison.OrdinalIgnoreCase))
                : null;
            return readme is null ? "" : SummarizeReadme(File.ReadAllText(readme));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// README 的第一段正文：跳過標題、徽章、圖片、HTML、程式碼區塊與表格，把同一段的幾行接起來。
    /// 太長就在句子結尾處收住，不加「…」。
    /// </summary>
    internal static string SummarizeReadme(string markdown)
    {
        var paragraph = new List<string>();
        var inCode = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inCode = !inCode;
                if (paragraph.Count > 0) break;
                continue;
            }
            if (inCode) continue;

            var skip = line.Length == 0
                       || line.StartsWith('#')
                       || line.StartsWith("![", StringComparison.Ordinal)
                       || line.StartsWith("[![", StringComparison.Ordinal)
                       || line.StartsWith('<')
                       || line.StartsWith('|')
                       || line.StartsWith("---", StringComparison.Ordinal)
                       || line.StartsWith("===", StringComparison.Ordinal);
            if (skip)
            {
                if (paragraph.Count > 0) break;
                continue;
            }

            paragraph.Add(line.TrimStart('>', ' ', '-', '*'));
        }

        var text = string.Join(" ", paragraph).Replace("**", "").Replace("`", "").Trim();
        if (text.Length <= MaxDescriptionLength) return text;

        var cut = text[..MaxDescriptionLength];
        var end = cut.LastIndexOfAny(new[] { '。', '！', '？', '.', '!', '?' });
        return (end > MaxDescriptionLength / 3 ? cut[..(end + 1)] : cut).Trim();
    }

    /// <summary>名稱重複時（跟已登錄的專案或同一批匯入的專案）在後面加上 Repo 名稱區分。</summary>
    public static string UniqueName(string name, string githubRepo, IEnumerable<string> takenNames)
    {
        var taken = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        var withRepo = $"{name}（{githubRepo.Split('/')[1]}）";
        if (!taken.Contains(withRepo)) return withRepo;
        for (var i = 2; ; i++)
        {
            var numbered = $"{withRepo} {i}";
            if (!taken.Contains(numbered)) return numbered;
        }
    }

    public static ProjectEntry ToProjectEntry(ImportCandidate candidate, string name) => new()
    {
        Name = name,
        Description = candidate.Description,
        ProjectPath = candidate.RepoSubpath.Length == 0
            ? candidate.RepoPath
            : Path.Combine(candidate.RepoPath, candidate.RepoSubpath.Replace('/', Path.DirectorySeparatorChar)),
        RepoName = candidate.GitHubRepo.Split('/')[1],
        RepoPath = candidate.RepoPath,
        RepoSubpath = candidate.RepoSubpath,
        GitHubRepo = candidate.GitHubRepo,
        DefaultBranch = candidate.DefaultBranch,
        Active = true
    };

    private async Task<string> RunGitAsync(string workingDirectory, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync("git", args, workingDirectory, null, TimeSpan.FromMinutes(2), cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Git 操作失敗：" + FirstLine($"{result.StandardError}\n{result.StandardOutput}"));
        return result.StandardOutput;
    }

    private static string FirstLine(string text) =>
        text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0)
        ?? "（沒有錯誤訊息）";
}
