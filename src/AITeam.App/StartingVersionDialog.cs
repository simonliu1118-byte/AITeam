using AITeam.Services;

namespace AITeam;

/// <summary>
/// 專案還沒有 VERSION 檔時，問使用者要從哪一版開始。
///
/// 依共用規則，版號第一碼是使用者的決定，AITeam 不能自己挑一個數字寫進去——
/// 一個已經用了好幾年的舊專案，說不定實際上早就是 2.x。所以這裡一定要問。
/// </summary>
public sealed class StartingVersionDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);
    private static readonly Color ErrorText = Color.FromArgb(176, 54, 54);

    private readonly TextBox _versionBox = new();
    private readonly Label _error = new();

    public string? Version { get; private set; }

    public StartingVersionDialog(StartingVersionPrompt prompt)
    {
        Text = "AITeam - 決定起始版號";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 300);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi(prompt);
    }

    private void BuildUi(StartingVersionPrompt prompt)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20),
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 4; i++) shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(shell);

        shell.Controls.Add(new Label
        {
            Text = prompt.IsNewProject
                ? $"新專案「{prompt.ProjectName}」要從哪一版開始？"
                : $"「{prompt.ProjectName}」還沒有 VERSION 檔，要從哪一版開始？",
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        shell.Controls.Add(new Label
        {
            Text = prompt.IsNewProject
                ? "這次修改會建立專案資料夾和 VERSION 檔，跟著修改一起送 PR。全新的專案通常從 0.1.0 開始。"
                : "這次修改會建立 VERSION 檔，跟著修改一起送 PR。如果這個專案其實已經用了一段時間、"
                  + "有自己認定的版本，請填那個版本；沒有的話 0.1.0 就好。",
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            Margin = new Padding(0, 0, 0, 14)
        }, 0, 1);

        _versionBox.Text = "0.1.0";
        _versionBox.Width = 160;
        _versionBox.Font = new Font("Consolas", 12F);
        _versionBox.Margin = new Padding(0, 0, 0, 6);
        _versionBox.TextChanged += (_, _) => _error.Text = "";
        shell.Controls.Add(_versionBox, 0, 2);

        _error.AutoSize = true;
        _error.ForeColor = ErrorText;
        _error.Font = new Font("Microsoft JhengHei UI", 9F);
        _error.Margin = new Padding(0, 0, 0, 10);
        shell.Controls.Add(_error, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Margin = Padding.Empty
        };

        var ok = MakeButton("用這個版號開始", 150, primary: true);
        ok.Margin = new Padding(0, 0, 10, 0);
        ok.Click += (_, _) => Confirm();
        buttons.Controls.Add(ok);
        AcceptButton = ok;

        var cancel = MakeButton("先不要做", 110, primary: false);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(cancel);
        CancelButton = cancel;

        shell.Controls.Add(buttons, 0, 4);
    }

    private void Confirm()
    {
        var text = _versionBox.Text.Trim();
        if (!ProjectPreflight.IsPlainVersion(text))
        {
            _error.Text = "請填 X.Y.Z 這種格式，例如 0.1.0（不要加 v，也不要加 -beta 之類的字尾）。";
            _versionBox.Focus();
            return;
        }

        Version = text;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Button MakeButton(string text, int width, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(width, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : PrimaryText,
            Font = new Font("Microsoft JhengHei UI", 10F, primary ? FontStyle.Bold : FontStyle.Regular),
            Margin = Padding.Empty
        };
        if (primary) button.FlatAppearance.BorderSize = 0;
        else button.FlatAppearance.BorderColor = BorderColor;
        return button;
    }
}
