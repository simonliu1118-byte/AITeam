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
        Assert.Equal("Project: CYAccounting", ChangeTaskService.DescribeProject(project));
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
