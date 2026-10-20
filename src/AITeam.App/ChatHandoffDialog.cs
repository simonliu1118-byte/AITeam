using System.Diagnostics;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 交給 ChatGPT 聊天做的那段時間，AITeam 就停在這個視窗上：給使用者交接文字、工作單連結，
/// 等使用者回來按「ChatGPT 已完成」。按下去之後 AITeam 會去 GitHub 檢查；沒通過的話，
/// 這個視窗會帶著原因再出現一次。
/// </summary>
public sealed class ChatHandoffDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);
    private static readonly Color WarnText = Color.FromArgb(176, 54, 54);

    private readonly string _handoffText;
    private readonly Label _copied = new();
    private bool _decided;

    public ChatHandoffAnswer Answer { get; private set; } = ChatHandoffAnswer.Cancel;

    public ChatHandoffDialog(ChatHandoffPrompt prompt)
    {
        _handoffText = prompt.HandoffText;

        Text = "AITeam - 交給 ChatGPT";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 520);
        Size = new Size(720, 600);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        MinimizeBox = true;
        ShowInTaskbar = true;

        BuildUi(prompt);
        FormClosing += OnFormClosing;
    }

    private static string StepTitle(ChatHandoffPrompt prompt) => prompt.Step switch
    {
        ChatHandoffStep.Implement => "實作",
        ChatHandoffStep.Repair => $"依審查意見修正（第 {prompt.Round} 輪）",
        _ => $"修正 CI 失敗（第 {prompt.Round} 輪）"
    };

    private void BuildUi(ChatHandoffPrompt prompt)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(20),
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 3; i++) shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 3; i++) shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        shell.Controls.Add(new Label
        {
            Text = $"交給 ChatGPT：{StepTitle(prompt)}",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        shell.Controls.Add(new Label
        {
            Text = "1. 按「複製交接文字」，貼給 ChatGPT。"
                   + Environment.NewLine + "2. 等 ChatGPT 改完、push 到分支"
                   + (prompt.Step == ChatHandoffStep.CiFix ? "。" : "，並把工作單的完成回報填好。")
                   + Environment.NewLine + "3. 回來按「ChatGPT 已完成」，AITeam 會去 GitHub 檢查，接著繼續審查。"
                   + Environment.NewLine + "等待期間請不要關掉 AITeam。",
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 1);

        if (prompt.Warning is { } warning)
        {
            shell.Controls.Add(new Label
            {
                Text = "還不能繼續：" + warning,
                AutoSize = true,
                MaximumSize = new Size(640, 0),
                ForeColor = WarnText,
                Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10)
            }, 0, 2);
        }

        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = CardBackground,
            ForeColor = PrimaryText,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            // WinForms 的 TextBox 只認 \r\n。
            Text = _handoffText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine)
        });
        shell.Controls.Add(card, 0, 3);

        var tools = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        var copy = MakeButton("複製交接文字", 130, primary: false);
        copy.Margin = new Padding(0, 0, 12, 0);
        copy.Click += (_, _) => CopyHandoff();
        tools.Controls.Add(copy);

        var link = new LinkLabel
        {
            Text = prompt.Step == ChatHandoffStep.CiFix ? "在 GitHub 打開分支" : "在 GitHub 打開工作單",
            AutoSize = true,
            LinkColor = Accent,
            Margin = new Padding(0, 9, 12, 0)
        };
        link.LinkClicked += (_, _) => OpenUrl(prompt.WorkOrderUrl);
        tools.Controls.Add(link);

        _copied.AutoSize = true;
        _copied.ForeColor = SecondaryText;
        _copied.Margin = new Padding(0, 9, 0, 0);
        tools.Controls.Add(_copied);
        shell.Controls.Add(tools, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = Padding.Empty
        };
        var done = MakeButton("ChatGPT 已完成", 150, primary: true);
        done.Margin = new Padding(0, 0, 10, 0);
        done.Click += (_, _) => Finish(ChatHandoffAnswer.Done);
        buttons.Controls.Add(done);

        var stop = MakeButton("停止任務", 100, primary: false);
        stop.Click += (_, _) =>
        {
            if (ConfirmStop()) Finish(ChatHandoffAnswer.Cancel);
        };
        buttons.Controls.Add(stop);
        shell.Controls.Add(buttons, 0, 5);
    }

    private void CopyHandoff()
    {
        try
        {
            Clipboard.SetText(_handoffText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
            _copied.Text = "已複製，貼到 ChatGPT 就可以了。";
        }
        catch (Exception ex)
        {
            _copied.Text = "複製失敗：" + TextSummary.OneLine(ex.Message, 80) + "，請直接選取上面的文字複製。";
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 打不開瀏覽器就算了，網址也在交接文字裡。
        }
    }

    private bool ConfirmStop() =>
        MessageBox.Show(
            this,
            "要停止這個任務嗎？" + Environment.NewLine + Environment.NewLine
            + "任務分支會留在 GitHub 上，ChatGPT 已經做的改動不會不見，但 AITeam 不會再接著審查和合併。",
            "AITeam",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>直接按右上角關掉，不能悄悄當成「停止任務」，要先問一聲。</summary>
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_decided) return;
        if (ConfirmStop())
        {
            Answer = ChatHandoffAnswer.Cancel;
            _decided = true;
            DialogResult = DialogResult.OK;
            return;
        }

        e.Cancel = true;
    }

    private void Finish(ChatHandoffAnswer answer)
    {
        Answer = answer;
        _decided = true;
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
