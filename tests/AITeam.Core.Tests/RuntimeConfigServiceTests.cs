using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class RuntimeConfigServiceParsingTests
{
    [Fact]
    public void ParseWorkflowSettings_ReadsMaxRepairRounds()
    {
        const string json = """
        {
          "schema_version": 2,
          "workflow": { "max_repair_rounds": 5 }
        }
        """;

        var settings = RuntimeConfigService.ParseWorkflowSettings(json);

        Assert.Equal(5, settings.MaxRepairRounds);
    }

    [Fact]
    public void ParseWorkflowSettings_DefaultsWhenFieldMissing()
    {
        const string json = "{ \"schema_version\": 2 }";

        var settings = RuntimeConfigService.ParseWorkflowSettings(json);

        Assert.Equal(WorkflowSettings.Default.MaxRepairRounds, settings.MaxRepairRounds);
    }

    [Fact]
    public void ParseWorkflowSettings_IgnoresNegativeValue()
    {
        const string json = """{ "workflow": { "max_repair_rounds": -1 } }""";

        var settings = RuntimeConfigService.ParseWorkflowSettings(json);

        Assert.Equal(WorkflowSettings.Default.MaxRepairRounds, settings.MaxRepairRounds);
    }

    [Fact]
    public void ParseWorkflowSettings_ReadsMaxCiRepairRounds()
    {
        const string json = """{ "workflow": { "max_ci_repair_rounds": 4 } }""";

        var settings = RuntimeConfigService.ParseWorkflowSettings(json);

        Assert.Equal(4, settings.MaxCiRepairRounds);
    }

    [Fact]
    public void ParseWorkflowSettings_DefaultsMaxCiRepairRounds_WhenFieldMissing()
    {
        const string json = """{ "workflow": { "max_repair_rounds": 5 } }""";

        var settings = RuntimeConfigService.ParseWorkflowSettings(json);

        Assert.Equal(WorkflowSettings.Default.MaxCiRepairRounds, settings.MaxCiRepairRounds);
    }

    [Fact]
    public void ParseAgentsConfig_ReadsAllCommandOverrides()
    {
        const string json = """
        {
          "schema_version": 1,
          "agents": {
            "codex": { "command": "my-codex", "enabled": true },
            "claude": { "command": "my-claude", "enabled": true },
            "antigravity": { "command": "my-agy", "enabled": false }
          }
        }
        """;

        var agents = RuntimeConfigService.ParseAgentsConfig(json);

        Assert.Equal("my-codex", agents.CodexCommand);
        Assert.Equal("my-claude", agents.ClaudeCommand);
        Assert.Equal("my-agy", agents.AntigravityCommand);
        Assert.Equal("my-codex", agents.CommandFor(ProviderId.Codex));
        Assert.Equal("my-claude", agents.CommandFor(ProviderId.Claude));
        Assert.Equal("my-agy", agents.CommandFor(ProviderId.Antigravity));
    }

    [Fact]
    public void ParseAgentsConfig_FallsBackToDefaultCommand_WhenEntryMissingOrBlank()
    {
        const string json = """
        {
          "agents": {
            "codex": { "command": "  " }
          }
        }
        """;

        var agents = RuntimeConfigService.ParseAgentsConfig(json);

        Assert.Equal(AgentsConfig.Default.CodexCommand, agents.CodexCommand);
        Assert.Equal(AgentsConfig.Default.ClaudeCommand, agents.ClaudeCommand);
        Assert.Equal(AgentsConfig.Default.AntigravityCommand, agents.AntigravityCommand);
    }

    [Fact]
    public void ParseAgentsConfig_DefaultsWhenAgentsSectionMissing()
    {
        const string json = "{ \"schema_version\": 1 }";

        var agents = RuntimeConfigService.ParseAgentsConfig(json);

        Assert.Equal(AgentsConfig.Default, agents);
    }
}

public sealed class RuntimeConfigServiceFileResolutionTests : IDisposable
{
    private readonly string _root;

    public RuntimeConfigServiceFileResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aiteam-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void LoadWorkflowSettings_ReturnsDefault_WhenNoFileExists()
    {
        var service = new RuntimeConfigService(_root);

        var settings = service.LoadWorkflowSettings();

        Assert.Equal(WorkflowSettings.Default, settings);
    }

