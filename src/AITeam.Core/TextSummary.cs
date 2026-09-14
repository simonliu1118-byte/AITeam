using System.Text.RegularExpressions;

namespace AITeam.Services;

public static class TextSummary
{
    /// <summary>把多行訊息壓成一行並截長度，避免一則訊息洗掉整個畫面。</summary>
    public static string OneLine(string text, int max)
    {
        var value = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        if (value.Length == 0) return "（沒有錯誤訊息）";
        return value.Length <= max ? value : value[..max].TrimEnd() + "…";
    }

    /// <summary>
    /// 把段落之間的空行拿掉。AI 的回答習慣用空行分段，一場會議一輪三個人疊起來
    /// 就會變成一大片空白，光第一輪就要一直上下捲；發言本身有標題與分隔線可以區隔，
    /// 不需要再靠空行。
    /// </summary>
    public static string CompactParagraphs(string text)
    {
        var normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0);

        return string.Join(Environment.NewLine, lines);
    }
}
