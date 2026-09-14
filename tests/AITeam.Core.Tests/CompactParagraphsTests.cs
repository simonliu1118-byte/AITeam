using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class CompactParagraphsTests
{
    [Fact]
    public void BlankLinesBetweenParagraphs_AreRemoved()
    {
        // AI 習慣用空行分段，一輪三個人疊起來就變成一大片空白，光第一輪就要一直上下捲。
        var compact = TextSummary.CompactParagraphs("第一段。\r\n\r\n第二段。\r\n\r\n\r\n第三段。");

        Assert.Equal($"第一段。{Environment.NewLine}第二段。{Environment.NewLine}第三段。", compact);
    }

    [Fact]
    public void ParagraphsThemselvesAreKept_JustNotThePaddingBetweenThem()
    {
        Assert.Equal($"一{Environment.NewLine}二{Environment.NewLine}三", TextSummary.CompactParagraphs("一\n二\n三"));
    }

    [Fact]
    public void TrailingSpacesGoToo()
    {
        Assert.Equal("內容", TextSummary.CompactParagraphs("內容   \n   \n"));
    }

    [Fact]
    public void EmptyTextStaysEmpty()
    {
        Assert.Equal("", TextSummary.CompactParagraphs("   \r\n  \r\n "));
    }
}
