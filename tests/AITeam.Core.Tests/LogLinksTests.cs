using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class LogLinksTests
{
    [Fact]
    public void MarkdownReference_IsFoldedDownToItsFileName()
    {
        var text = "根據 [PROJECT_STATUS.md](file:///D:/AITeam/tasks/inquiry-6b21/apps/CYWatermark/PROJECT_STATUS.md) 的結果";

        var folded = LogLinks.Fold(text);

        Assert.Equal("根據 PROJECT_STATUS.md 的結果", folded.Text);
        var link = Assert.Single(folded.Links);
        Assert.Equal(@"D:\AITeam\tasks\inquiry-6b21\apps\CYWatermark\PROJECT_STATUS.md", link.Target);
        // 折疊後的位置要對得上，畫面才能在正確的字上顯示 tooltip。
        Assert.Equal("PROJECT_STATUS.md", folded.Text.Substring(link.Start, link.Length));
    }

    [Fact]
    public void EveryReferenceOnTheLine_IsFolded()
    {
        var text = "[A.md](file:///D:/x/A.md) 與 [B.md](file:///D:/x/B.md)";

        var folded = LogLinks.Fold(text);

        Assert.Equal("A.md 與 B.md", folded.Text);
        Assert.Equal(2, folded.Links.Count);
        Assert.Equal("A.md", folded.Text.Substring(folded.Links[0].Start, folded.Links[0].Length));
        Assert.Equal("B.md", folded.Text.Substring(folded.Links[1].Start, folded.Links[1].Length));
    }

    [Fact]
    public void PlainText_IsLeftAlone()
    {
        const string text = "沒有任何參照的一般訊息 [不是連結] (也不是)";

        var folded = LogLinks.Fold(text);

        Assert.Equal(text, folded.Text);
        Assert.Empty(folded.Links);
    }

    [Fact]
    public void NonFileLinks_KeepTheirOriginalAddress()
    {
        var folded = LogLinks.Fold("看 [PR #36](https://github.com/o/r/pull/36) 的結果");

        Assert.Equal("看 PR #36 的結果", folded.Text);
        Assert.Equal("https://github.com/o/r/pull/36", Assert.Single(folded.Links).Target);
    }

    [Fact]
    public void EncodedPaths_AreShownAsReadablePaths()
    {
        var folded = LogLinks.Fold("[a b.md](file:///D:/My%20Projects/a%20b.md)");

        Assert.Equal(@"D:\My Projects\a b.md", Assert.Single(folded.Links).Target);
    }
}
