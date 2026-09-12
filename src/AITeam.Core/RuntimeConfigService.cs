using System.Text;
using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

public sealed record WorkflowSettings(int MaxRepairRounds)
{
    public static readonly WorkflowSettings Default = new(3);
}

public sealed record AgentsConfig(
    string CodexCommand,
    string ClaudeCommand,
    string AntigravityCommand)
{
    public static readonly AgentsConfig Default = new("codex", "claude", "agy");

    public string CommandFor(ProviderId provider) => provider switch
    {
        ProviderId.Codex => CodexCommand,
        ProviderId.Claude => ClaudeCommand,
        ProviderId.Antigravity => AntigravityCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}

public sealed class RuntimeConfigService
{
    private readonly string _runtimeRoot;

    public RuntimeConfigService(string runtimeRoot) => _runtimeRoot = runtimeRoot;

    public WorkflowSettings LoadWorkflowSettings()
    {
        var json = ReadFirstExisting("settings.json");
        if (json is null) return WorkflowSettings.Default;

        try
        {
            return ParseWorkflowSettings(json);
        }
        catch
        {
            return WorkflowSettings.Default;
        }
    }

    public AgentsConfig LoadAgentsConfig()
    {
        var json = ReadFirstExisting("agents.json");
        if (json is null) return AgentsConfig.Default;

        try
        {
            return ParseAgentsConfig(json);
        }
        catch
        {
            return AgentsConfig.Default;
        }
    }

    internal static WorkflowSettings ParseWorkflowSettings(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var maxRepairRounds = WorkflowSettings.Default.MaxRepairRounds;
        if (document.RootElement.TryGetProperty("workflow", out var workflow) &&
            workflow.ValueKind == JsonValueKind.Object &&
            workflow.TryGetProperty("max_repair_rounds", out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed) &&
            parsed >= 0)
        {
            maxRepairRounds = parsed;
        }

        return new WorkflowSettings(maxRepairRounds);
    }

    internal static AgentsConfig ParseAgentsConfig(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var codex = AgentsConfig.Default.CodexCommand;
        var claude = AgentsConfig.Default.ClaudeCommand;
        var antigravity = AgentsConfig.Default.AntigravityCommand;

        if (document.RootElement.TryGetProperty("agents", out var agents) && agents.ValueKind == JsonValueKind.Object)
        {
            codex = ReadCommand(agents, "codex", codex);
            claude = ReadCommand(agents, "claude", claude);
            antigravity = ReadCommand(agents, "antigravity", antigravity);
        }

        return new AgentsConfig(codex, claude, antigravity);
    }

    private static string ReadCommand(JsonElement agents, string key, string fallback)
    {
        if (!agents.TryGetProperty(key, out var entry) || entry.ValueKind != JsonValueKind.Object)
            return fallback;
        if (!entry.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String)
            return fallback;
        var text = command.GetString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    private string? ReadFirstExisting(string fileName)
    {
        foreach (var candidate in new[]
        {
            Path.Combine(_runtimeRoot, "data", fileName),
            Path.Combine(_runtimeRoot, "config", fileName)
        })
        {
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate, Encoding.UTF8);
            }
        }
        return null;
    }
}
