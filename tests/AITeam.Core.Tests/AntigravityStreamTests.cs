using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class AntigravityStreamTests
{
    [Fact]
    public void StreamedTextDeltas_AreAssembledBackIntoOneAnswer()
    {
        // Gemini/Antigravity 把答案切成很多小碎片送出，必須依序接回來。
        var stream = string.Join("\n", new[]
        {
            """{"event":"init","init":{"cwd":"D:\\AITeam"}}""",
            """{"event":"step_update","step_update":{"step_index":1,"state":"ACTIVE","step_type":"agent_response","text_delta":"主要版本"}}""",
            """{"event":"step_update","step_update":{"step_index":1,"state":"ACTIVE","step_type":"agent_response","text_delta":"號為 "}}""",
            """{"event":"step_update","step_update":{"step_index":1,"state":"ACTIVE","step_type":"agent_response","text_delta":"1.1.0。"}}""",
            """{"event":"step_update","step_update":{"step_index":2,"state":"DONE","step_type":"tool_call","text_delta":"不該出現"}}"""
        });

        Assert.Equal("主要版本號為 1.1.0。", AntigravityStream.ExtractAnswer(stream));
    }

    [Fact]
    public void WhenThereAreNoDeltas_TheMostCompleteWholeStringIsUsed()
    {
        var stream = """{"event":"result","message":{"content":"AITeamIntent: INQUIRY\n這個專案是記帳工具。"}}""";

        Assert.Contains("這個專案是記帳工具。", AntigravityStream.ExtractAnswer(stream));
    }

    [Fact]
    public void UnparsableOutput_IsSummarised_NotDumpedWholesale()
    {
        var noise = new string('x', 5000);

        var described = AntigravityStream.DescribeUnparsableOutput(noise);

        Assert.Contains("無法從 Gemini / Antigravity 的回覆中取出可讀內容", described);
        Assert.True(described.Length < noise.Length, "原始輸出必須被截斷，不能整包丟到畫面上。");
    }

    [Fact]
    public void EmptyOutput_SaysSoPlainly()
    {
        Assert.Contains("沒有回傳任何內容", AntigravityStream.DescribeUnparsableOutput("   "));
    }
}
