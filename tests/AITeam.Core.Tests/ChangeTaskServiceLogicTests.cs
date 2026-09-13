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
    [InlineData("AITeamReview: PASS\nAITeamVersionBumpConfirm: MAJOR\n理由。", VersionBump.Major)]
    [InlineData("AITeamReview: PASS\nAITeamVersionBumpConfirm: MINOR\n理由。", VersionBump.Minor)]
    [InlineData("AITeamReview: PASS\nAITeamVersionBumpConfirm: PATCH\n理由。", VersionBump.Patch)]
    [InlineData("AITeamReview: PASS\n沒有重新確認版號。", VersionBump.None)]
    public void ParseVersionBumpConfirm_ReadsReviewerConfirmationOrDefaultsToNone(string review, VersionBump expected)
    {
        Assert.Equal(expected, ChangeTaskService.ParseVersionBumpConfirm(review));
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

    [Fact]
    public void ParseRepairDispute_ExtractsReasonWhenRepairerDisputes()
    {
        var repairResult = "AITeamRepairStance: DISPUTE\r\n這個發現其實是誤判，因為輸入已經在上一層驗證過。";

        var reason = ChangeTaskService.ParseRepairDispute(repairResult);

        Assert.Equal("這個發現其實是誤判，因為輸入已經在上一層驗證過。", reason);
    }

    [Fact]
    public void ParseRepairDispute_FallsBackToPlaceholder_WhenReasonIsEmpty()
    {
        var reason = ChangeTaskService.ParseRepairDispute("AITeamRepairStance: DISPUTE");

        Assert.Equal("（未提供理由）", reason);
    }

    [Fact]
    public void ParseRepairDispute_ReturnsNull_WhenRepairerMadeNoDispute()
    {
        var reason = ChangeTaskService.ParseRepairDispute("已修正相關問題並補上測試。");

        Assert.Null(reason);
    }

    [Theory]
    [InlineData("AITeamPlanStatus: NEEDS_INPUT\n這裡有兩種做法，你想要哪一種？", true)]
    [InlineData("AITeamPlanStatus: READY\nAITeamRisk: NORMAL\nAITeamVersionBump: MINOR\n計畫內容。", false)]
    [InlineData("沒有任何狀態標記", false)]
    public void IsPlanGateNeedsInput_ReadsDeclaredStatus(string reply, bool expected)
    {
        Assert.Equal(expected, ChangeTaskService.IsPlanGateNeedsInput(reply));
    }

    [Fact]
    public void ExtractPlanGateBody_StripsOnlyTheStatusLine()
    {
        var reply = "AITeamPlanStatus: READY\r\nAITeamRisk: NORMAL\r\nAITeamVersionBump: MINOR\r\n計畫內容。";

        var body = ChangeTaskService.ExtractPlanGateBody(reply);

        Assert.Equal("AITeamRisk: NORMAL" + Environment.NewLine + "AITeamVersionBump: MINOR" + Environment.NewLine + "計畫內容。", body);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/pull/42\n", 42)]
    [InlineData("Some warning line\nhttps://github.com/owner/repo/pull/7", 7)]
    public void ParsePrNumberFromUrl_ExtractsNumberFromGhOutput(string output, int expected)
    {
        Assert.Equal(expected, ChangeTaskService.ParsePrNumberFromUrl(output));
    }

    [Fact]
    public void ParsePrNumberFromUrl_ThrowsWhenNoPrUrlPresent()
    {
        Assert.Throws<InvalidOperationException>(() => ChangeTaskService.ParsePrNumberFromUrl("no url here"));
    }

    [Fact]
    public void ParsePrState_ReadsStateField()
    {
        Assert.Equal("MERGED", ChangeTaskService.ParsePrState("""{ "state": "MERGED" }"""));
    }

    [Fact]
    public void ParsePrState_ReturnsNull_WhenJsonInvalid()
    {
        Assert.Null(ChangeTaskService.ParsePrState("not json"));
    }

    [Theory]
    [InlineData("no checks reported on the 'main' branch", true)]
    [InlineData("No Checks Reported", true)]
    [InlineData("✓ build\tpass\t1m2s", false)]
    public void NoChecksReported_DetectsGhsNoChecksMessage(string output, bool expected)
    {
        Assert.Equal(expected, ChangeTaskService.NoChecksReported(output));
    }
}

public sealed class ChangeTaskServiceCiDetectionTests : IDisposable
{
    private readonly string _root;

    public ChangeTaskServiceCiDetectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aiteam-ci-detect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void HasGitHubActionsWorkflows_FalseWhenDirectoryMissing()
    {
        Assert.False(ChangeTaskService.HasGitHubActionsWorkflows(_root));
    }

    [Fact]
    public void HasGitHubActionsWorkflows_FalseWhenDirectoryEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".github", "workflows"));
        Assert.False(ChangeTaskService.HasGitHubActionsWorkflows(_root));
    }

    [Fact]
    public void HasGitHubActionsWorkflows_TrueWhenAnyWorkflowFileExists()
    {
        var dir = Path.Combine(_root, ".github", "workflows");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ci.yml"), "name: ci");
        Assert.True(ChangeTaskService.HasGitHubActionsWorkflows(_root));
    }

    [Fact]
    public void DetectTechStackHint_ReturnsUnknown_WhenNoRecognizedFiles()
    {
        Assert.Equal("unknown", ChangeTaskService.DetectTechStackHint(_root));
    }

    [Theory]
    [InlineData("package.json", "node")]
    [InlineData("requirements.txt", "python")]
    [InlineData("pyproject.toml", "python")]
    [InlineData("go.mod", "go")]
    [InlineData("Cargo.toml", "rust")]
    public void DetectTechStackHint_RecognizesCommonManifestFiles(string fileName, string expected)
    {
        File.WriteAllText(Path.Combine(_root, fileName), "");
        Assert.Equal(expected, ChangeTaskService.DetectTechStackHint(_root));
    }

    [Fact]
    public void DetectTechStackHint_RecognizesDotnetProject()
    {
        File.WriteAllText(Path.Combine(_root, "App.csproj"), "<Project />");
        Assert.Equal("dotnet", ChangeTaskService.DetectTechStackHint(_root));
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
