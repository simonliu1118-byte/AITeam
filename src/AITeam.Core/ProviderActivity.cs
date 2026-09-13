using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

/// <summary>
/// 把 CLI 一行一行吐出來的原始輸出，翻成一句「它現在在做什麼」給畫面用。
/// 三家的輸出格式不一樣：Codex 是給人看的純文字進度，Gemini／Antigravity 是 NDJSON 事件，
/// Claude 目前用 text 輸出格式、過程中不會印任何東西（只有最後的答案）。
/// 翻不出有意義的內容就回 null，代表這一行不用顯示。
/// </summary>
public static class ProviderActivity
{
    private const int MaxLength = 120;

    public static string? Describe(ProviderId provider, string line) => provider switch
    {
        ProviderId.Antigravity => DescribeJsonEvent(line),
        ProviderId.Claude => DescribeJsonEvent(line) ?? DescribePlainLine(line),
        _ => DescribePlainLine(line)
    };

    private static string? DescribePlainLine(string line)
    {
        var text = line.Trim();
        if (text.Length == 0) return null;

        // 純裝飾用的分隔線、進度條殘骸之類的，不值得佔畫面。
        if (text.All(c => c is '-' or '=' or '_' or '*' or '.' or '─' or '━')) return null;
        if (text.StartsWith("[2K", StringComparison.Ordinal)) return null;

        return Shorten(text);
    }

    private static string? DescribeJsonEvent(string line)
    {
        var text = line.TrimStart();
        if (text.Length == 0 || text[0] != '{') return null;

        try
        {
            using var document = JsonDocument.Parse(text);
            return DescribeJsonEvent(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? DescribeJsonEvent(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        // 有工具名稱就最有用：「正在讀檔」「正在跑測試」這類資訊都在這裡。
        var tool = FindString(element, "tool_name", "toolName", "name");
        if (!string.IsNullOrWhiteSpace(tool) && !LooksLikeNoise(tool))
            return Shorten($"使用工具：{tool}");

        var step = FindString(element, "step_type", "stepType");
        if (!string.IsNullOrWhiteSpace(step))
            return Shorten($"階段：{step.Replace('_', ' ')}");

        var type = FindString(element, "type", "event", "subtype");
        return string.IsNullOrWhiteSpace(type) || LooksLikeNoise(type) ? null : Shorten($"事件：{type}");
    }

    /// <summary>在事件物件裡找第一個有值的欄位，含一層巢狀（各家把欄位放的深度不一樣）。</summary>
    private static string? FindString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var direct) && direct.ValueKind == JsonValueKind.String)
                return direct.GetString();
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var name in names)
            {
                if (property.Value.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.String)
                    return nested.GetString();
            }
        }

        return null;
    }

    private static bool LooksLikeNoise(string value) =>
        value is "text_delta" or "text" or "delta" or "message" or "assistant";

    private static string Shorten(string text)
    {
        var single = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= MaxLength ? single : single[..MaxLength] + "…";
    }
}
