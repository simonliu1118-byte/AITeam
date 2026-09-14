using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class TaskKindTests
{
    [Theory]
    [InlineData(TaskKind.Inquiry, "查詢")]
    [InlineData(TaskKind.Change, "修改")]
    [InlineData(TaskKind.Meeting, "會議")]
    public void EveryKindHasAName_ForTheHistoryList(TaskKind kind, string expected)
    {
        Assert.Equal(expected, kind.ToFriendlyName());
    }

    [Fact]
    public void MeetingsDoNotCrashTheStageStrip_EvenThoughTheyHaveNoStages()
    {
        Assert.NotEmpty(TaskStages.For(TaskKind.Meeting));
    }

    [Fact]
    public void ChangeTasksKeepTheirSevenStages()
    {
        Assert.Equal(7, TaskStages.For(TaskKind.Change).Count);
    }

    [Fact]
    public void NoDecisionMeansDoNotMerge()
    {
        // 不合併可以反悔，合併不行；預設一定要是保守的那一邊。
        Assert.Equal(MergeDecision.Skip, TaskInteraction.None.AskMergeApproval(
            new MergeApprovalPrompt("p", 1, "u", "1.0.0", "High", "受限模式", "", "", ""),
            CancellationToken.None).Result);
    }
}
