using AITeam.Models;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// GPT / Codex 在實作途中額度用完時，問使用者要怎麼接續。
/// 平常的分工是 GPT 實作；換成別家要花更貴的額度，改人工接續要多花使用者的工夫，
/// 兩個都有代價，所以不替使用者決定。
/// </summary>
public sealed class ImplementerExhaustedDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);

    public ExhaustedChoice Choice { get; private set; } = ExhaustedChoice.Stop;

    public ImplementerExhaustedDialog(ImplementerExhaustedPrompt prompt)
    {
        Text = "AITeam - GPT / Codex 額度用完";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(600, 420);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi(prompt);
    }

    private void BuildUi(ImplementerExhaustedPrompt prompt)
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
            Text = $"GPT / Codex 的額度用完了，「{prompt.Step}」要怎麼接續？",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        shell.Controls.Add(new Label
        {
            Text = $"CLI 的訊息：{TextSummary.OneLine(prompt.Detail, 220)}",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9F),
            Margin = new Padding(0, 0, 0, 14)
        }, 0, 1);

        var fallbackName = prompt.Fallback?.ToFriendlyName();
        shell.Controls.Add(new Label
        {
            Text =
                (fallbackName is null
                    ? "● 改用備援：目前沒有其他 AI 可以接手。"
                    : $"● 改用備援：由 {fallbackName} 接手，全程自動，但會用到 {fallbackName} 的額度。")
                + Environment.NewLine
                + "● 改用 ChatGPT 人工接續：AITeam 把工作單放到 GitHub 的任務分支，給你一段交接文字貼給 ChatGPT。"
                + "它做完之後你回來按「ChatGPT 已完成」，挑戰、審查、CI、合併照常進行。這個任務之後的修正也會用同樣的方式交接。"
                + Environment.NewLine
                + "● 停止任務：工作區保留，額度恢復之後可以重新送出。",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = PrimaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            Margin = new Padding(0, 0, 0, 16)
        }, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty
        };

        if (prompt.Fallback is { } fallback)
        {
            var useFallback = MakeButton($"改用 {fallback.ToFriendlyName()}", 150, primary: true);
            useFallback.Margin = new Padding(0, 0, 10, 0);
            useFallback.Click += (_, _) => Finish(ExhaustedChoice.Fallback);
            buttons.Controls.Add(useFallback);
        }

        var chat = MakeButton("改用 ChatGPT 人工接續", 190, primary: prompt.Fallback is null);
        chat.Margin = new Padding(0, 0, 10, 0);
        chat.Click += (_, _) => Finish(ExhaustedChoice.ChatHandoff);
        buttons.Controls.Add(chat);

        var stop = MakeButton("停止任務", 100, primary: false);
        stop.Click += (_, _) => Finish(ExhaustedChoice.Stop);
        buttons.Controls.Add(stop);
        CancelButton = stop;

        shell.Controls.Add(buttons, 0, 3);
    }

    private void Finish(ExhaustedChoice choice)
    {
        Choice = choice;
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
