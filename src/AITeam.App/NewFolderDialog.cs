namespace AITeam;

/// <summary>
/// 在 Repo 裡開一個新資料夾給新專案用。資料夾只是先登記下來，真正建立是在第一次修改任務，
/// 跟著那次的修改一起送 PR——git 不會保存空資料夾，而且 AITeam 對 Repo 的任何改動
/// 都要經過 PR、CI 和你按合併。
/// </summary>
public sealed class NewFolderDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);
    private static readonly Color ErrorText = Color.FromArgb(176, 54, 54);

    private readonly TextBox _nameBox = new();
    private readonly Label _error = new();
    private readonly Func<string, string?> _validate;

    public string FolderName { get; private set; } = "";

    /// <param name="parentDisplay">要建在哪一層底下，顯示給使用者看。</param>
    /// <param name="validate">回傳 null 代表名稱可以用，否則回傳要顯示的原因。</param>
    public NewFolderDialog(string parentDisplay, Func<string, string?> validate)
    {
        _validate = validate;

        Text = "AITeam - 新增資料夾";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 270);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi(parentDisplay);
    }

    private void BuildUi(string parentDisplay)
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
            Text = $"在「{parentDisplay}」底下新增資料夾",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        shell.Controls.Add(new Label
        {
            Text = "資料夾會先登記下來，第一次對這個專案送出修改任務時，才會連同 VERSION 檔一起建立並送 PR。",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);

        _nameBox.Width = 300;
        _nameBox.Margin = new Padding(0, 0, 0, 6);
        _nameBox.TextChanged += (_, _) => _error.Text = "";
        shell.Controls.Add(_nameBox, 0, 2);

        _error.AutoSize = true;
        _error.MaximumSize = new Size(460, 0);
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

        var ok = MakeButton("新增", 100, primary: true);
        ok.Margin = new Padding(0, 0, 10, 0);
        ok.Click += (_, _) => Confirm();
        buttons.Controls.Add(ok);
        AcceptButton = ok;

        var cancel = MakeButton("取消", 90, primary: false);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(cancel);
        CancelButton = cancel;

        shell.Controls.Add(buttons, 0, 4);
    }

    private void Confirm()
    {
        var name = _nameBox.Text.Trim();
        var problem = _validate(name);
        if (problem is not null)
        {
            _error.Text = problem;
            _nameBox.Focus();
            return;
        }

        FolderName = name;
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
