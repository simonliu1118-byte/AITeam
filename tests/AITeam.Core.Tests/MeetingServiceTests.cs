using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class MeetingServiceTests
{
    private static readonly ProviderId[] Three =
    {
        ProviderId.Codex, ProviderId.Claude, ProviderId.Antigravity
    };

    [Fact]
    public void EveryRoundStartsWithADifferentSpeaker()
    {
        // 第一個發言的人對整輪走向影響最大，固定同一家會讓它永遠定調。
        Assert.Equal(ProviderId.Codex, MeetingService.SpeakingOrder(Three, 1)[0]);
        Assert.Equal(ProviderId.Claude, MeetingService.SpeakingOrder(Three, 2)[0]);
        Assert.Equal(ProviderId.Antigravity, MeetingService.SpeakingOrder(Three, 3)[0]);
        Assert.Equal(ProviderId.Codex, MeetingService.SpeakingOrder(Three, 4)[0]);
    }

    [Fact]
    public void EveryoneStillSpeaksExactlyOncePerRound()
    {
        var order = MeetingService.SpeakingOrder(Three, 2);

        Assert.Equal(3, order.Count);
        Assert.Equal(Three.OrderBy(x => x), order.OrderBy(x => x));
    }

    [Fact]
    public void NoParticipants_IsNotACrash()
    {
        Assert.Empty(MeetingService.SpeakingOrder(Array.Empty<ProviderId>(), 3));
    }

    [Fact]
    public void RoundRobin_LetsSpeakersSeeEachOther()
    {
        var prompt = MeetingService.BuildPrompt(
            ProviderId.Claude,
            new MeetingSetup("要不要換資料庫", null, MeetingMode.RoundRobin, MeetingScale.Standard),
            new[] { new MeetingRemark(1, ProviderId.Codex, "我覺得不必換") },
            2,
            MeetingScale.Standard);

        Assert.Contains("我覺得不必換", prompt);
        // 看得到別人的話時，一定要明講「你不必同意」，否則會變成互相附和。
        Assert.Contains("NOT required to agree", prompt);
    }

    [Fact]
    public void Parallel_HidesTheOtherAisButKeepsWhatTheUserSaid()
    {
        var prompt = MeetingService.BuildPrompt(
            ProviderId.Claude,
            new MeetingSetup("要不要換資料庫", null, MeetingMode.Parallel, MeetingScale.Standard),
            new[]
            {
                new MeetingRemark(1, ProviderId.Codex, "我覺得不必換"),
                new MeetingRemark(1, null, "資料量其實沒那麼大")
            },
            2,
            MeetingScale.Standard);

        Assert.DoesNotContain("我覺得不必換", prompt);
        Assert.Contains("資料量其實沒那麼大", prompt);
    }

    [Fact]
    public void WithoutAProject_ThePromptSaysSoInsteadOfPretending()
    {
        var prompt = MeetingService.BuildPrompt(
            ProviderId.Codex,
            new MeetingSetup("純討論", null, MeetingMode.RoundRobin, MeetingScale.Short),
            Array.Empty<MeetingRemark>(),
            1,
            MeetingScale.Short);

        Assert.Contains("not tied to a specific project", prompt);
    }

    [Fact]
    public void WithAProject_TheRulesPointerRidesAlong()
    {
        var prompt = MeetingService.BuildPrompt(
            ProviderId.Codex,
            new MeetingSetup("討論", new ProjectEntry { Name = "CYAccounting" }, MeetingMode.RoundRobin, MeetingScale.Deep),
            Array.Empty<MeetingRemark>(),
            1,
            MeetingScale.Deep);

        Assert.Contains("Project: CYAccounting", prompt);
        Assert.Contains("AGENTS.md", prompt);
        Assert.Contains("2000", prompt);
    }

    [Theory]
    [InlineData("SHORT", MeetingScale.Short)]
    [InlineData("standard", MeetingScale.Standard)]
    [InlineData("Deep", MeetingScale.Deep)]
    public void AScaleSuggestionIsLiftedOutOfTheReply(string value, MeetingScale expected)
    {
        var (text, suggested) = MeetingService.ParseRemark($"我的看法是……\nAITeamScale: {value}");

        Assert.Equal("我的看法是……", text);
        Assert.Equal(expected, suggested);
        // 建議行是給程式看的，不應該留在顯示給人看的發言裡。
        Assert.DoesNotContain("AITeamScale", text);
    }

    [Fact]
    public void RepliesWithoutASuggestion_AreLeftAlone()
    {
        var (text, suggested) = MeetingService.ParseRemark("就照原本的方向做。");

        Assert.Equal("就照原本的方向做。", text);
        Assert.Null(suggested);
    }

    [Theory]
    [InlineData(MeetingScale.Short, 300)]
    [InlineData(MeetingScale.Standard, 800)]
    [InlineData(MeetingScale.Deep, 2000)]
    public void EachScaleHasAWordBudget(MeetingScale scale, int expected)
    {
        Assert.Equal(expected, scale.WordBudget());
    }
}
