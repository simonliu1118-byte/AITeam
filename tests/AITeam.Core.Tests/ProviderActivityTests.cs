using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ProviderActivityTests
{
    [Fact]
    public void CodexPlainProgress_IsShownAsIs()
    {
        Assert.Equal("Reading src/Program.cs", ProviderActivity.Describe(ProviderId.Codex, "  Reading src/Program.cs  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--------------------")]
    [InlineData("====")]
    public void EmptyAndDecorativeLines_AreSkipped(string line)
    {
        Assert.Null(ProviderActivity.Describe(ProviderId.Codex, line));
    }

    [Theory]
    // Codex 在 Windows 上用 PowerShell 做事，找不到檔案時會吐出這一串。對它來說只是
    // 換個方法再試，但顯示在「現在在做什麼」會讓人以為整件事失敗了——它後來照樣正常發言。
    [InlineData("+ FullyQualifiedErrorId : PathNotFound,Microsoft.PowerShell.Commands.GetContentCommand")]
    [InlineData("+ CategoryInfo          : ObjectNotFound")]
    [InlineData("At line:1 char:1")]
    [InlineData("~~~~~~~~~~~~~~~")]
    public void ErrorStackNoiseFromTheAisOwnTooling_IsNotShownAsActivity(string line)
    {
        Assert.Null(ProviderActivity.Describe(ProviderId.Codex, line));
    }

    [Fact]
    public void RealProgressIsStillShown()
    {
        Assert.Equal("Reading apps/CYEnvelope/paintEnvelope.py",
            ProviderActivity.Describe(ProviderId.Codex, "Reading apps/CYEnvelope/paintEnvelope.py"));
    }

    [Fact]
    public void ToolNameIsThePreferredSignal_BecauseItSaysWhatTheAiIsActuallyDoing()
    {
        var line = """{"type":"step_update","step_type":"tool_call","tool_name":"read_file"}""";

        Assert.Equal("使用工具：read_file", ProviderActivity.Describe(ProviderId.Antigravity, line));
    }

    [Fact]
    public void ToolNameIsFound_EvenWhenNestedOneLevelDown()
    {
        var line = """{"type":"step_update","payload":{"tool_name":"run_tests"}}""";

        Assert.Equal("使用工具：run_tests", ProviderActivity.Describe(ProviderId.Antigravity, line));
    }

    [Fact]
    public void WithoutAToolName_TheStepTypeIsUsed()
    {
        var line = """{"type":"step_update","step_type":"agent_response"}""";

        Assert.Equal("階段：agent response", ProviderActivity.Describe(ProviderId.Antigravity, line));
    }

    [Fact]
    public void TextDeltaNoise_IsNotWorthShowing()
    {
        // 回答本文是一小片一小片送來的，每一片都刷一次畫面沒有任何意義。
        var line = """{"type":"text_delta","text":"好"}""";

        Assert.Null(ProviderActivity.Describe(ProviderId.Antigravity, line));
    }

    [Fact]
    public void BrokenJsonFromAJsonProvider_IsSkippedRatherThanThrowing()
    {
        Assert.Null(ProviderActivity.Describe(ProviderId.Antigravity, "{ this is not json"));
    }

    [Fact]
    public void VeryLongLines_AreShortenedSoTheyFitOnOneLine()
    {
        var described = ProviderActivity.Describe(ProviderId.Codex, new string('x', 400));

        Assert.NotNull(described);
        Assert.True(described!.Length <= 121);
        Assert.EndsWith("…", described);
    }
}
