using System.Drawing.Drawing2D;

namespace AITeam;

/// <summary>
/// 右上角狀態徽章：圓角底色＋狀態燈點＋文字（可再帶一段小字，例如「4/7」）。
/// 寬度依內容自動調整，不會把文字擠掉。
/// </summary>
public sealed class StatusBadge : Control
{
    private const int PaddingX = 13;
    private const int DotSize = 7;
    private const int DotGap = 7;
    private const int MeterGap = 7;

    private Color _fill = Color.FromArgb(236, 248, 240);
    private Color _dot = Color.FromArgb(46, 160, 92);
    private Color _outline = Color.Empty;
    private string _meter = string.Empty;

    public StatusBadge()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Height = 30;
        Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        ForeColor = Color.FromArgb(36, 122, 72);
        Text = "待命";
        AdjustWidth();
    }

    private Font MeterFont => new(Font.FontFamily, 8.5F, FontStyle.Regular);

    public void SetStatus(string text, string meter, Color fill, Color foreColor, Color dot, Color outline)
    {
        _meter = meter ?? string.Empty;
        _fill = fill;
        _dot = dot;
        _outline = outline;
        ForeColor = foreColor;
        Text = text;
        AdjustWidth();
        Invalidate();
    }

    private void AdjustWidth()
    {
        var width = PaddingX * 2 + DotSize + DotGap + TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        if (_meter.Length > 0)
        {
            using var meterFont = MeterFont;
            width += MeterGap + TextRenderer.MeasureText(_meter, meterFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        }
        Width = width;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
        using (var path = CreateRoundedPath(bounds, 6))
        {
            using var fill = new SolidBrush(_fill);
            e.Graphics.FillPath(fill, path);
            if (_outline != Color.Empty)
            {
                using var pen = new Pen(_outline, 1.4f);
                e.Graphics.DrawPath(pen, path);
            }
        }

        var dotRect = new Rectangle(PaddingX, (ClientSize.Height - DotSize) / 2, DotSize, DotSize);
        using (var dotBrush = new SolidBrush(_dot))
            e.Graphics.FillEllipse(dotBrush, dotRect);

        var textLeft = dotRect.Right + DotGap;
        var textSize = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(textLeft, 0, textSize.Width, ClientSize.Height),
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (_meter.Length == 0) return;
        using var meterFont = MeterFont;
        var meterLeft = textLeft + textSize.Width + MeterGap;
        TextRenderer.DrawText(
            e.Graphics,
            _meter,
            meterFont,
            new Rectangle(meterLeft, 0, ClientSize.Width - meterLeft, ClientSize.Height),
            Color.FromArgb(200, ForeColor),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
