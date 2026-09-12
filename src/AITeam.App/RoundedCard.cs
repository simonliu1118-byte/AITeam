using System.Drawing.Drawing2D;

namespace AITeam;

public sealed class RoundedCard : Panel
{
    private int _radius = 10;
    private Color _borderColor = Color.FromArgb(190, 199, 210);

    public RoundedCard()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
    }

    public int Radius
    {
        get => _radius;
        set { _radius = Math.Max(0, value); Invalidate(); }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(1.5f, 1.5f,
            Math.Max(1, ClientSize.Width - 3.5f),
            Math.Max(1, ClientSize.Height - 3.5f));
        using var path = CreateRoundedRectangle(rect, Radius);
        using var pen = new Pen(BorderColor, 1.5f);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || rect.Width <= radius * 2 || rect.Height <= radius * 2)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }
        var d = radius * 2f;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
