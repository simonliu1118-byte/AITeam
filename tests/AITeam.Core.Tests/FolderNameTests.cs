using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public class FolderNameTests
{
    private static readonly string[] Siblings = { "apps", "tools" };

    [Theory]
    [InlineData("erp-query")]
    [InlineData("ERP查詢")]
    [InlineData("v2_tools")]
    public void NormalNames_AreAccepted(string name)
    {
        Assert.Null(ProjectPreflight.DescribeFolderNameProblem(name, Siblings));
    }

    [Theory]
    [InlineData("", "請輸入")]
    [InlineData("   ", "請輸入")]
    [InlineData("a/b", "一次只能建立一層")]
    [InlineData("a\\b", "一次只能建立一層")]
    [InlineData(" lead", "空白或句點")]
    [InlineData(".hidden", "空白或句點")]
    [InlineData("trail.", "空白或句點")]
    [InlineData("what?", "不能包含")]
    [InlineData("a:b", "不能包含")]
    [InlineData("CON", "保留")]
    [InlineData("nul.txt", "保留")]
    [InlineData("Tools", "同名")]
    public void BadNames_AreRejectedWithAReasonTheUserCanAct_On(string name, string expectedFragment)
    {
        var problem = ProjectPreflight.DescribeFolderNameProblem(name, Siblings);

        Assert.NotNull(problem);
        Assert.Contains(expectedFragment, problem);
    }
}
