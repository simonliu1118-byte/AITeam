using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public class MeetingConclusionTests
{
    [Fact]
    public void UnconvergedSummary_TellsThePlannerItIsNotDecided()
    {
        var conclusion = new MeetingConclusion("要不要換資料庫", "AITeam", "● GPT：换 Postgres");

        var background = conclusion.ComposeBackground();

        // 這是整件事的重點：沒收斂過的東西不可以被介紹成「已經決定好的事」，
        // 否則接手的 AI 會直接照著一個根本沒定案的立場去改程式，而且不會回頭問。
        Assert.DoesNotContain("已經由使用者確認", background);
        Assert.DoesNotContain("不要重新討論", background);
        Assert.Contains("沒有收斂出定案", background);
        Assert.Contains("● GPT：换 Postgres", background);
    }

    [Fact]
    public void Decision_SaysWhoWroteItAndRingFencesTheOpenQuestions()
    {
        var conclusion = new MeetingConclusion(
            "要不要換資料庫", "AITeam", "要做什麼：換成 Postgres",
            MeetingConclusionKind.Decision, ProviderId.Claude);

        var background = conclusion.ComposeBackground();

        Assert.Contains("定案書", background);
        Assert.Contains(ProviderId.Claude.ToFriendlyName(), background);
        // 「還沒決定的事」那一段就算寫在定案書裡，也不能被當成定案。
        Assert.Contains("還沒決定的事", background);
        Assert.Contains("要做什麼：換成 Postgres", background);
    }

    [Fact]
    public void DefaultKind_IsUnconverged_SoNothingIsAccidentallyPresentedAsDecided()
    {
        var conclusion = new MeetingConclusion("題目", null, "內容");

        Assert.Equal(MeetingConclusionKind.UnconvergedSummary, conclusion.Kind);
    }
}
