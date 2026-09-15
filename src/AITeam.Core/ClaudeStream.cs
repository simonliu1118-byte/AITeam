using System.Text;
using System.Text.Json;

namespace AITeam.Services;

/// <summary>
/// Claude CLI 的 <c>--output-format stream-json</c> 輸出。它每一行是一個事件：
/// <c>system</c>（開場）、<c>assistant</c>（它說的話與工具呼叫）、<c>user</c>（工具結果）、
/// 最後一個 <c>result</c> 帶著完整答案。
/// 用 <c>text</c> 格式時整個過程是黑的，只有最後才吐答案——換成串流才看得到它在做什麼。
/// </summary>
public static class ClaudeStream
{
    /// <summary>最終答案。優先取 result 事件；沒有的話退回把 assistant 說過的文字接起來。</summary>
    public static string ExtractAnswer(string stream)
    {
        var assistantText = new StringBuilder();

        foreach (var element in Events(stream))
        {
            var type = TypeOf(element);
            if (type == "result" && element.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                var text = result.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text!.Trim();
            }

            if (type != "assistant") continue;
            foreach (var block in ContentBlocks(element))
            {
                if (TypeOf(block) != "text") continue;
                if (!block.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String) continue;

                if (assistantText.Length > 0) assistantText.Append('\n');
                assistantText.Append(value.GetString());
            }
        }

        return assistantText.ToString().Trim();
    }

    /// <summary>
    /// Claude 可能以結束代碼 0 結束卻在 result 事件裡標著 is_error——例如超過限額。
    /// 只看結束代碼會把失敗當成成功。
    /// </summary>
    public static bool IsErrorResult(string stream) =>
        Events(stream).Any(element =>
            TypeOf(element) == "result"
            && element.TryGetProperty("is_error", out var flag)
            && flag.ValueKind == JsonValueKind.True);

    /// <summary>把一個串流事件翻成一句「現在在做什麼」；沒什麼好說的就回 null。</summary>
    public static string? DescribeEvent(JsonElement element)
    {
        switch (TypeOf(element))
        {
            case "assistant":
                foreach (var block in ContentBlocks(element))
                {
                    if (TypeOf(block) != "tool_use") continue;
                    if (block.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        return $"使用工具：{name.GetString()}";
                }
                return "正在回答…";

            case "user":
                return "讀取工具結果…";

            case "system":
                return "啟動中…";

            case "result":
                return "完成";

            default:
                return null;
        }
    }

    private static IEnumerable<JsonElement> Events(string stream)
    {
        foreach (var line in (stream ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.TrimStart();
            if (text.Length == 0 || text[0] != '{') continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                continue;
            }

            // 複製一份再釋放 document，否則回傳出去的 JsonElement 會指向已釋放的記憶體。
            using (document) yield return document.RootElement.Clone();
        }
    }

    private static IEnumerable<JsonElement> ContentBlocks(JsonElement element)
    {
        if (!element.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            yield break;
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var block in content.EnumerateArray()) yield return block;
    }

    private static string? TypeOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("type", out var type)
        && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;
}
