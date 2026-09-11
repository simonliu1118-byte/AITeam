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

    [JsonPropertyName("version_file")]
    public string VersionFile { get; set; } = "";

    [JsonPropertyName("tag_prefix")]
    public string TagPrefix { get; set; } = "";

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

public sealed record ProviderHealth(
    ProviderId Provider,
    ProviderHealthState State,
    string Message,
    TimeSpan Duration)
{
    public static ProviderHealth Checking(ProviderId id) =>
        new(id, ProviderHealthState.Checking, "檢查中...", TimeSpan.Zero);
}

public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);
