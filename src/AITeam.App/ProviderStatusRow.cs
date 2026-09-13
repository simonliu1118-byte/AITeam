using System.Drawing.Drawing2D;
using AITeam.Models;

namespace AITeam;

public sealed class ProviderStatusRow
{
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);

    private readonly LampDot _lamp = new();
    private readonly Label _nameLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _detailLabel = new();
    private readonly CheckBox _enabledCheck = new();

    public ProviderStatusRow(ProviderId provider, string name)
    {
        Provider = provider;

        _nameLabel.Text = name;
        _nameLabel.AutoSize = true;
        _nameLabel.ForeColor = PrimaryText;
        _nameLabel.Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold);
        _nameLabel.Anchor = AnchorStyles.Left;
        _nameLabel.Margin = new Padding(0, 9, 18, 9);

        _lamp.Size = new Size(14, 14);
        _lamp.Anchor = AnchorStyles.Left;
        _lamp.Margin = new Padding(0, 12, 8, 9);

        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        _statusLabel.Anchor = AnchorStyles.Left;
        _statusLabel.Margin = new Padding(0, 9, 18, 9);

        // 說明欄不能用 AutoSize：AutoSize 的文字寬度會變成整張卡片的最小寬度，
        // 視窗變窄時整列就會撐出容器右緣被裁掉。改成填滿剩餘空間、過長自動縮寫。
        _detailLabel.AutoSize = false;
        _detailLabel.AutoEllipsis = true;
        _detailLabel.Dock = DockStyle.Fill;
        _detailLabel.TextAlign = ContentAlignment.MiddleLeft;
        _detailLabel.ForeColor = SecondaryText;
        _detailLabel.Margin = new Padding(0, 4, 10, 4);
        // 非 AutoSize 控制項會拿目前尺寸當成偏好尺寸，起始尺寸放到最小，
        // 視窗很窄時這一欄才能被壓縮；實際寬度由 Dock=Fill 接手。
        _detailLabel.Size = new Size(1, 1);

        _enabledCheck.Text = "本次使用";
        _enabledCheck.Checked = true;
        _enabledCheck.AutoSize = true;
        _enabledCheck.Anchor = AnchorStyles.Right;
        _enabledCheck.Margin = new Padding(0, 9, 0, 9);
        _enabledCheck.CheckedChanged += (_, _) => SessionEnabledChanged?.Invoke(this, _enabledCheck.Checked);

        SetHealth(new ProviderHealth(provider, ProviderHealthState.Unknown, "待檢查", TimeSpan.Zero));
    }

    public ProviderId Provider { get; }
    public bool SessionEnabled => _enabledCheck.Checked;
    public ProviderHealthState State { get; private set; } = ProviderHealthState.Unknown;
    public event EventHandler<bool>? SessionEnabledChanged;

    public void AddTo(TableLayoutPanel grid, int row)
    {
        grid.Controls.Add(_nameLabel, 0, row);
        grid.Controls.Add(_lamp, 1, row);
        grid.Controls.Add(_statusLabel, 2, row);
        grid.Controls.Add(_detailLabel, 3, row);
        grid.Controls.Add(_enabledCheck, 4, row);
    }

    public void SetManualDisabled()
    {
        State = ProviderHealthState.Unknown;
        _statusLabel.Text = "本次停用";
        _detailLabel.Text = "重新勾選後可再次檢查";
        _lamp.DotColor = Color.FromArgb(145, 153, 163);
        _statusLabel.ForeColor = Color.FromArgb(110, 117, 126);
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
        _lamp.DotColor = presentation.Color;
        _statusLabel.Text = presentation.Status;
        _statusLabel.ForeColor = presentation.Color;
        _detailLabel.Text = presentation.Detail;
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

    private sealed record ProviderPresentation(Color Color, string Status, string Detail);
}

public sealed class LampDot : Panel
{
    private Color _dotColor = Color.FromArgb(145, 153, 163);

    public LampDot()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    public Color DotColor
    {
        get => _dotColor;
        set { _dotColor = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var d = Math.Min(Width, Height) - 2;
        using var brush = new SolidBrush(_dotColor);
        e.Graphics.FillEllipse(brush, 1, 1, d, d);
        using var outline = new Pen(Color.FromArgb(45, 0, 0, 0), 1f);
        e.Graphics.DrawEllipse(outline, 1, 1, d, d);
    }
}
