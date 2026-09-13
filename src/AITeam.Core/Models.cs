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

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

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
