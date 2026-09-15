using AITeam.Services;

namespace AITeam;

/// <summary>
/// 一些「試試看、不行就關回去」性質的選項。這裡的每一項預設都是關閉＝維持原本
/// 已知穩定的行為，勾起來才會改變做法，而且不用重開程式也不用改檔案。
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);
    private static readonly Color WarnText = Color.FromArgb(180, 83, 9);

    private readonly AppPreferencesService _preferences;
    private readonly CheckBox _hideConsole = new();
    private readonly Label _hideConsoleState = new();

    private AppPreferences _current;
    // 套用失敗時要把勾取消，而取消又會再觸發一次 CheckedChanged。用旗標擋掉這一圈。
    private bool _applying;

    public SettingsForm(AppPreferencesService preferences, AppPreferences current)
    {
        _preferences = preferences;
        _current = current;

        Text = "AITeam - 設定";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 360);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        shell.Controls.Add(new Label
        {
            Text = "實驗性選項",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(2, 0, 0, 8)
        }, 0, 0);

        shell.Controls.Add(BuildHideConsoleCard(), 0, 1);

        var close = new Button
        {
            Text = "關閉",
            AutoSize = false,
            Size = new Size(96, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 12, 2, 0),
            Cursor = Cursors.Hand
        };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Close();
        shell.Controls.Add(close, 0, 2);
        AcceptButton = close;
        CancelButton = close;
    }

    private Control BuildHideConsoleCard()
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Padding = new Padding(16, 14, 16, 14)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = CardBackground
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(layout);

        _hideConsole.Text = "嘗試隱藏 CLI 的主控台視窗（開程式時閃一下的黑框）";
        _hideConsole.AutoSize = true;
        _hideConsole.ForeColor = PrimaryText;
        _hideConsole.Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold);
        // 顯示「實際上有沒有生效」，不是「設定檔寫了什麼」。開程式時如果套用失敗，
        // 這裡就該是沒勾的狀態，不能讓畫面說謊。
        _hideConsole.Checked = HiddenConsole.IsActive;
        _hideConsole.Margin = new Padding(0, 0, 0, 8);
        _hideConsole.CheckedChanged += (_, _) => ApplyHideConsole();
        layout.Controls.Add(_hideConsole, 0, 0);

        layout.Controls.Add(new Label
        {
            Text =
                "閃出來的黑框不是 AI CLI 本身，而是它自己再叫起來的小程式（Codex 在 Windows 上是用 " +
                "PowerShell 跑工具）。勾起來之後，AITeam 會先幫自己準備一個看不見的主控台，" +
                "讓這些程式共用它，理論上就不會再跳出新視窗。" + Environment.NewLine + Environment.NewLine +
                "這個手法不保證有效：如果那個小程式是「指名要開一個新視窗」，誰都攔不住。" +
                "所以請直接勾起來重開程式看看——沒效果或反而更奇怪，就把勾取消，" +
                "行為會完全回到現在這個穩定的版本，不需要換版本也不用改任何檔案。",
            AutoSize = true,
            MaximumSize = new Size(470, 0),
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            Margin = new Padding(22, 0, 0, 10)
        }, 0, 1);

        _hideConsoleState.AutoSize = true;
        _hideConsoleState.MaximumSize = new Size(470, 0);
        _hideConsoleState.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        _hideConsoleState.Margin = new Padding(22, 0, 0, 0);
        layout.Controls.Add(_hideConsoleState, 0, 2);

        UpdateHideConsoleState();
        return card;
    }

    private void ApplyHideConsole()
    {
        if (_applying) return;
        _applying = true;
        try
        {
            ApplyHideConsoleCore();
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplyHideConsoleCore()
    {
        var wanted = _hideConsole.Checked;
        var applied = HiddenConsole.Apply(wanted);

        if (wanted && !applied)
        {
            // 套不上去就誠實地把勾拿掉，不要讓畫面顯示一個其實沒生效的狀態。
            _hideConsole.Checked = false;
            MessageBox.Show(
                this,
                "這台電腦上沒辦法準備隱藏的主控台，已經自動回到原本的做法。",
                "AITeam",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            UpdateHideConsoleState();
            return;
        }

        _current = _current with { HideCliConsole = HiddenConsole.IsActive };
        try
        {
            _preferences.Save(_current);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"設定沒有存起來：{ex.Message}", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        UpdateHideConsoleState();
    }

    private void UpdateHideConsoleState()
    {
        if (HiddenConsole.IsActive)
        {
            _hideConsoleState.Text = "目前狀態：已開啟。下一次叫用 AI 就會套用；要看開程式時還會不會閃，請重開 AITeam。";
            _hideConsoleState.ForeColor = WarnText;
        }
        else
        {
            _hideConsoleState.Text = "目前狀態：關閉（＝現在這個穩定的做法）。";
            _hideConsoleState.ForeColor = SecondaryText;
        }
    }
}
