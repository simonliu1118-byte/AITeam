using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class TaskStagesTests
{
    [Fact]
    public void ChangeTask_HasTheSevenPipelineStagesInOrder()
    {
        Assert.Equal(
            new[]
            {
                TaskStage.Prepare,
                TaskStage.Scout,
                TaskStage.Plan,
                TaskStage.Implement,
                TaskStage.Review,
                TaskStage.Verify,
                TaskStage.Merge
            },
            TaskStages.For(TaskKind.Change));
    }

    [Fact]
    public void Inquiry_OnlyPreparesAndQueries()
    {
        Assert.Equal(new[] { TaskStage.Prepare, TaskStage.Inquire }, TaskStages.For(TaskKind.Inquiry));
    }

    [Theory]
    [InlineData(TaskStage.Prepare, "準備")]
    [InlineData(TaskStage.Scout, "調查")]
    [InlineData(TaskStage.Plan, "規劃")]
    [InlineData(TaskStage.Implement, "實作")]
    [InlineData(TaskStage.Review, "審查")]
    [InlineData(TaskStage.Verify, "驗證")]
    [InlineData(TaskStage.Merge, "合併")]
    [InlineData(TaskStage.Inquire, "查詢")]
    public void EveryStageHasAChineseDisplayName(TaskStage stage, string expected)
    {
        Assert.Equal(expected, TaskStages.DisplayName(stage));
    }
}
