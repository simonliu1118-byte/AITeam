using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class DescribeProjectTests
{
    [Fact]
    public void WithoutDescription_OnlyTheProjectNameIsSent()
    {
        var project = new ProjectEntry { Name = "CYAccounting" };
        var described = ChangeTaskService.DescribeProject(project);

        Assert.StartsWith("Project: CYAccounting", described);
        Assert.DoesNotContain("What this project is", described);
    }

    [Fact]
    public void EveryPrompt_PointsTheAiAtTheRepositoryRules()
    {
        var described = ChangeTaskService.DescribeProject(new ProjectEntry { Name = "CYAccounting" });

        // 只指路、不貼全文：三份規則加起來約 26 KB，貼進每一輪 prompt 太浪費。
        Assert.Contains("AGENTS.md", described);
        Assert.Contains("REPOSITORY_RULES.md", described);
        Assert.Contains("REPO_POLICY.md", described);
        Assert.Contains("PROJECT_RULES.md", described);
        Assert.True(described.Length < 800);
    }

    [Fact]
    public void WithDescription_TheDescriptionIsSentToTheAi()
    {
        var project = new ProjectEntry { Name = "CYAccounting", Description = "  記帳用的桌面小工具  " };
        var described = ChangeTaskService.DescribeProject(project);

        Assert.Contains("Project: CYAccounting", described);
        Assert.Contains("What this project is: 記帳用的桌面小工具", described);
    }
}
