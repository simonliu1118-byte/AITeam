using System.Drawing.Drawing2D;
using AITeam.Models;
using AITeam.Services;

namespace AITeam;

public sealed class ProviderStatusRow
{
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);

    private readonly LampDot _lamp = new();
    private readonly Label _nameLabel = new();
    private readonly Label _statusLabel = new();
    private readonly AutoFitLabel _detailLabel = new();
    private readonly CheckBox _enabledCheck = new();
    private readonly Button _actionButton = new();
    private readonly TableLayoutPanel _detailCell = new();
    private readonly Func<ProviderHealthState, CliMaintenanceAction?> _actionFor;
    private CliMaintenanceAction? _action;

    // 最後一次真正檢查到的狀態。使用者勾不勾「本次使用」都不會動到它。
    private ProviderHealth _health;

    /// <param name="actionFor">這個狀態下卡片上可以直接按的修復動作（登入、更新），沒有就回 null。</param>
    public ProviderStatusRow(ProviderId provider, string name, Func<ProviderHealthState, CliMaintenanceAction?> actionFor)
    {
        Provider = provider;
        _actionFor = actionFor;

        _nameLabel.Text = name;
        _nameLabel.AutoSize = true;
        _nameLabel.ForeColor = PrimaryText;
        _nameLabel.Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold);
        _nameLabel.Anchor = AnchorStyles.Left;
        _nameLabel.Margin = new Padding(0, 9, 12, 9);

        _lamp.Size = new Size(14, 14);
        _lamp.Anchor = AnchorStyles.Left;
        // 上下邊界要對稱，TableLayoutPanel 才會把燈號真正置中；左右不等會讓燈號比
        // 旁邊的文字高或低一點點，看起來就沒對齊。
        _lamp.Margin = new Padding(0, 9, 8, 9);

        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        _statusLabel.Anchor = AnchorStyles.Left;
        _statusLabel.Margin = new Padding(0, 9, 12, 9);

        // 說明欄用固定空間：填滿剩餘寬度、一律單行，放不下就自動降字級，
        // 確保整段文字一定完整顯示，不折行、不裁切，也不縮寫成 …。
        // 也不能用 AutoSize，否則文字長度會變成整張卡片的最小寬度，
        // 視窗變窄時整列會撐出容器右緣被裁掉。
        _detailLabel.Dock = DockStyle.Fill;
        _detailLabel.Font = new Font("Microsoft JhengHei UI", 9F);
        _detailLabel.ForeColor = SecondaryText;
        // 明確跟著卡片的白底，不要留給系統預設（會變成灰底色塊）。
        _detailLabel.BackColor = Color.White;
        _detailLabel.Margin = new Padding(0, 2, 10, 2);

        // 登入過期、CLI 太舊的時候，說明欄右邊多一顆按鈕，按了 AITeam 就替你處理。
        _actionButton.AutoSize = false;
        _actionButton.Size = new Size(58, 28);
        _actionButton.FlatStyle = FlatStyle.Flat;
        _actionButton.FlatAppearance.BorderColor = Color.FromArgb(43, 108, 176);
        _actionButton.BackColor = Color.White;
        _actionButton.ForeColor = Color.FromArgb(43, 108, 176);
        _actionButton.Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold);
        _actionButton.Anchor = AnchorStyles.Right;
        _actionButton.Margin = new Padding(0, 4, 10, 4);
        _actionButton.Visible = false;
        _actionButton.Click += (_, _) =>
        {
            if (_action is not null) ActionRequested?.Invoke(this, _action);
        };

        _detailCell.Dock = DockStyle.Fill;
        _detailCell.ColumnCount = 2;
        _detailCell.RowCount = 1;
        _detailCell.Margin = Padding.Empty;
        _detailCell.BackColor = Color.White;
        _detailCell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _detailCell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _detailCell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _detailCell.Controls.Add(_detailLabel, 0, 0);
        _detailCell.Controls.Add(_actionButton, 1, 0);

        _enabledCheck.Text = "本次使用";
        _enabledCheck.Checked = true;
        _enabledCheck.AutoSize = true;
        _enabledCheck.Anchor = AnchorStyles.Right;
        _enabledCheck.Margin = new Padding(0, 9, 0, 9);
        _enabledCheck.CheckedChanged += (_, _) =>
        {
            // 勾不勾只決定這一輪要不要派工，跟這家 AI 的狀態無關，所以不重畫燈號、
            // 也不把狀態打回「待檢查」，只換說明文字。
            Render();
            SessionEnabledChanged?.Invoke(this, _enabledCheck.Checked);
        };

        _health = new ProviderHealth(provider, ProviderHealthState.Unknown, "待檢查", TimeSpan.Zero);
        Render();
    }

    public ProviderId Provider { get; }
    public bool SessionEnabled => _enabledCheck.Checked;
    public ProviderHealthState State => _health.State;
    public event EventHandler<bool>? SessionEnabledChanged;
    public event EventHandler<CliMaintenanceAction>? ActionRequested;

    /// <summary>修復動作執行中：按鈕先停用，避免同一個登入視窗被開兩次。</summary>
    public void SetActionBusy(bool busy) => _actionButton.Enabled = !busy;

    public void AddTo(TableLayoutPanel grid, int row)
    {
        grid.Controls.Add(_nameLabel, 0, row);
        grid.Controls.Add(_lamp, 1, row);
        grid.Controls.Add(_statusLabel, 2, row);
        grid.Controls.Add(_detailCell, 3, row);
        grid.Controls.Add(_enabledCheck, 4, row);
    }

    public void SetHealth(ProviderHealth health)
    {
        _health = health;
        Render();
    }

    private void Render()
    {
        var presentation = GetPresentation(_health);
        _lamp.DotColor = presentation.Color;
        _statusLabel.Text = presentation.Status;
        _statusLabel.ForeColor = presentation.Color;
        _detailLabel.Text = SessionEnabled ? presentation.Detail : "本次不使用，狀態仍會持續更新";

        _action = _actionFor(_health.State);
        _actionButton.Visible = _action is not null;
        if (_action is not null) _actionButton.Text = _action.ButtonText;
    }

    private ProviderPresentation GetPresentation(ProviderHealth health) =>
        health.State switch
        {
            // Antigravity 沒有登入、更新的指令，只能在它自己的程式裡處理。
            ProviderHealthState.AuthenticationRequired when Provider == ProviderId.Antigravity =>
                new(Color.FromArgb(48, 116, 181), "需登入", "請打開 Antigravity 程式重新登入"),
            ProviderHealthState.AuthenticationRequired => new(Color.FromArgb(48, 116, 181), "需登入", "按右邊「登入」，在跳出的瀏覽器登入"),
            ProviderHealthState.Error when Provider != ProviderId.Antigravity =>
                new(Color.FromArgb(194, 58, 52), "錯誤", "原因見右側紀錄；可以先按「更新」試試"),
            ProviderHealthState.Unknown => new(Color.FromArgb(145, 153, 163), "待檢查", "尚未取得狀態"),
            ProviderHealthState.Checking => new(Color.FromArgb(214, 158, 46), "檢測中", "正在送出輕量健康檢查"),
            ProviderHealthState.Online => new(Color.FromArgb(46, 160, 92), "上線", "可用"),
            ProviderHealthState.Quota => new(Color.FromArgb(224, 132, 37), "超過限額", "本次工作會自動跳過"),
            ProviderHealthState.TemporaryError => new(Color.FromArgb(207, 86, 50), "暫時異常", "稍後可重新檢查，原因見右側紀錄"),
            // 「錯誤」是分類不出來時的結果，卡片這一格放不下 CLI 原文（放進來會被縮到看不清楚），
            // 所以這裡只指路，完整原文寫在右側的執行紀錄。
            ProviderHealthState.Error => new(Color.FromArgb(194, 58, 52), "錯誤", "本次先跳過，原因見右側紀錄"),
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
