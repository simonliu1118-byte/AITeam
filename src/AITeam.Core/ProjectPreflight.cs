using System.Text.RegularExpressions;
using AITeam.Models;

namespace AITeam.Services;

public enum PreflightSeverity
{
    /// <summary>擋下來，不要送出。</summary>
    Blocker,

    /// <summary>可以送出，但先講清楚會有什麼後果。</summary>
    Warning
}

public sealed record PreflightIssue(PreflightSeverity Severity, string Title, string Detail);

/// <summary>
/// 送出任務前的前置檢查。以前 VERSION 檔的檢查是在「升版號」那一步才做，
/// 但那時候調查、規劃、實作、審查全部都已經跑完，AI 額度早就燒掉一大半，
/// 才告訴使用者這個專案根本不能做——該講的話要在開始前就講。
/// </summary>
public static class ProjectPreflight
{
    public const string VersionFileName = "VERSION";

    private static readonly Regex PlainVersion = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    public static IReadOnlyList<PreflightIssue> Inspect(ProjectEntry project, int onlineProviderCount)
    {
        var issues = new List<PreflightIssue>();

        if (onlineProviderCount <= 0)
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Blocker,
                "目前沒有可用的 AI",
                "請先按「重新檢查」，至少要有一個 AI 上線才能送出任務。"));
        }
        else if (onlineProviderCount < 3)
        {
            var mode = ReviewModeExtensions.ForProviderCount(onlineProviderCount);
            issues.Add(new PreflightIssue(
                PreflightSeverity.Warning,
                $"把關強度：{mode.ToFriendlyName()}（{onlineProviderCount} 個 AI 可用）",
                mode.Describe()));
        }

        if (!Directory.Exists(project.RepoPath))
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Blocker,
                "找不到本機 Repo",
                $"專案登錄的位置不存在：{project.RepoPath}。請到「管理專案」重新載入 Repo。"));
            return issues;
        }

        var projectDirectory = project.PhysicalPath;
        if (!Directory.Exists(projectDirectory))
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Blocker,
                "找不到專案目錄",
                $"Repo 裡找不到登錄的專案位置：{projectDirectory}。請到「管理專案」重新選擇目錄。"));
            return issues;
        }

        var versionProblem = DescribeVersionFileProblem(projectDirectory);
        if (versionProblem is not null)
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Warning,
                "版本檔有問題，修改任務會被擋下",
                versionProblem + " 查詢類的需求不受影響。"));
        }

        if (!HasGitHubActions(project.RepoPath))
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Warning,
                "這個 Repo 還沒有 GitHub Actions CI",
                "如果這次是修改任務，計畫會一併要求補建一份最小可用的 CI，跟這次的修改一起送出。"));
        }

        return issues;
    }

    /// <summary>VERSION 檔沒問題時回傳 null，有問題時回傳可以直接顯示給使用者的原因。</summary>
    public static string? DescribeVersionFileProblem(string projectDirectory)
    {
        var versionPath = Path.GetFullPath(Path.Combine(projectDirectory, VersionFileName));
        var root = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        // 防止 projectDirectory 被設成會跳出專案範圍的路徑時，讀到別人的 VERSION。
        if (!versionPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(versionPath))
        {
            return "這個專案的根目錄找不到 VERSION 檔。依共用規則，每個可發行專案根目錄都必須有 VERSION 檔（內容只放 X.Y.Z）。";
        }

        string current;
        try
        {
            current = File.ReadAllText(versionPath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"讀不到 VERSION 檔：{ex.Message}";
        }

        return PlainVersion.IsMatch(current)
            ? null
            : $"VERSION 檔目前是「{current}」，不是單純的 X.Y.Z；AITeam 不會猜測 prerelease 或特殊版號規則。";
    }

    private static bool HasGitHubActions(string repoPath)
    {
        var workflows = Path.Combine(repoPath, ".github", "workflows");
        if (!Directory.Exists(workflows)) return false;

        return Directory.EnumerateFiles(workflows, "*.yml").Any()
            || Directory.EnumerateFiles(workflows, "*.yaml").Any();
    }
}
