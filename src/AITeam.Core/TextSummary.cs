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
}
