using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class InquiryServiceParseTests
{
    [Fact]
    public void Parse_DetectsChangeIntent_AndStripsMarkerLine()
    {
        var raw = "AITeamIntent: CHANGE\r\n這是變更摘要。";

        var result = InquiryService.Parse(ProviderId.Codex, raw);

        Assert.Equal(RequestIntent.Change, result.Intent);
        Assert.Equal(ProviderId.Codex, result.Provider);
        Assert.Equal("這是變更摘要。", result.Answer);
    }

    [Fact]
    public void Parse_DefaultsToInquiry_WhenNoChangeMarkerPresent()
    {
        var raw = "AITeamIntent: INQUIRY\r\n這是回答內容。";

        var result = InquiryService.Parse(ProviderId.Claude, raw);

        Assert.Equal(RequestIntent.Inquiry, result.Intent);
        Assert.Equal("這是回答內容。", result.Answer);
    }

    [Fact]
    public void Parse_FallsBackToPlaceholder_WhenChangeAnswerIsEmptyAfterStrippingMarker()
    {
        var raw = "AITeamIntent: CHANGE";

        var result = InquiryService.Parse(ProviderId.Antigravity, raw);

        Assert.Equal(RequestIntent.Change, result.Intent);
        Assert.Equal("已判斷為修改任務。", result.Answer);
    }

    [Fact]
    public void Parse_TreatsMissingMarkerAsInquiry_AndKeepsFullText()
    {
        var raw = "沒有任何意圖標記的自由回答。";

        var result = InquiryService.Parse(ProviderId.Claude, raw);

        Assert.Equal(RequestIntent.Inquiry, result.Intent);
        Assert.Equal(raw, result.Answer);
    }
}
