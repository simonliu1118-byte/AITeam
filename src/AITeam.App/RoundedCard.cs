using System.Drawing.Drawing2D;

namespace AITeam;

public sealed class RoundedCard : Panel
{
    private int _radius = 10;
    private Color _borderColor = Color.LightGray;

    public RoundedCard()
    {
        DoubleBuffered = true;
    }

    public int Radius
    {
        get => _radius;
        set
        {
            _radius = Math.Max(0, value);
            UpdateRegion();
            Invalidate();
        }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = CreateRoundedRectangle(ClientRectangle, Radius);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedRectangle(ClientRectangle, Radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var rect = new Rectangle(
            bounds.X,
            bounds.Y,
            Math.Max(1, bounds.Width - 1),
            Math.Max(1, bounds.Height - 1));

        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }

        var diameter = Math.Max(2, radius * 2);

        if (rect.Width <= diameter || rect.Height <= diameter)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }

        var arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);

        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);

        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
