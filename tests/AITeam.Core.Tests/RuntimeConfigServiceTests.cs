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
        Assert.False(service.Load().HideCliConsole);
    }

    [Fact]
    public void PreferencesSurviveASaveAndReload()
    {
        var service = new AppPreferencesService(_root);

        service.Save(new AppPreferences(HideCliConsole: true));

        Assert.True(service.Load().HideCliConsole);
    }

    [Fact]
    public void LoadPreferences_ReturnsDefault_WhenFileIsBroken()
    {
        var dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "preferences.json"), "{ not valid json");

        Assert.Equal(AppPreferences.Default, new AppPreferencesService(_root).Load());
    }
}
