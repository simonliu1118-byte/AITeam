namespace AITeam;

/// <summary>
/// 固定空間的說明文字，一律單行顯示：寬度不夠時自動降字級，直到整段文字放得下為止。
/// 不折行、不裁字，也不會縮寫成「…」。
/// </summary>
public sealed class AutoFitLabel : Control
{
    private const float MaxFontSize = 9F;
    private const float MinFontSize = 5.5F;
    private const float FontStep = 0.5F;

    private const TextFormatFlags Format =
        TextFormatFlags.SingleLine |
        TextFormatFlags.Left |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPadding;

    public AutoFitLabel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        // 這個控制項畫在卡片上，背景要跟著卡片走；不設定的話會吃到系統預設的灰底。
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
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
            var measured = TextRenderer.MeasureText(e.Graphics, Text, candidate, new Size(int.MaxValue, int.MaxValue), Format);
            if (measured.Width <= bounds.Width && measured.Height <= bounds.Height)
            {
                TextRenderer.DrawText(e.Graphics, Text, candidate, bounds, ForeColor, Format);
                return;
            }
        }

        using var smallest = new Font(Font.FontFamily, MinFontSize, Font.Style);
        TextRenderer.DrawText(e.Graphics, Text, smallest, bounds, ForeColor, Format);
    }
}
