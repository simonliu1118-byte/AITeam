namespace AITeam;

/// <summary>
/// 固定空間的說明文字。先在目前寬度內折行（最多兩行），若還是放不下就自動降字級，
/// 直到整段文字完整顯示為止；不會裁掉文字，也不會縮寫成「…」。
/// </summary>
public sealed class AutoFitLabel : Control
{
    private const float MaxFontSize = 9F;
    private const float MinFontSize = 6F;
    private const float FontStep = 0.5F;
    private const int MaxLines = 2;

    private const TextFormatFlags Format =
        TextFormatFlags.WordBreak |
        TextFormatFlags.Left |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPadding;

    public AutoFitLabel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        // 保證永遠留得下兩行最大字級的高度，讓折行後不會被上下裁掉。
        MinimumSize = new Size(0, (int)Math.Ceiling(MaxFontSize * 1.9F) * MaxLines);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Text.Length == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        var bounds = ClientRectangle;
        for (var size = MaxFontSize; size >= MinFontSize; size -= FontStep)
        {
            using var candidate = new Font(Font.FontFamily, size, Font.Style);
            var wrapped = TextRenderer.MeasureText(e.Graphics, Text, candidate, new Size(bounds.Width, int.MaxValue), Format);
            var lineHeight = TextRenderer.MeasureText(e.Graphics, "字", candidate, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
            if (wrapped.Height <= Math.Min(bounds.Height, lineHeight * MaxLines))
            {
                TextRenderer.DrawText(e.Graphics, Text, candidate, bounds, ForeColor, Format);
                return;
            }
        }

        using var smallest = new Font(Font.FontFamily, MinFontSize, Font.Style);
        TextRenderer.DrawText(e.Graphics, Text, smallest, bounds, ForeColor, Format);
    }
}
