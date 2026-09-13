using System.Text;
using System.Text.Json;

namespace AITeam.Services;

/// <summary>
/// 解析 Gemini／Antigravity 的 NDJSON 串流輸出。
///
/// 它的回答不是一次回傳一整段，而是切成很多小碎片逐一送出：
/// {"event":"step_update","step_update":{...,"step_type":"agent_response","text_delta":"主要版本"}}
/// 因此必須把這些 text_delta 依序接回來。舊作法是「收集所有字串、挑最長的一個」，
/// 永遠只會拿到殘缺片段，接著整包原始串流 JSON 被當成答案顯示在畫面上，完全無法閱讀。
/// </summary>
internal static class AntigravityStream
{
    private const int RawFallbackLimit = 1200;

    public static string ExtractAnswer(string stream)
    {
        var assembled = new StringBuilder();
        var wholeStrings = new List<string>();

        foreach (var line in stream.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                AppendAgentText(document.RootElement, assembled);
                CollectWholeStrings(document.RootElement, wholeStrings);
            }
        }

        var deltaAnswer = assembled.ToString().Trim();
        if (deltaAnswer.Length > 0) return deltaAnswer;

        // 沒有 text_delta 的情況（例如一次回傳完整內容）就退回挑最完整的單一字串。
        return wholeStrings
            .Where(x => x.Contains("AITeam", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Length)
            .FirstOrDefault()
            ?? wholeStrings.OrderByDescending(x => x.Length).FirstOrDefault()
            ?? string.Empty;
    }

    /// <summary>
    /// 解析失敗時的退路。不把整包串流 JSON 當成答案丟出去——那是使用者看到的
    /// 「完全無法閱讀」的來源——只保留開頭一段供診斷。
    /// </summary>
    public static string DescribeUnparsableOutput(string stream)
    {
        var trimmed = stream.Trim();
        if (trimmed.Length == 0) return "（Gemini / Antigravity 沒有回傳任何內容。）";

        var head = trimmed.Length <= RawFallbackLimit ? trimmed : trimmed[..RawFallbackLimit] + "…（後略）";
        return "（無法從 Gemini / Antigravity 的回覆中取出可讀內容，以下為原始輸出開頭供診斷）"
               + Environment.NewLine + head;
    }

    private static void AppendAgentText(JsonElement element, StringBuilder output)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (IsAgentResponse(element) &&
                element.TryGetProperty("text_delta", out var delta) &&
                delta.ValueKind == JsonValueKind.String)
            {
                output.Append(delta.GetString());
            }

            foreach (var property in element.EnumerateObject())
                AppendAgentText(property.Value, output);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AppendAgentText(item, output);
        }
    }

    private static bool IsAgentResponse(JsonElement element) =>
        element.TryGetProperty("step_type", out var stepType) &&
        stepType.ValueKind == JsonValueKind.String &&
        string.Equals(stepType.GetString(), "agent_response", StringComparison.OrdinalIgnoreCase);

    private static void CollectWholeStrings(JsonElement element, List<string> output)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    (property.NameEquals("content") || property.NameEquals("text") || property.NameEquals("result")))
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) output.Add(value);
                }
                else CollectWholeStrings(property.Value, output);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                CollectWholeStrings(item, output);
        }
    }
}
