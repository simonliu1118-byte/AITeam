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
        ProviderId.Claude => DescribeClaudeLine(line) ?? DescribePlainLine(line),
        _ => DescribePlainLine(line)
    };

    /// <summary>
    /// Claude 的串流事件跟另外兩家長得不一樣：工具名稱包在
    /// <c>message.content[]</c> 裡面的 <c>tool_use</c> 區塊，通用的找法抓不到。
    /// </summary>
    private static string? DescribeClaudeLine(string line)
    {
        var text = line.TrimStart();
        if (text.Length == 0 || text[0] != '{') return null;

        try
        {
            using var document = JsonDocument.Parse(text);
            var described = ClaudeStream.DescribeEvent(document.RootElement);
            return described is null ? null : Shorten(described);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? DescribePlainLine(string line)
    {
        var text = line.Trim();
        if (text.Length == 0) return null;

        // 純裝飾用的分隔線、進度條殘骸之類的，不值得佔畫面。
        if (text.All(c => c is '-' or '=' or '_' or '*' or '.' or '~' or '─' or '━')) return null;
        if (text.StartsWith("[2K", StringComparison.Ordinal)) return null;
        if (IsStackNoise(text)) return null;

        return Shorten(text);
    }

    /// <summary>
    /// AI 在工作中自己踩到的錯誤（例如 Codex 在 Windows 上用 PowerShell 找不到某個檔），
    /// 對它來說往往只是試一下、失敗就換個方法，但顯示在「現在在做什麼」那一行會很嚇人，
    /// 讓人以為整件事失敗了——實際上它後來照樣正常發言。這類噪音一律不顯示。
    /// </summary>
    private static bool IsStackNoise(string text)
    {
        if (text.StartsWith("+ ", StringComparison.Ordinal)) return true;
        if (text.StartsWith("At line:", StringComparison.OrdinalIgnoreCase)) return true;

        return text.Contains("FullyQualifiedErrorId", StringComparison.OrdinalIgnoreCase)
            || text.Contains("CategoryInfo", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Microsoft.PowerShell.Commands", StringComparison.OrdinalIgnoreCase);
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
