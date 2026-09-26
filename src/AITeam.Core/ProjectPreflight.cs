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
        if (project.IsPendingCreation)
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Warning,
                "這是新專案，資料夾還沒建立",
                $"「{project.RepoSubpath}」在 GitHub 上還不存在。這次的任務會直接當成修改任務，"
                + "一併建立資料夾與 VERSION 檔（開始前會問你要從哪一版開始），跟著這次的修改一起送 PR。"));
        }
        else if (!Directory.Exists(projectDirectory))
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Blocker,
                "找不到專案目錄",
                $"Repo 裡找不到登錄的專案位置：{projectDirectory}。請到「管理專案」重新選擇目錄。"));
            return issues;
        }
        else if (IsVersionFileMissing(projectDirectory))
        {
            issues.Add(new PreflightIssue(
                PreflightSeverity.Warning,
                "這個專案還沒有 VERSION 檔",
                "如果這次是修改任務，開始前會問你要從哪一版開始，並跟著這次的修改一起建立 VERSION 檔。"
                + "查詢類的需求不受影響。"));
        }
        else if (DescribeVersionFileProblem(projectDirectory) is { } versionProblem)
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

    // 用 Windows 的規則，而不是 Path.GetInvalidFileNameChars()——後者在 Linux 上只擋 / 和 \0，
    // 測試在 Linux 跑會放過一堆在使用者電腦上根本建不出來的名字。
    private static readonly char[] WindowsInvalidNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// 新資料夾的名稱能不能用。回傳 null 代表可以，否則回傳可以直接給使用者看的原因。
    /// </summary>
    public static string? DescribeFolderNameProblem(string? name, IEnumerable<string> existingSiblings)
    {
        var value = name ?? "";
        if (value.Trim().Length == 0) return "請輸入資料夾名稱。";
        if (value.Contains('/') || value.Contains('\\'))
            return "一次只能建立一層資料夾；要建更深的，先選好上一層再按一次「＋ 新增資料夾」。";
        if (value != value.Trim() || value.StartsWith('.') || value.EndsWith('.'))
            return "資料夾名稱不能用空白或句點開頭、結尾。";
        if (value.IndexOfAny(WindowsInvalidNameChars) >= 0 || value.Any(char.IsControl))
            return "資料夾名稱不能包含這些符號：\\ / : * ? \" < > |";
        if (WindowsReservedNames.Contains(value.Split('.')[0]))
            return "這是 Windows 保留的名稱，不能拿來當資料夾名稱。";
        if (existingSiblings.Any(s => s.Equals(value, StringComparison.OrdinalIgnoreCase)))
            return "這一層已經有同名的資料夾了。";
        return null;
    }

    /// <summary>
    /// VERSION 檔是「根本不存在」，還是「存在但內容不對」？兩者處理方式不同：
    /// 不存在可以問使用者要從哪一版開始、幫他建立；內容不對就不能亂猜，只能停下來。
    /// </summary>
    public static bool IsVersionFileMissing(string projectDirectory) =>
        !File.Exists(Path.Combine(projectDirectory, VersionFileName));

    /// <summary>使用者自己輸入的起始版號要是單純的 X.Y.Z，跟 VERSION 檔的規則一樣。</summary>
    public static bool IsPlainVersion(string? text) => text is not null && PlainVersion.IsMatch(text.Trim());

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
