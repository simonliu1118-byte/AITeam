using AITeam.Services;

namespace AITeam;

/// <summary>
/// 實作完成後的確認點。讓使用者在審查開始前看到「改了哪些檔案」，
/// 可以直接繼續、補充意見後繼續，或是中止整個任務。
/// </summary>
public sealed class CheckpointDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);
    private static readonly Color Danger = Color.FromArgb(176, 54, 54);

    private readonly CheckpointPrompt _prompt;
    private readonly TextBox _noteBox = new();

    public CheckpointResponse? Response { get; private set; }

    public CheckpointDialog(CheckpointPrompt prompt)
    {
        _prompt = prompt;

        Text = "AITeam - 實作完成，請確認";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(620, 480);
        Size = new Size(700, 540);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        MinimizeBox = false;
        ShowInTaskbar = false;

        BuildUi();
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        shell.Controls.Add(new Label
        {
            Text = _prompt.Title,
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        var summaryBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = CardBackground,
            ForeColor = PrimaryText,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft JhengHei UI", 9F),
            Text = _prompt.Summary
        };
        var summaryCard = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };
        summaryCard.Controls.Add(summaryBox);
        shell.Controls.Add(summaryCard, 0, 1);

        shell.Controls.Add(new Label
        {
            Text = "要補充什麼嗎？（選填；填了之後每一步都會帶著它）",
            AutoSize = true,
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold),
            Margin = new Padding(2, 0, 0, 6)
        }, 0, 2);

        _noteBox.Multiline = true;
        _noteBox.Dock = DockStyle.Fill;
        _noteBox.ScrollBars = ScrollBars.Vertical;
        _noteBox.Font = new Font("Microsoft JhengHei UI", 10F);
        _noteBox.Margin = new Padding(0, 0, 0, 12);
        shell.Controls.Add(_noteBox, 0, 3);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var abort = MakeButton("中止任務", 110, primary: false);
        abort.ForeColor = Danger;
        abort.Click += (_, _) =>
        {
            Response = new CheckpointResponse(CheckpointAction.Abort);
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(abort, 1, 0);

        var proceed = MakeButton("繼續進入審查", 150, primary: true);
        proceed.Margin = new Padding(10, 0, 0, 0);
        proceed.Click += (_, _) =>
        {
            Response = new CheckpointResponse(CheckpointAction.Continue, _noteBox.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(proceed, 2, 0);

        shell.Controls.Add(buttons, 0, 4);
        AcceptButton = proceed;
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
