using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ProjectPreflightTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "aiteam-preflight-" + Guid.NewGuid().ToString("N")[..8]);

    public ProjectPreflightTests() => Directory.CreateDirectory(_repo);

    [Fact]
    public void GoodProject_WithThreeAisOnline_HasNothingToReport()
    {
        File.WriteAllText(Path.Combine(_repo, "VERSION"), "1.2.3\n");
        Directory.CreateDirectory(Path.Combine(_repo, ".github", "workflows"));
        File.WriteAllText(Path.Combine(_repo, ".github", "workflows", "ci.yml"), "name: ci");

        Assert.Empty(ProjectPreflight.Inspect(Project(), 3));
    }

    [Fact]
    public void NoAiOnline_IsABlocker()
    {
        var issue = Assert.Single(ProjectPreflight.Inspect(Project(), 0), i => i.Title.Contains("沒有可用的 AI"));
        Assert.Equal(PreflightSeverity.Blocker, issue.Severity);
    }

    [Theory]
    [InlineData(1, "受限模式")]
    [InlineData(2, "降級模式")]
    public void FewerThanThreeAis_IsOnlyAWarning_BecauseTheTaskStillRuns(int online, string expectedMode)
    {
        File.WriteAllText(Path.Combine(_repo, "VERSION"), "1.2.3\n");
        Directory.CreateDirectory(Path.Combine(_repo, ".github", "workflows"));
        File.WriteAllText(Path.Combine(_repo, ".github", "workflows", "ci.yml"), "name: ci");

        var issue = Assert.Single(ProjectPreflight.Inspect(Project(), online));

        Assert.Equal(PreflightSeverity.Warning, issue.Severity);
        Assert.Contains(expectedMode, issue.Title);
    }

    [Fact]
    public void MissingRepo_IsABlocker_AndStopsFurtherChecks()
    {
        var project = new ProjectEntry { Name = "X", RepoPath = Path.Combine(_repo, "not-there") };

        var issue = Assert.Single(ProjectPreflight.Inspect(project, 3));

        Assert.Equal(PreflightSeverity.Blocker, issue.Severity);
        Assert.Contains("找不到本機 Repo", issue.Title);
    }

    [Fact]
    public void MissingVersionFile_IsAWarning_SoInquiriesAreStillAllowed()
    {
        var issues = ProjectPreflight.Inspect(Project(), 3);

        var version = Assert.Single(issues, i => i.Title.Contains("版本檔"));
        Assert.Equal(PreflightSeverity.Warning, version.Severity);
    }

    [Theory]
    [InlineData("1.2.3", null)]
    [InlineData("  0.17.1  ", null)]
    [InlineData("1.2.3-beta", "不是單純的 X.Y.Z")]
    [InlineData("v1.2.3", "不是單純的 X.Y.Z")]
    [InlineData("", "不是單純的 X.Y.Z")]
    public void VersionFileFormat_IsCheckedBeforeAnyAiWorkStarts(string content, string? expectedFragment)
    {
        File.WriteAllText(Path.Combine(_repo, "VERSION"), content);

        var problem = ProjectPreflight.DescribeVersionFileProblem(_repo);

        if (expectedFragment is null) Assert.Null(problem);
        else Assert.Contains(expectedFragment, problem);
    }

    [Fact]
    public void NoVersionFileAtAll_SaysSo()
    {
        Assert.Contains("找不到 VERSION 檔", ProjectPreflight.DescribeVersionFileProblem(_repo));
    }

    private ProjectEntry Project() => new() { Name = "X", RepoPath = _repo, ProjectPath = _repo };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_repo)) Directory.Delete(_repo, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
