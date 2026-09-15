using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public class DecisionPromptTests
{
    private static readonly MeetingSetup Setup =
        new("要不要換資料庫", null, MeetingMode.RoundRobin, MeetingScale.Standard);

    [Fact]
    public void FailedTurnsAreLeftOutOfTheTranscript()
    {
        var transcript = new[]
        {
            new MeetingRemark(1, ProviderId.Codex, "我認為應該換。"),
            new MeetingRemark(1, ProviderId.Antigravity, "（這一輪失敗：額度用完。）", Failed: true)
        };

        var prompt = MeetingService.BuildDecisionPrompt(ProviderId.Claude, Setup, transcript);

        Assert.Contains("我認為應該換。", prompt);
        // 失敗那則沒有內容，放進去只會被當成某人真的講過的話。
        Assert.DoesNotContain("額度用完", prompt);
    }

    [Fact]
    public void AllFiveSectionsAreAskedFor_InOrder()
    {
        var prompt = MeetingService.BuildDecisionPrompt(
            ProviderId.Claude, Setup, new[] { new MeetingRemark(1, ProviderId.Codex, "換。") });

        var position = -1;
        foreach (var section in MeetingService.DecisionSections)
        {
            var found = prompt.IndexOf(section, StringComparison.Ordinal);
            Assert.True(found > position, $"定案書少了「{section}」，或者順序跟使用者讀到的不一樣。");
            position = found;
        }
    }

    [Fact]
    public void EmptyTranscriptStillProducesAUsablePrompt()
    {
        var prompt = MeetingService.BuildDecisionPrompt(
            ProviderId.Claude, Setup, Array.Empty<MeetingRemark>());

        Assert.Contains("（還沒有任何發言。）", prompt);
    }
}
