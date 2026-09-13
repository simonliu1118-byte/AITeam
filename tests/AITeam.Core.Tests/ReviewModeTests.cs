using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ReviewModeTests
{
    [Theory]
    [InlineData(3, ReviewMode.Full)]
    [InlineData(4, ReviewMode.Full)]
    [InlineData(2, ReviewMode.Degraded)]
    [InlineData(1, ReviewMode.Restricted)]
    [InlineData(0, ReviewMode.Restricted)]
    public void ModeFollowsHowManyAisCanActuallyHelp(int online, ReviewMode expected)
    {
        Assert.Equal(expected, ReviewModeExtensions.ForProviderCount(online));
    }

    [Theory]
    [InlineData(ReviewMode.Full, "完整模式")]
    [InlineData(ReviewMode.Degraded, "降級模式")]
    [InlineData(ReviewMode.Restricted, "受限模式")]
    public void EveryModeHasAName(ReviewMode mode, string expected)
    {
        Assert.Equal(expected, mode.ToFriendlyName());
    }

    [Fact]
    public void RestrictedModeSaysOutLoudThatAiTeamWillNotMergeByItself()
    {
        // 受限模式等於沒有獨立審查，這件事一定要在畫面與 PR 上講清楚。
        Assert.Contains("不會自動合併", ReviewMode.Restricted.Describe());
    }

    [Fact]
    public void EveryModeExplainsItself()
    {
        foreach (var mode in Enum.GetValues<ReviewMode>())
            Assert.False(string.IsNullOrWhiteSpace(mode.Describe()));
    }
}
