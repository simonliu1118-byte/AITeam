using System.Drawing.Drawing2D;
using AITeam.Models;

namespace AITeam;

public sealed class ProviderStatusCard : UserControl
{
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);

    private readonly Label _nameLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _detailLabel = new();
    private readonly CheckBox _enabledCheck = new();
    private Color _lampColor = Color.FromArgb(145, 153, 163);

    public ProviderStatusCard(ProviderId provider, string name)
    {
        Provider = provider;
        Height = 68;
        MinimumSize = new Size(250, 68);
        BackColor = Color.White;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(42, 9, 12, 9);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        _nameLabel.Text = name;
        _nameLabel.AutoSize = true;
        _nameLabel.ForeColor = PrimaryText;
        _nameLabel.Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold);
        _nameLabel.Anchor = AnchorStyles.Left;
        _nameLabel.Margin = Padding.Empty;
        layout.Controls.Add(_nameLabel, 0, 0);

        _detailLabel.Text = "尚未檢查";
        _detailLabel.AutoSize = true;
        _detailLabel.ForeColor = SecondaryText;
        _detailLabel.Anchor = AnchorStyles.Left;
        _detailLabel.Margin = Padding.Empty;
        layout.Controls.Add(_detailLabel, 0, 1);

        _statusLabel.Text = "待檢查";
        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = SecondaryText;
        _statusLabel.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        _statusLabel.Anchor = AnchorStyles.Right;
        _statusLabel.Margin = new Padding(8, 0, 10, 0);
        layout.SetRowSpan(_statusLabel, 2);
        layout.Controls.Add(_statusLabel, 1, 0);

        _enabledCheck.Text = "本次使用";
        _enabledCheck.Checked = true;
        _enabledCheck.AutoSize = true;
        _enabledCheck.Anchor = AnchorStyles.Right;
        _enabledCheck.Margin = Padding.Empty;
        _enabledCheck.CheckedChanged += (_, _) => SessionEnabledChanged?.Invoke(this, _enabledCheck.Checked);
        layout.SetRowSpan(_enabledCheck, 2);
        layout.Controls.Add(_enabledCheck, 2, 0);

        Controls.Add(layout);
        SetHealth(new ProviderHealth(provider, ProviderHealthState.Unknown, "待檢查", TimeSpan.Zero));
    }

    public ProviderId Provider { get; }
    public bool SessionEnabled => _enabledCheck.Checked;
    public ProviderHealthState State { get; private set; } = ProviderHealthState.Unknown;
    public event EventHandler<bool>? SessionEnabledChanged;

    public void SetManualDisabled()
    {
        State = ProviderHealthState.Unknown;
        _statusLabel.Text = "本次停用";
        _detailLabel.Text = "重新勾選後可再次檢查";
        _lampColor = Color.FromArgb(145, 153, 163);
        _statusLabel.ForeColor = Color.FromArgb(110, 117, 126);
        Invalidate();
    }

    public void SetHealth(ProviderHealth health)
    {
        if (!SessionEnabled)
        {
            SetManualDisabled();
            return;
        }

        State = health.State;
        var presentation = GetPresentation(health);
        _lampColor = presentation.Color;
        _statusLabel.Text = presentation.Status;
        _statusLabel.ForeColor = presentation.Color;
        _detailLabel.Text = presentation.Detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var borderRect = new RectangleF(1.5f, 1.5f,
            Math.Max(1, ClientSize.Width - 3.5f),
            Math.Max(1, ClientSize.Height - 3.5f));
        using var path = CreateRoundedRectangle(borderRect, 10);
        using var pen = new Pen(BorderColor, 1.5f);
        e.Graphics.DrawPath(pen, path);

        const int diameter = 14;
        var lampX = 15;
        var lampY = (ClientSize.Height - diameter) / 2;
        using var brush = new SolidBrush(_lampColor);
        e.Graphics.FillEllipse(brush, lampX, lampY, diameter, diameter);
        using var outline = new Pen(Color.FromArgb(45, 0, 0, 0), 1f);
        e.Graphics.DrawEllipse(outline, lampX, lampY, diameter, diameter);
    }

    private static ProviderPresentation GetPresentation(ProviderHealth health) =>
        health.State switch
        {
            ProviderHealthState.Unknown => new(Color.FromArgb(145, 153, 163), "待檢查", "尚未取得狀態"),
            ProviderHealthState.Checking => new(Color.FromArgb(214, 158, 46), "檢測中", "正在送出輕量健康檢查"),
            ProviderHealthState.Online => new(Color.FromArgb(46, 160, 92), "上線", "可用"),
            ProviderHealthState.Quota => new(Color.FromArgb(224, 132, 37), "超過限額", "本次工作會自動跳過"),
            ProviderHealthState.AuthenticationRequired => new(Color.FromArgb(48, 116, 181), "需登入", "請重新完成 CLI 登入"),
            ProviderHealthState.TemporaryError => new(Color.FromArgb(207, 86, 50), "暫時異常", "稍後可重新檢查"),
            ProviderHealthState.Error => new(Color.FromArgb(194, 58, 52), "錯誤", "本次先跳過"),
            ProviderHealthState.Missing => new(Color.FromArgb(101, 108, 117), "未安裝", "找不到 CLI"),
            _ => new(Color.FromArgb(145, 153, 163), health.State.ToString(), health.Message)
        };

    private static GraphicsPath CreateRoundedRectangle(RectangleF rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2f;
        if (rect.Width <= d || rect.Height <= d)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed record ProviderPresentation(Color Color, string Status, string Detail);
}
