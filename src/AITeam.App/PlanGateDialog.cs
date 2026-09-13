using AITeam.Services;

namespace AITeam;

public sealed class PlanGateDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);

    private readonly PlanGatePrompt _prompt;
    private readonly TextBox _replyBox = new();

    public PlanGateResponse? Response { get; private set; }

    public PlanGateDialog(PlanGatePrompt prompt)
    {
        _prompt = prompt;

        Text = "AITeam - Plan Gate 討論";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 500);
        Size = new Size(700, 580);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;

        BuildUi();
    }

    private void BuildUi()
    {
        var isReady = _prompt.Stage == PlanGateStage.ReadyForConfirmation;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        var title = new Label
        {
            Text = isReady ? "Plan Gate 認為計畫已可定案，請確認：" : "Plan Gate 想先確認幾件事：",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 10)
        };
        shell.Controls.Add(title, 0, 0);

        var bodyCard = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(13),
            Margin = new Padding(0, 0, 0, 12)
        };
        var bodyBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = CardBackground,
            ForeColor = PrimaryText,
            Font = new Font("Microsoft JhengHei UI", 10.5F),
            Text = _prompt.Body
        };
        bodyCard.Controls.Add(bodyBox);
        shell.Controls.Add(bodyCard, 0, 1);

        var inputCard = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(11),
            Margin = new Padding(0, 0, 0, 12)
        };
        _replyBox.Multiline = true;
        _replyBox.ScrollBars = ScrollBars.Vertical;
        _replyBox.BorderStyle = BorderStyle.None;
        _replyBox.Dock = DockStyle.Fill;
        _replyBox.BackColor = CardBackground;
        _replyBox.ForeColor = PrimaryText;
        _replyBox.Font = new Font("Microsoft JhengHei UI", 10.5F);
        _replyBox.PlaceholderText = isReady
            ? "沒有的話可以直接按「確認，開始執行」；有想補充的可以寫在這裡…"
            : "在這裡輸入回答…";
        inputCard.Controls.Add(_replyBox);
        shell.Controls.Add(inputCard, 0, 2);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = AppBackground,
            Margin = Padding.Empty
        };

        var handOffButton = MakeGhostButton(isReady ? "交給 AI 全權判斷" : "交給 AI 全權判斷，直接定案");
        handOffButton.Click += (_, _) => Complete(PlanGateAction.HandOff);

        if (isReady)
        {
            var confirmButton = MakePrimaryButton("確認，開始執行");
            confirmButton.Click += (_, _) => Complete(PlanGateAction.Finalize);
            buttonRow.Controls.Add(confirmButton);

            var supplementButton = MakeGhostButton("補充後重新確認");
            supplementButton.Margin = new Padding(0, 0, 10, 0);
            supplementButton.Click += (_, _) => Complete(PlanGateAction.Reply);
            buttonRow.Controls.Add(supplementButton);
        }
        else
        {
            var sendButton = MakePrimaryButton("送出回答");
            sendButton.Click += (_, _) => Complete(PlanGateAction.Reply);
            buttonRow.Controls.Add(sendButton);
        }

        handOffButton.Margin = new Padding(0, 0, 10, 0);
        buttonRow.Controls.Add(handOffButton);

        shell.Controls.Add(buttonRow, 0, 3);
    }

    private Button MakePrimaryButton(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(140, 40),
        FlatStyle = FlatStyle.Flat,
        BackColor = Accent,
        ForeColor = Color.White,
        Font = new Font("Microsoft JhengHei UI", 10.5F, FontStyle.Bold),
        Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 }
    };

    private Button MakeGhostButton(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(170, 40),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.White,
        ForeColor = PrimaryText,
        Font = new Font("Microsoft JhengHei UI", 10F),
        Cursor = Cursors.Hand,
        FlatAppearance = { BorderColor = BorderColor }
    };

    private void Complete(PlanGateAction action)
    {
        var text = _replyBox.Text.Trim();
        if (action == PlanGateAction.Reply && text.Length == 0)
        {
            MessageBox.Show(
                this,
                "請先輸入內容，或改按其他按鈕。",
                "AITeam",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Response = new PlanGateResponse(action, text.Length == 0 ? null : text);
        DialogResult = DialogResult.OK;
        Close();
    }
}
