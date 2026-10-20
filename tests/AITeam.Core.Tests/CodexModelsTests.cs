using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class CodexModelsTests
{
    [Fact]
    public void Catalog_ListsOnlyVisibleModels_ByPriority()
    {
        var json = """
        {"models":[
          {"slug":"gpt-b","visibility":"list","priority":2},
          {"slug":"hidden","visibility":"hide","priority":0},
          {"slug":"gpt-a","visibility":"list","priority":1},
          {"slug":"bad slug","visibility":"list","priority":3}
        ]}
        """;

        Assert.Equal(new[] { "gpt-a", "gpt-b" }, CodexModels.ParseCatalog(json));
        Assert.Empty(CodexModels.ParseCatalog("not json"));
    }

    [Theory]
    [InlineData("The 'gpt-6.1-sol' model is not supported when using Codex with a ChatGPT account.", true)]
    [InlineData("This model requires a newer version of Codex.", true)]
    [InlineData("You've hit your usage limit.", false)]
    public void UnsupportedModelErrors_AreRecognised(string text, bool expected)
    {
        Assert.Equal(expected, CodexModels.LooksLikeUnsupportedModel(text));
    }

    [Fact]
    public void RejectedModelName_IsReadFromTheError()
    {
        Assert.Equal("gpt-6.1-sol", CodexModels.RejectedModel("exit 1｜The 'gpt-6.1-sol' model is not supported when using Codex"));
        Assert.Equal("gpt-x", CodexModels.RejectedModel("Model metadata for `gpt-x` not found."));
        Assert.Null(CodexModels.RejectedModel("something else"));
    }

    [Fact]
    public void ModelArgs_OnlyWhenAModelIsRemembered()
    {
        Assert.Empty(CodexModels.ArgsForModel(null));
        Assert.Equal(new[] { "-c", "model=\"gpt-a\"" }, CodexModels.ArgsForModel("gpt-a"));
        Assert.Empty(CodexModels.ArgsForModel("bad\"slug"));
    }

    [Fact]
    public void RememberedModel_SurvivesSaveAndLoad_WithTheDecisionWriter()
    {
        var root = Path.Combine(Path.GetTempPath(), "aiteam-codex-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AppPreferencesService(root);
            service.Save(new AppPreferences(ProviderId.Claude, "gpt-a"));

            var loaded = service.Load();
            Assert.Equal("gpt-a", loaded.CodexModel);
            Assert.Equal(ProviderId.Claude, loaded.DecisionWriter);
            Assert.Equal(new[] { "-c", "model=\"gpt-a\"" }, CodexModels.Args(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(ProviderId.Codex, ProviderHealthState.AuthenticationRequired, "login")]
    [InlineData(ProviderId.Claude, ProviderHealthState.AuthenticationRequired, "auth login")]
    [InlineData(ProviderId.Codex, ProviderHealthState.Error, "update")]
    [InlineData(ProviderId.Claude, ProviderHealthState.Error, "update")]
    public void StatusCards_OfferTheRightFix(ProviderId provider, ProviderHealthState state, string expected)
    {
        var action = CliMaintenance.For(provider, state, AgentsConfig.Default);

        Assert.NotNull(action);
        Assert.Equal(expected, string.Join(" ", action!.Arguments));
    }

    [Theory]
    [InlineData(ProviderId.Antigravity, ProviderHealthState.AuthenticationRequired)]
    [InlineData(ProviderId.Codex, ProviderHealthState.Quota)]
    [InlineData(ProviderId.Claude, ProviderHealthState.Online)]
    public void NoFixButton_WhenThereIsNothingToRun(ProviderId provider, ProviderHealthState state)
    {
        Assert.Null(CliMaintenance.For(provider, state, AgentsConfig.Default));
    }
}
