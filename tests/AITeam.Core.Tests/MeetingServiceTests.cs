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
    public void ThePromptStopsTheAiFromActingLikeACodingAgent()
    {
        var prompt = MeetingService.BuildPrompt(
            ProviderId.Claude,
            new MeetingSetup("議題", null, MeetingMode.RoundRobin, MeetingScale.Standard),
            Array.Empty<MeetingRemark>(),
            1,
            MeetingScale.Standard);

        // 實測 Claude 回過「my turn 1 contribution already reflects this / no action needed」——
        // 它把會議當成工作階段在回報進度，而不是在發言。
        Assert.Contains("This is a discussion, not a task", prompt);
        Assert.Contains("already posted", prompt);
        Assert.Contains("no action needed", prompt);
        // 也回過整段英文，所以語言要求要講兩次、而且要講在最前面。
        Assert.Contains("Write in Traditional Chinese", prompt);
    }

    [Fact]
    public void FailedOrSkippedTurns_AreKeptOutOfTheTranscript()
    {
        var prompt = MeetingService.BuildPrompt(
            ProviderId.Claude,
            new MeetingSetup("議題", null, MeetingMode.RoundRobin, MeetingScale.Standard),
            new[]
            {
                new MeetingRemark(1, ProviderId.Codex, "（這一輪失敗，沒有發言。）", Failed: true),
                new MeetingRemark(1, ProviderId.Antigravity, "我認為先做 A")
            },
            2,
            MeetingScale.Standard);

        Assert.DoesNotContain("這一輪失敗", prompt);
        Assert.Contains("我認為先做 A", prompt);
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

    [Fact]
    public void TheSummaryIsEveryonesFinalPosition_NotJustWhoeverSpokeLast()
    {
        var transcript = new[]
        {
            new MeetingRemark(1, ProviderId.Codex, "我主張改用 PostgreSQL"),
            new MeetingRemark(1, ProviderId.Claude, "我主張維持 SQLite"),
            new MeetingRemark(2, ProviderId.Codex, "看完大家的說法，我改口支持維持 SQLite"),
            new MeetingRemark(2, null, "那就先不換"),
            new MeetingRemark(2, ProviderId.Antigravity, "（這一輪失敗，沒有發言。）", Failed: true)
        };

        var summary = MeetingService.Summarise(transcript, rounds: 2, calls: 5);

        Assert.Contains("共 2 輪 · 呼叫 5 次", summary);
        // 每個 AI 取它最後一次的立場，不是只取最後一個講話的人。
        Assert.Contains("我改口支持維持 SQLite", summary);
        Assert.Contains("我主張維持 SQLite", summary);
        // 第一輪那個已經被自己後來的發言取代了。
        Assert.DoesNotContain("我主張改用 PostgreSQL", summary);
        // 使用者的發言與失敗的那一則都不算「立場」。
        Assert.DoesNotContain("那就先不換", summary);
        Assert.DoesNotContain("這一輪失敗", summary);
    }

    [Fact]
    public void AMeetingWhereNobodySpoke_SaysSo()
    {
        Assert.Equal("（沒有任何 AI 發言。）", MeetingService.Summarise(Array.Empty<MeetingRemark>(), 0, 0));
    }

    [Theory]
    [InlineData(MeetingScale.Short, 5)]
    [InlineData(MeetingScale.Standard, 10)]
    [InlineData(MeetingScale.Deep, 15)]
    public void HardTimeoutFollowsTheScale_SoAShortRoundCannotRunForHalfAnHour(MeetingScale scale, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), scale.HardTimeout());
    }

    [Fact]
    public void ShortRounds_TellTheAiNotToSurveyTheWholeRepository()
    {
        var prompt = MeetingService.BuildPrompt(
            ProviderId.Antigravity,
            new MeetingSetup("小問題", new ProjectEntry { Name = "CYEnvelope" }, MeetingMode.RoundRobin, MeetingScale.Short),
            Array.Empty<MeetingRemark>(),
            1,
            MeetingScale.Short);

        // 不講清楚讀多少，它會把整個 repo 掃一遍，三百字的題目也能花上十分鐘。
        Assert.Contains("Do not survey the repository", prompt);
    }

    [Fact]
    public void DeepRounds_AreAllowedToInvestigateProperly()
    {
        var prompt = MeetingService.BuildPrompt(
            ProviderId.Antigravity,
            new MeetingSetup("大問題", new ProjectEntry { Name = "CYEnvelope" }, MeetingMode.RoundRobin, MeetingScale.Deep),
            Array.Empty<MeetingRemark>(),
            1,
            MeetingScale.Deep);

        Assert.Contains("investigate thoroughly", prompt);
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
