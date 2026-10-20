using System.Text.Json;
using System.Text.RegularExpressions;

namespace AITeam.Services;

/// <summary>
/// Codex 用哪個模型。平常交給 Codex 自己的預設；預設模型你的帳號不能用時（例如
/// 「not supported when using Codex with a ChatGPT account」），健康檢查會從 Codex 內建的
/// 模型清單（codex debug models）依序試，找到能用的就記在偏好裡，之後每次叫 Codex 都指定它。
/// </summary>
public static class CodexModels
{
    private static readonly Regex SlugPattern = new("^[A-Za-z0-9._-]{1,80}$", RegexOptions.CultureInvariant);

    public static bool IsValidSlug(string? slug) => slug is not null && SlugPattern.IsMatch(slug);

    /// <summary>要加在 Codex 指令裡的模型參數；沒有記下模型就是空的。</summary>
    public static IReadOnlyList<string> Args(string runtimeRoot) =>
        ArgsForModel(new AppPreferencesService(runtimeRoot).Load().CodexModel);

    internal static IReadOnlyList<string> ArgsForModel(string? model) =>
        IsValidSlug(model) ? new[] { "-c", $"model=\"{model}\"" } : Array.Empty<string>();

    /// <summary>錯誤是不是「這個模型不能用」，而不是額度、登入或網路問題。</summary>
    public static bool LooksLikeUnsupportedModel(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("model is not supported")
               || lower.Contains("not supported when using codex")
               || lower.Contains("model_not_found")
               || lower.Contains("model not found")
               || lower.Contains("unknown model")
               || lower.Contains("does not exist or you do not have access")
               || lower.Contains("requires a newer version of codex");
    }

    /// <summary>
    /// codex debug models 的輸出：只取標成 list（會出現在 /model 選單裡）的模型，依 priority 由小到大排。
    /// </summary>
    internal static IReadOnlyList<string> ParseCatalog(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return models.EnumerateArray()
                .Where(m => m.TryGetProperty("visibility", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == "list")
                .Select(m => (
                    Slug: m.TryGetProperty("slug", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null,
                    Priority: m.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : int.MaxValue))
                .Where(m => IsValidSlug(m.Slug))
                .OrderBy(m => m.Priority)
                .Select(m => m.Slug!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>錯誤訊息裡提到、確定不能用的模型名稱（'xxx' model），試的時候跳過它。</summary>
    internal static string? RejectedModel(string text)
    {
        var match = Regex.Match(text, @"'([A-Za-z0-9._-]{1,80})' model", RegexOptions.CultureInvariant);
        if (match.Success) return match.Groups[1].Value;
        match = Regex.Match(text, @"Model metadata for `([A-Za-z0-9._-]{1,80})`", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }
}
