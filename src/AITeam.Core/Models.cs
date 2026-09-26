using System.Text.Json;
using System.Text.Json.Serialization;

namespace AITeam.Models;

public sealed class ProjectRegistryDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("projects")]
    public List<ProjectEntry> Projects { get; set; } = new();
}

public sealed class ProjectEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>這個專案是做什麼的，由使用者自行填寫，會一併交給 AI 當作背景說明。</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("project_path")]
    public string ProjectPath { get; set; } = "";

    [JsonPropertyName("repo_name")]
    public string RepoName { get; set; } = "";

    [JsonPropertyName("repo_path")]
    public string RepoPath { get; set; } = "";

    [JsonPropertyName("repo_subpath")]
    public string RepoSubpath { get; set; } = "";

    [JsonPropertyName("github_repo")]
    public string GitHubRepo { get; set; } = "";

    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; set; } = "main";

    [JsonPropertyName("active")]
    public bool? Active { get; set; } = true;

    /// <summary>
    /// 使用者在「管理專案」裡新開的資料夾，GitHub 上還沒有。第一次修改任務會把它連同
    /// VERSION 檔一起建立、送 PR；合併之後資料夾就真的存在，這個標記也就自然失效。
    /// 只有明確標記過的專案才會被當成「待建立」——一個原本存在、後來被刪掉或改名的
    /// 專案目錄，絕對不能被悄悄地重新建立。
    /// </summary>
    [JsonPropertyName("pending_creation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PendingCreation { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>標記為待建立，而且預設分支上確實還沒有這個資料夾。</summary>
    [JsonIgnore]
    public bool IsPendingCreation => PendingCreation == true && !Directory.Exists(PhysicalPath);

    [JsonIgnore]
    public string PhysicalPath =>
        !string.IsNullOrWhiteSpace(ProjectPath)
            ? ProjectPath
            : string.IsNullOrWhiteSpace(RepoSubpath)
                ? RepoPath
                : Path.Combine(RepoPath, RepoSubpath.Replace('/', Path.DirectorySeparatorChar));

    public override string ToString() => Name;
}

public enum ProviderId
{
    Codex,
    Claude,
    Antigravity
}

public static class ProviderIdExtensions
{
    public static string ToFriendlyName(this ProviderId provider) => provider switch
    {
        ProviderId.Codex => "GPT / Codex",
        ProviderId.Claude => "Claude",
        ProviderId.Antigravity => "Gemini / Antigravity",
        _ => provider.ToString()
    };
}

public enum ProviderHealthState
{
    Unknown,
    Checking,
    Online,
    Quota,
    AuthenticationRequired,
    TemporaryError,
    Error,
    Missing
}

public static class ProviderHealthStateExtensions
{
    public static string ToFriendlyName(this ProviderHealthState state) => state switch
    {
        ProviderHealthState.Unknown => "待檢查",
        ProviderHealthState.Checking => "檢測中",
        ProviderHealthState.Online => "上線",
        ProviderHealthState.Quota => "超過限額",
        ProviderHealthState.AuthenticationRequired => "需要重新登入",
        ProviderHealthState.TemporaryError => "暫時異常",
        ProviderHealthState.Error => "錯誤",
        ProviderHealthState.Missing => "CLI 未安裝",
        _ => state.ToString()
    };
}

public sealed record ProviderHealth(
    ProviderId Provider,
    ProviderHealthState State,
    string Message,
    TimeSpan Duration,
    // CLI 原文（已壓成一行並截斷）。畫面上只顯示分類後的短句，但分類錯的時候
    // 使用者完全看不到 CLI 到底說了什麼，等於無從查起；原文一定要留著寫進紀錄。
    string Detail = "")
{
    public static ProviderHealth Checking(ProviderId id) =>
        new(id, ProviderHealthState.Checking, "檢查中...", TimeSpan.Zero);
}

public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);
