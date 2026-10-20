using System.Text;
using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

public sealed record WorkflowSettings(int MaxRepairRounds, int MaxCiRepairRounds = 3, RoleOrder? Roles = null)
{
    public static readonly WorkflowSettings Default = new(3, 3);

    /// <summary>設定檔沒寫分工時用預設分工。</summary>
    public RoleOrder EffectiveRoles => Roles ?? RoleOrder.Default;
}

/// <summary>
/// 修改管線五個角色各自的人選順序：排在前面的優先，沒上線就往後找。
///
/// 預設分工的原則是看成本：Claude 額度最貴、GPT 其次、Gemini 最便宜但實作能力有限。
/// 所以便宜的做大量閱讀（偵察、挑戰），GPT 做最吃額度的實作，Claude 只做份量少
/// 但需要判斷的規劃與最終審查。Gemini 一律排在實作候補的最後：它最慢，而實作會在
/// 修正與 CI 修正裡被反覆呼叫，Google 也明講不要用 Antigravity 跑大量自動迴圈。
/// </summary>
public sealed record RoleOrder(
    IReadOnlyList<ProviderId> Scout,
    IReadOnlyList<ProviderId> Planner,
    IReadOnlyList<ProviderId> Implementer,
    IReadOnlyList<ProviderId> Challenger,
    IReadOnlyList<ProviderId> FinalReviewer)
{
    public static readonly RoleOrder Default = new(
        Scout: new[] { ProviderId.Antigravity, ProviderId.Codex, ProviderId.Claude },
        Planner: new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Antigravity },
        Implementer: new[] { ProviderId.Codex, ProviderId.Claude, ProviderId.Antigravity },
        Challenger: new[] { ProviderId.Antigravity, ProviderId.Claude, ProviderId.Codex },
        FinalReviewer: new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Antigravity });

    /// <summary>
    /// 把設定檔裡的一串名字變成人選順序。認不得的名字略過、重複的只算一次；
    /// 沒寫到的 AI 照預設順序補在最後——寫漏一家不該讓它永遠不會被選到，
    /// 否則其他人沒上線時，任務會因為「找不到可用 AI」而失敗。
    /// </summary>
    internal static IReadOnlyList<ProviderId> Normalise(IEnumerable<string?> names, IReadOnlyList<ProviderId> fallback)
    {
        var result = new List<ProviderId>();
        foreach (var name in names)
        {
            if (TryParseProvider(name, out var provider) && !result.Contains(provider))
                result.Add(provider);
        }

        foreach (var provider in fallback)
        {
            if (!result.Contains(provider)) result.Add(provider);
        }

        return result;
    }

    /// <summary>設定檔裡可以寫產品名，也可以寫 AITeam 介面上看到的名字。</summary>
    internal static bool TryParseProvider(string? name, out ProviderId provider)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "claude":
                provider = ProviderId.Claude;
                return true;
            case "codex":
            case "gpt":
            case "openai":
                provider = ProviderId.Codex;
                return true;
            case "antigravity":
            case "agy":
            case "gemini":
                provider = ProviderId.Antigravity;
                return true;
            default:
                provider = default;
                return false;
        }
    }
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
        RoleOrder? roles = null;
        if (document.RootElement.TryGetProperty("workflow", out var workflow) &&
            workflow.ValueKind == JsonValueKind.Object)
        {
            if (workflow.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Object)
                roles = ParseRoles(rolesElement);

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

        return new WorkflowSettings(maxRepairRounds, maxCiRepairRounds, roles);
    }

    private static RoleOrder ParseRoles(JsonElement roles)
    {
        IReadOnlyList<ProviderId> Read(string key, IReadOnlyList<ProviderId> fallback)
        {
            if (!roles.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
                return fallback;
            var names = value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString());
            return RoleOrder.Normalise(names, fallback);
        }

        var defaults = RoleOrder.Default;
        return new RoleOrder(
            Read("scout", defaults.Scout),
            Read("planner", defaults.Planner),
            Read("implementer", defaults.Implementer),
            Read("challenger", defaults.Challenger),
            Read("final_reviewer", defaults.FinalReviewer));
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
/// 程式記下來的使用者偏好（例如上次選了誰整理定案書）。跟 settings.json／agents.json
/// 不同的是，這份是程式自己寫回去的，不是給人手動編輯的。
/// </summary>
/// <param name="CodexModel">
/// Codex 要用哪個模型。null＝交給 Codex 自己的預設。Codex 的預設模型你的帳號不能用時，
/// 健康檢查會自動試出一個能用的並記在這裡，之後每次叫 Codex 都指定它。
/// </param>
public sealed record AppPreferences(ProviderId? DecisionWriter = null, string? CodexModel = null)
{
    public static readonly AppPreferences Default = new();
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
            decision_writer = preferences.DecisionWriter?.ToString(),
            codex_model = preferences.CodexModel
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

        var writer = AppPreferences.Default.DecisionWriter;

        // 記下來的那家可能已經不在線上，甚至可能是舊版留下來的名字；
        // 認不得就當作沒記過，讓預設順序去決定。
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("decision_writer", out var writerValue) &&
            writerValue.ValueKind == JsonValueKind.String &&
            Enum.TryParse<ProviderId>(writerValue.GetString(), ignoreCase: true, out var parsed))
        {
            writer = parsed;
        }

        string? codexModel = null;
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("codex_model", out var modelValue) &&
            modelValue.ValueKind == JsonValueKind.String &&
            CodexModels.IsValidSlug(modelValue.GetString()))
        {
            codexModel = modelValue.GetString();
        }

        return new AppPreferences(writer, codexModel);
    }
}
