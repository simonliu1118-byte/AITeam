using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class ClaudeStreamTests
{
    private const string Session = """
{"type":"system","subtype":"init","session_id":"abc"}
{"type":"assistant","message":{"content":[{"type":"text","text":"先看一下檔案。"},{"type":"tool_use","name":"Read","input":{}}]}}
{"type":"user","message":{"content":[{"type":"tool_result","content":"..."}]}}
{"type":"assistant","message":{"content":[{"type":"text","text":"我的結論是維持現狀。"}]}}
{"type":"result","subtype":"success","is_error":false,"result":"我的結論是維持現狀。"}
""";

    [Fact]
    public void TheFinalAnswerComesFromTheResultEvent()
    {
        Assert.Equal("我的結論是維持現狀。", ClaudeStream.ExtractAnswer(Session));
    }

    [Fact]
    public void WithoutAResultEvent_TheAssistantTextIsStitchedTogether()
    {
        var truncated = string.Join("\n", Session.Split('\n').Where(l => !l.Contains("\"result\"")));

        var answer = ClaudeStream.ExtractAnswer(truncated);

        // 被中途砍掉時，至少要拿得回它已經講出來的部分，而不是整段丟掉。
        Assert.Contains("先看一下檔案。", answer);
        Assert.Contains("我的結論是維持現狀。", answer);
    }

    [Fact]
    public void AnErrorResultIsDetected_EvenWhenTheProcessExitsCleanly()
    {
        // 超過限額時 Claude 會以結束代碼 0 結束，只在 result 事件裡標 is_error。
        var stream = """{"type":"result","subtype":"error","is_error":true,"result":"usage limit reached"}""";

        Assert.True(ClaudeStream.IsErrorResult(stream));
        Assert.False(ClaudeStream.IsErrorResult(Session));
    }

    [Fact]
    public void GarbageLinesAreSkipped_NotFatal()
    {
        var stream = "not json\n" + Session + "\nalso not json";

        Assert.Equal("我的結論是維持現狀。", ClaudeStream.ExtractAnswer(stream));
    }

    [Theory]
    [InlineData("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Grep"}]}}""", "使用工具：Grep")]
    [InlineData("""{"type":"assistant","message":{"content":[{"type":"text","text":"嗯"}]}}""", "正在回答…")]
    [InlineData("""{"type":"user","message":{"content":[]}}""", "讀取工具結果…")]
    [InlineData("""{"type":"result","is_error":false}""", "完成")]
    public void ActivityIsReadable_EvenThoughClaudeNestsToolNamesTwoLevelsDeep(string line, string expected)
    {
        // 工具名稱包在 message.content[] 的 tool_use 區塊裡，通用的找法抓不到。
        Assert.Equal(expected, ProviderActivity.Describe(ProviderId.Claude, line));
    }
}