    [Fact]
    public void LoadWorkflowSettings_PrefersDataOverLegacyConfig()
    {
        var dataDir = Path.Combine(_root, "data");
        var configDir = Path.Combine(_root, "config");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), """{ "workflow": { "max_repair_rounds": 7 } }""");
        File.WriteAllText(Path.Combine(configDir, "settings.json"), """{ "workflow": { "max_repair_rounds": 1 } }""");

        var service = new RuntimeConfigService(_root);
        var settings = service.LoadWorkflowSettings();

        Assert.Equal(7, settings.MaxRepairRounds);
    }

    [Fact]
    public void LoadWorkflowSettings_FallsBackToLegacyConfigPath()
    {
        var configDir = Path.Combine(_root, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "settings.json"), """{ "workflow": { "max_repair_rounds": 2 } }""");

        var service = new RuntimeConfigService(_root);
        var settings = service.LoadWorkflowSettings();

        Assert.Equal(2, settings.MaxRepairRounds);
    }

    [Fact]
    public void LoadWorkflowSettings_ReturnsDefault_WhenFileIsMalformed()
    {
        var dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), "{ not valid json");

        var service = new RuntimeConfigService(_root);
        var settings = service.LoadWorkflowSettings();

        Assert.Equal(WorkflowSettings.Default, settings);
    }

    [Fact]
    public void LoadAgentsConfig_ReturnsDefault_WhenNoFileExists()
    {
        var service = new RuntimeConfigService(_root);

        var agents = service.LoadAgentsConfig();

        Assert.Equal(AgentsConfig.Default, agents);
    }

    [Fact]
    public void LoadPreferences_ReturnsDefault_WhenNoFileExists()
    {
        var service = new AppPreferencesService(_root);

        Assert.Equal(AppPreferences.Default, service.Load());
        Assert.Null(service.Load().DecisionWriter);
    }

    [Fact]
    public void LoadPreferences_ReturnsDefault_WhenFileIsBroken()
    {
        var dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "preferences.json"), "{ not valid json");

        Assert.Equal(AppPreferences.Default, new AppPreferencesService(_root).Load());
    }

    [Fact]
    public void DecisionWriterSurvivesASaveAndReload()
    {
        var service = new AppPreferencesService(_root);

        service.Save(new AppPreferences(DecisionWriter: ProviderId.Codex));

        Assert.Equal(ProviderId.Codex, service.Load().DecisionWriter);
    }

    [Fact]
    public void UnknownDecisionWriter_IsIgnoredRatherThanCrashing()
    {
        var dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(
            Path.Combine(dataDir, "preferences.json"),
            """{ "hide_cli_console": true, "decision_writer": "Copilot" }""");

        // 認不得的名字（舊版留下來的、或手動亂改的）當作沒記過，而且舊版留下來的
        // hide_cli_console 也不該讓整個檔案讀不起來。
        Assert.Null(new AppPreferencesService(_root).Load().DecisionWriter);
    }

    [Fact]
    public void DefaultRoles_FollowTheCostPrinciple()
    {
        // 便宜的做大量閱讀、GPT 做最吃額度的實作、最貴的 Claude 只做規劃與最終審查。
        var roles = WorkflowSettings.Default.EffectiveRoles;

        Assert.Equal(ProviderId.Antigravity, roles.Scout[0]);
        Assert.Equal(ProviderId.Claude, roles.Planner[0]);
        Assert.Equal(ProviderId.Codex, roles.Implementer[0]);
        Assert.Equal(ProviderId.Antigravity, roles.Challenger[0]);
        Assert.Equal(ProviderId.Claude, roles.FinalReviewer[0]);
        // Gemini 最慢，而且實作會在修正迴圈裡被反覆呼叫：永遠排最後。
        Assert.Equal(ProviderId.Antigravity, roles.Implementer[^1]);
    }

    [Fact]
    public void SettingsWithoutRoles_UseTheDefaults()
    {
        var settings = RuntimeConfigService.ParseWorkflowSettings("""{ "workflow": { "max_repair_rounds": 2 } }""");

        Assert.Same(RoleOrder.Default, settings.EffectiveRoles);
    }

    [Fact]
    public void RolesCanBeWrittenWithTheNamesShownInTheApp()
    {
        var settings = RuntimeConfigService.ParseWorkflowSettings(
            """{ "workflow": { "roles": { "implementer": ["claude", "gpt", "gemini"] } } }""");

        Assert.Equal(new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Antigravity }, settings.EffectiveRoles.Implementer);
        // 沒寫到的角色維持預設。
        Assert.Equal(RoleOrder.Default.Planner, settings.EffectiveRoles.Planner);
    }

    [Fact]
    public void AnAiLeftOutOfARole_IsStillAppendedAsALastResort()
    {
        // 只寫了一家：其他兩家照預設順序補在後面，不然那家沒上線時任務會直接失敗。
        var settings = RuntimeConfigService.ParseWorkflowSettings(
            """{ "workflow": { "roles": { "planner": ["gpt"] } } }""");

        Assert.Equal(new[] { ProviderId.Codex, ProviderId.Claude, ProviderId.Antigravity }, settings.EffectiveRoles.Planner);
    }

    [Fact]
    public void UnknownAndDuplicateNames_AreIgnored()
    {
        var settings = RuntimeConfigService.ParseWorkflowSettings(
            """{ "workflow": { "roles": { "scout": ["copilot", "gemini", "agy", "claude"] } } }""");

        Assert.Equal(new[] { ProviderId.Antigravity, ProviderId.Claude, ProviderId.Codex }, settings.EffectiveRoles.Scout);
    }
}
