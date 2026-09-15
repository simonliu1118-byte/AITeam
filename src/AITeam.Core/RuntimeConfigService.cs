using System.Text;
using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

public sealed record WorkflowSettings(int MaxRepairRounds, int MaxCiRepairRounds = 3)
{
    public static readonly WorkflowSettings Default = new(3, 3);
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
        var maxCiRepairRounds = WorkflowSettings.Default.MaxCiRepairRounds;
        if (document.RootElement.TryGetProperty("workflow", out var workflow) &&
            workflow.ValueKind == JsonValueKind.Object)
        {
            if (workflow.TryGetProperty("max_repair_rounds", out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out var parsed) &&
                parsed >= 0)
            {
                maxRepairRounds = parsed;
            }

            if (workflow.TryGetProperty("max_ci_repair_rounds", out var ciValue) &&
                ciValue.ValueKind == JsonValueKind.Number &&
                ciValue.TryGetInt32(out var ciParsed) &&
                ciParsed >= 0)
            {
                maxCiRepairRounds = ciParsed;
            }
        }

        return new WorkflowSettings(maxRepairRounds, maxCiRepairRounds);
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

/// <summary>
/// 使用者自己在「設定」視窗裡調的偏好。跟 settings.json／agents.json 不同的是，
/// 這份是程式自己寫回去的，不是給人手動編輯的。
/// </summary>
public sealed record AppPreferences(bool HideCliConsole, ProviderId? DecisionWriter = null)
{
    public static readonly AppPreferences Default = new(false);
}

public sealed class AppPreferencesService
{
    private readonly string _runtimeRoot;

    public AppPreferencesService(string runtimeRoot) => _runtimeRoot = runtimeRoot;

    private string FilePath => Path.Combine(_runtimeRoot, "data", "preferences.json");

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return AppPreferences.Default;
            return Parse(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch
        {
            // 偏好讀不到就用預設值，不要讓程式開不起來。
            return AppPreferences.Default;
        }
    }

    public void Save(AppPreferences preferences)
    {
        var payload = new
        {
            schema_version = 1,
            hide_cli_console = preferences.HideCliConsole,
            decision_writer = preferences.DecisionWriter?.ToString()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            new UTF8Encoding(false));
    }

    internal static AppPreferences Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var hide = AppPreferences.Default.HideCliConsole;
        var writer = AppPreferences.Default.DecisionWriter;

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (document.RootElement.TryGetProperty("hide_cli_console", out var value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                hide = value.GetBoolean();
            }

            // 記下來的那家可能已經不在線上，甚至可能是舊版留下來的名字；
            // 認不得就當作沒記過，讓預設順序去決定。
            if (document.RootElement.TryGetProperty("decision_writer", out var writerValue) &&
                writerValue.ValueKind == JsonValueKind.String &&
                Enum.TryParse<ProviderId>(writerValue.GetString(), ignoreCase: true, out var parsed))
            {
                writer = parsed;
            }
        }

        return new AppPreferences(hide, writer);
    }
}
