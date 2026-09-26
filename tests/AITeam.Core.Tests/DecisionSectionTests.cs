using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public class DecisionSectionTests
{
    [Fact]
    public void SplitsIntoTheFiveSections_WhenHeadingsAreOnTheirOwnLines()
    {
        var text = string.Join("\n", new[]
        {
            "要做什麼：", "換成 Postgres。", "",
            "為什麼這樣做：", "現在的 SQLite 撐不住。", "",
            "具體做法：", "先搬 schema。", "",
            "明確不做／已經排除的選項：", "不換 ORM。", "",
            "還沒決定的事：", "什麼時候切換。"
        });

        var sections = MeetingService.SplitDecision(text);

        Assert.Equal(5, sections.Count);
        Assert.Equal("要做什麼：", sections[0].Heading);
        Assert.Equal("換成 Postgres。", sections[0].Body);
        Assert.Equal("什麼時候切換。", sections[4].Body);
    }

    [Fact]
    public void SplitsEvenWhenTheHeadingAndItsContentShareOneLine()
    {
        // AI 實際上常常這樣寫，逐行比對會整份只抓到第一個標題。
        var text = "要做什麼：換成 Postgres。\n為什麼這樣做：現在的 SQLite 撐不住。";

        var sections = MeetingService.SplitDecision(text);

        Assert.Equal(2, sections.Count);
        Assert.Equal("換成 Postgres。", sections[0].Body);
        Assert.Equal("現在的 SQLite 撐不住。", sections[1].Body);
    }

    [Fact]
    public void MarkdownDecorationDoesNotCreateJunkSections()
    {
        var text = "## **要做什麼：**\n換成 Postgres。\n\n## **還沒決定的事：**\n什麼時候切換。";

        var sections = MeetingService.SplitDecision(text);

        Assert.Equal(2, sections.Count);
        Assert.Equal("換成 Postgres。", sections[0].Body);
        Assert.Equal("什麼時候切換。", sections[1].Body);
    }

    [Fact]
    public void TextWithNoHeadings_BecomesOneSection_AndKeepsEverything()
    {
        var text = "● GPT：換。\n● Claude：不換。";

        var sections = MeetingService.SplitDecision(text);

        var only = Assert.Single(sections);
        Assert.Equal("", only.Heading);
        Assert.Contains("● GPT：換。", only.Body);
        Assert.Contains("● Claude：不換。", only.Body);
    }

    [Fact]
    public void EditingOneSectionSurvivesTheRoundTrip()
    {
        var sections = MeetingService
            .SplitDecision("要做什麼：換成 Postgres。\n還沒決定的事：什麼時候切換。")
            .Select(s => s.Heading == "要做什麼：" ? s with { Body = "維持現狀。" } : s)
            .ToList();

        var rebuilt = MeetingService.JoinDecision(sections);

        Assert.Contains("要做什麼：", rebuilt);
        Assert.Contains("維持現狀。", rebuilt);
        Assert.DoesNotContain("Postgres", rebuilt);
        Assert.Equal(2, MeetingService.SplitDecision(rebuilt).Count);
    }
}
