using System.Drawing.Drawing2D;

namespace AITeam;

/// <summary>
/// 自動更新時使用者看得到的東西（跟 CYERPAutoInput 一樣）：主視窗上方一條提示列（下載中、等待中、
/// 更新完成或失敗），以及重新開啟前的倒數視窗。
/// </summary>
internal static class UpdateColors
{
    public static readonly Color Accent = Color.FromArgb(43, 108, 176);
    public static readonly Color AccentSoft = Color.FromArgb(230, 239, 249);
    public static readonly Color Success = Color.FromArgb(46, 125, 50);
    public static readonly Color SuccessSoft = Color.FromArgb(232, 245, 233);
    public static readonly Color Danger = Color.FromArgb(176, 54, 54);
    public static readonly Color DangerSoft = Color.FromArgb(251, 234, 234);
    public static readonly Color Text = Color.FromArgb(34, 40, 49);
    public static readonly Color Border = Color.FromArgb(190, 199, 210);
    public static readonly Color Window = Color.FromArgb(244, 247, 250);
}

internal sealed class UpdateSpinner : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    private float _angle;

    public UpdateSpinner()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(20, 20);
        _timer.Tick += (_, _) => { _angle = (_angle + 12) % 360; Invalidate(); };
    }

    public Color Color { get; set; } = UpdateColors.Accent;

    public bool Spinning
    {
        get => _timer.Enabled;
        set { _timer.Enabled = value; Visible = value; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height) - 4;
        var box = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
        using var track = new Pen(Color.FromArgb(70, Color), 2.5f);
        using var arc = new Pen(Color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawEllipse(track, box);
        e.Graphics.DrawArc(arc, box, _angle, 100);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

internal enum UpdateBannerKind { Progress, Success, Failure }

/// <summary>
/// 主視窗上方的提示列：忙的時候轉圈，結束時顯示 ✓／!，可以按 ✕ 關掉。
/// 文字太長就換行、把提示列撐高，不會裁成「…」。
/// </summary>
internal sealed class UpdateBanner : Panel
{
    private readonly UpdateSpinner _spinner = new() { Margin = Padding.Empty };
    private readonly Label _mark = new() { AutoSize = false, Size = new Size(20, 20), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold) };
    private readonly Label _text = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft JhengHei UI", 9.5F) };
    private readonly Button _close = new() { Text = "✕", FlatStyle = FlatStyle.Flat, Size = new Size(28, 24), TabStop = false };
    private readonly System.Windows.Forms.Timer _hide = new();

    public UpdateBanner()
    {
        Dock = DockStyle.Top;
        Height = 34;
        Padding = new Padding(14, 0, 8, 0);
        Visible = false;
        _close.FlatAppearance.BorderSize = 0;
        _close.Click += (_, _) => Visible = false;
        _hide.Tick += (_, _) => { _hide.Stop(); Visible = false; };
        Controls.AddRange(new Control[] { _spinner, _mark, _text, _close });
        Layout += (_, _) => Arrange();
    }

    private void Arrange()
    {
        var textLeft = Padding.Left + 28;
        var textWidth = Math.Max(40, Width - Padding.Right - _close.Width - 8 - textLeft);
        var needed = TextRenderer.MeasureText(_text.Text, _text.Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak).Height;
        var height = Math.Max(34, needed + 14);
        if (Height != height) Height = height;

        var y = (Height - 20) / 2;
        _spinner.Location = new Point(Padding.Left, y);
        _mark.Location = new Point(Padding.Left, y);
        _close.Location = new Point(Width - Padding.Right - _close.Width, (Height - _close.Height) / 2);
        _text.SetBounds(textLeft, 0, textWidth, Height);
    }

    /// <summary>顯示一段訊息；<paramref name="hideAfter"/> 時間到自己收起來。進行中的狀態不能關。</summary>
    public void Show(UpdateBannerKind kind, string text, TimeSpan? hideAfter = null)
    {
        var (color, soft) = kind switch
        {
            UpdateBannerKind.Success => (UpdateColors.Success, UpdateColors.SuccessSoft),
            UpdateBannerKind.Failure => (UpdateColors.Danger, UpdateColors.DangerSoft),
            _ => (UpdateColors.Accent, UpdateColors.AccentSoft)
        };
        BackColor = soft;
        _text.BackColor = soft;
        _mark.BackColor = soft;
        _close.BackColor = soft;
        _text.ForeColor = UpdateColors.Text;
        _text.Text = text;
        _spinner.Color = color;
        _spinner.Spinning = kind == UpdateBannerKind.Progress;
        _mark.Visible = kind != UpdateBannerKind.Progress;
        _mark.ForeColor = color;
        _mark.Text = kind == UpdateBannerKind.Success ? "✓" : "!";
        _close.Visible = kind != UpdateBannerKind.Progress;
        _hide.Stop();
        if (hideAfter is { } after) { _hide.Interval = (int)after.TotalMilliseconds; _hide.Start(); }
        Visible = true;
        PerformLayout();
    }

    public void HideBanner()
    {
        _hide.Stop();
        _spinner.Spinning = false;
        Visible = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new Pen(UpdateColors.Border);
        e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hide.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// 重新開啟前的倒數（10 秒；「稍後再更新」延 30 分鐘）。OK：現在更新（時間到或按「立即更新」）；
/// Ignore：稍後；Abort：倒數期間 AITeam 變忙了（<paramref name="busy"/>），等閒下來再說。
/// </summary>
internal sealed class UpdateCountdownForm : Form
{
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly Label _text = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private int _left;

    public UpdateCountdownForm(string program, string version, int seconds, Func<bool> busy)
    {
        _left = seconds;
        Text = $"{program} 更新";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = UpdateColors.Window;
        ClientSize = new Size(440, 150);

        var spinner = new UpdateSpinner { Size = new Size(36, 36), Location = new Point(22, 30) };
        spinner.Spinning = true;
        _text.SetBounds(72, 16, 350, 64);
        _text.ForeColor = UpdateColors.Text;
        var now = MakeButton("立即更新", 100, new Point(212, 100), primary: true);
        now.DialogResult = DialogResult.OK;
        var later = MakeButton("稍後再更新", 110, new Point(318, 100), primary: false);
        later.DialogResult = DialogResult.Ignore;
        Controls.AddRange(new Control[] { spinner, _text, now, later });
        AcceptButton = now;

        void Render() => _text.Text = $"{program} 有新版 V{version}。\n{_left} 秒後自動更新並重新開啟。";
        Render();
        _tick.Tick += (_, _) =>
        {
            if (busy()) { DialogResult = DialogResult.Abort; return; }
            if (--_left <= 0) { DialogResult = DialogResult.OK; return; }
            Render();
        };
        Shown += (_, _) => { Activate(); _tick.Start(); };
        FormClosed += (_, _) => _tick.Stop();
    }

    private static Button MakeButton(string text, int width, Point location, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, 32),
            Location = location,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? UpdateColors.Accent : Color.White,
            ForeColor = primary ? Color.White : UpdateColors.Text
        };
        if (primary) button.FlatAppearance.BorderSize = 0;
        else button.FlatAppearance.BorderColor = UpdateColors.Border;
        return button;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }
}
