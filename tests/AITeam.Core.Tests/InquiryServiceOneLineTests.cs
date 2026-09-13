using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class InquiryServiceOneLineTests
{
    [Fact]
    public void MultiLineErrors_AreFlattenedForTheLog()
    {
        var message = "Codex failed\r\n  reason: rate limited\n\n  retry later";

        Assert.Equal("Codex failed reason: rate limited retry later", InquiryService.OneLine(message, 300));
    }

    [Fact]
    public void LongErrors_AreTruncatedSoOneLineCannotFloodTheLog()
    {
        var flattened = InquiryService.OneLine(new string('x', 900), 300);

        Assert.Equal(301, flattened.Length); // 300 個字 + 省略號
        Assert.EndsWith("…", flattened);
    }

    [Fact]
    public void SilentFailures_StillSaySomething()
    {
        Assert.Equal("（沒有錯誤訊息）", InquiryService.OneLine("   \r\n  ", 300));
    }
}
