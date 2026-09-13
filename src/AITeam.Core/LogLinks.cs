using System.Text;
using System.Text.RegularExpressions;

namespace AITeam.Services;

/// <summary>被折疊起來的一個參照位置：在文字裡的範圍，以及原本的完整路徑。</summary>
public readonly record struct LogLinkSpan(int Start, int Length, string Target)
{
    public int End => Start + Length;
    public bool Contains(int index) => index >= Start && index < End;
}

public sealed record FoldedLog(string Text, IReadOnlyList<LogLinkSpan> Links);

/// <summary>
/// AI 回覆常把參照寫成 Markdown 連結，例如
/// <c>[PROJECT_RULES.md](file:///D:/AITeam/tasks/inquiry-xxxx/apps/CYWatermark/PROJECT_RULES.md)</c>。
/// 完整路徑寫在文章裡會把版面撐得非常長，但路徑本身還是有用，所以這裡只把顯示文字留下來，
/// 完整路徑收起來交給畫面用 tooltip 呈現。
/// </summary>
public static class LogLinks
{
    // [顯示文字](路徑)：顯示文字不跨行、路徑不含空白與右括號。
    private static readonly Regex MarkdownLink = new(
        @"\[([^\]\r\n]{1,200})\]\(\s*<?([^)\r\n<>]{1,1000}?)>?\s*\)",
        RegexOptions.Compiled);

    public static FoldedLog Fold(string text)
    {
        if (string.IsNullOrEmpty(text)) return new FoldedLog("", Array.Empty<LogLinkSpan>());

        var matches = MarkdownLink.Matches(text);
        if (matches.Count == 0) return new FoldedLog(text, Array.Empty<LogLinkSpan>());

        var builder = new StringBuilder(text.Length);
        var links = new List<LogLinkSpan>(matches.Count);
        var copiedUpTo = 0;

        foreach (Match match in matches)
        {
            builder.Append(text, copiedUpTo, match.Index - copiedUpTo);

            var label = match.Groups[1].Value;
            var target = match.Groups[2].Value.Trim();
            links.Add(new LogLinkSpan(builder.Length, label.Length, Describe(target)));
            builder.Append(label);

            copiedUpTo = match.Index + match.Length;
        }

        builder.Append(text, copiedUpTo, text.Length - copiedUpTo);
        return new FoldedLog(builder.ToString(), links);
    }

    /// <summary>把 file:/// 開頭還原成一般看得懂的路徑，其他連結原樣保留。</summary>
    internal static string Describe(string target)
    {
        if (!target.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) return target;

        var path = Uri.UnescapeDataString(target["file:///".Length..]);
        return path.Replace('/', '\\');
    }
}
