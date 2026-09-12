using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ChangeTaskServiceLogicTests
{
    [Theory]
    [InlineData("AITeamRisk: HIGH\n本次為高風險變更。", ChangeRisk.High)]
    [InlineData("AITeamRisk: LOW\n本次為低風險變更。", ChangeRisk.Low)]
    [InlineData("AITeamRisk: NORMAL\n本次為一般風險變更。", ChangeRisk.Normal)]
    [InlineData("計畫內容裡沒有風險標記", ChangeRisk.Normal)]
    public void ParseRisk_ReadsDeclaredRiskOrDefaultsToNormal(string plan, ChangeRisk expected)
    {
        Assert.Equal(expected, ChangeTaskService.ParseRisk(plan));
    }

    [Theory]
    [InlineData("AITeamVersionBump: MAJOR", VersionBump.Major)]
    [InlineData("AITeamVersionBump: MINOR", VersionBump.Minor)]
    [InlineData("AITeamVersionBump: PATCH", VersionBump.Patch)]
    [InlineData("AITeamVersionBump: NONE", VersionBump.None)]
    [InlineData("計畫內容裡沒有版號標記", VersionBump.None)]
    public void ParseVersionBump_ReadsDeclaredBumpOrDefaultsToNone(string plan, VersionBump expected)
    {
        Assert.Equal(expected, ChangeTaskService.ParseVersionBump(plan));
    }

    [Theory]
    [InlineData("AITeamReview: PASS\n看起來沒問題。", true)]
    [InlineData("AITeamReview: REPAIR\n還需要修正。", false)]
    [InlineData("AITeamReview: PASS\n但內文又提到 AITeamReview: REPAIR", false)]
    [InlineData("沒有任何審查標記", false)]
    public void ReviewPassed_RequiresPassWithoutAnyRepairMention(string review, bool expected)
    {
        Assert.Equal(expected, ChangeTaskService.ReviewPassed(review));
    }
}

public sealed class ChangeTaskServiceRoleSelectionTests
{
    private static readonly ProviderId[] FinalReviewerPreferences =
        { ProviderId.Codex, ProviderId.Antigravity, ProviderId.Claude };

    [Theory]
    [InlineData(ProviderId.Codex, ProviderId.Claude)]
    [InlineData(ProviderId.Codex, ProviderId.Antigravity)]
    [InlineData(ProviderId.Claude, ProviderId.Antigravity)]
    public void TryPickDifferentFromAny_FailsWhenOnlyOneNonExcludedProviderExists(
        ProviderId implementer, ProviderId challenger)
    {
        var available = new[] { implementer, challenger };

        var found = ChangeTaskService.TryPickDifferentFromAny(
            available,
            new[] { implementer, challenger },
            FinalReviewerPreferences,
            out _);

        Assert.False(found);
    }

    [Fact]
    public void TryPickDifferentFromAny_FindsThirdProvider_WhenAllThreeOnline()
    {
        var available = new[] { ProviderId.Codex, ProviderId.Claude, ProviderId.Antigravity };

        var found = ChangeTaskService.TryPickDifferentFromAny(
            available,
            new[] { ProviderId.Claude, ProviderId.Antigravity },
            FinalReviewerPreferences,
            out var result);

        Assert.True(found);
        Assert.Equal(ProviderId.Codex, result);
    }

    [Fact]
    public void TryPickDifferentFromAny_RespectsPreferenceOrder()
    {
        var available = new[] { ProviderId.Codex, ProviderId.Claude, ProviderId.Antigravity };

        var found = ChangeTaskService.TryPickDifferentFromAny(
            available,
            Array.Empty<ProviderId>(),
            FinalReviewerPreferences,
            out var result);

        Assert.True(found);
        Assert.Equal(ProviderId.Codex, result);
    }
}
