using AITeam.Models;
using AITeam.Services;

namespace AITeam;

public enum DecisionGateChoice
{
    Cancel,
    /// <summary>先請一家 AI 把會議寫成定案書，再送去執行。</summary>
    Draft,
    /// <summary>不收斂，直接把目前的討論摘要送出去。</summary>
    SendAsIs
}

/// <summary>
/// 按「送去執行」時擋一下。
///
/// 會議談到哪裡算談完，只有使用者自己認定；但「你覺得談完了」跟「這份東西適合交給
/// 會直接改程式的 AI」是兩件事。沒有這一關的話，送出去的是每家最後一則發言壓成一行、
/// 砍到 220 字並排的東西——三家意見不同時那是三段互相打架的話，而且我們還會跟 Planner
/// 說那是「使用者確認過的定案」。
///
/// 這裡不硬擋：有時候第一輪就講得很清楚了，硬要多跑一次只是多花時間。
/// </summary>
public sealed class DecisionGateDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);

    private readonly ComboBox _writerBox = new();
    private readonly IReadOnlyList<ProviderId> _candidates;

    public DecisionGateChoice Choice { get; private set; } = DecisionGateChoice.Cancel;
    public ProviderId Writer { get; private set; }

    public DecisionGateDialog(IReadOnlyList<ProviderId> participants)
    {
        _candidates = OrderCandidates(participants);
        Writer = _candidates.Count > 0 ? _candidates[0] : ProviderId.Claude;

        Text = "AITeam - 送去執行";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 330);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
    }

    /// <summary>
    /// 誰適合寫定案書。這是「讀完整場中文討論、輸出結構化中文」的工作，三家都做得到，
    /// 差別在速度：Antigravity 光啟動就比另外兩家慢很多，放在使用者等著送出的路徑上不合適。
    /// 使用者隨時可以在下拉選單裡換人。
    /// </summary>
    internal static IReadOnlyList<ProviderId> OrderCandidates(IReadOnlyList<ProviderId> participants)
    {
        var preference = new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Antigravity };
        return preference.Where(participants.Contains).ToList();
    }

    private void BuildUi()
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
            Text = "要先請 AI 把這場會議寫成一份定案書嗎？",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        shell.Controls.Add(new Label
        {
            Text =
                "不寫的話，送出去的是「每位 AI 最後一段話各砍成一行」——三家意見不同時，"
                + "接手改程式的 AI 會拿到三段互相打架的話，而且沒有任何一句告訴它最後決定怎麼做。"
                + Environment.NewLine + Environment.NewLine
                + "定案書會寫成五段：要做什麼、為什麼、具體做法、明確不做的事、還沒決定的事。"
                + "最後那一段很重要——會議談到一半就送出時，沒談完的部分會被老實列出來，"
                + "接手的 AI 就不會把它們當成已經決定好的事。"
                + Environment.NewLine + Environment.NewLine
                + "成本是一次 AI 呼叫（比「請 AI 收斂結論」的三次還少）。寫完你還可以直接修改。",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            Margin = new Padding(0, 0, 0, 14)
        }, 0, 1);

        var writerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = AppBackground,
            Margin = new Padding(0, 0, 0, 16)
        };
        writerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        writerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        writerRow.Controls.Add(new Label
        {
            Text = "由誰整理：",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = PrimaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
            Margin = new Padding(0, 0, 10, 0)
        }, 0, 0);

        _writerBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _writerBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _writerBox.Font = new Font("Microsoft JhengHei UI", 9.5F);
        _writerBox.Margin = new Padding(0);
        foreach (var candidate in _candidates) _writerBox.Items.Add(candidate.ToFriendlyName());
        if (_writerBox.Items.Count > 0) _writerBox.SelectedIndex = 0;
        _writerBox.Enabled = _writerBox.Items.Count > 1;
        _writerBox.SelectedIndexChanged += (_, _) =>
        {
            if (_writerBox.SelectedIndex >= 0) Writer = _candidates[_writerBox.SelectedIndex];
        };
        writerRow.Controls.Add(_writerBox, 1, 0);
        shell.Controls.Add(writerRow, 0, 2);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var draft = MakeButton("寫定案書再送", 150, primary: true);
        draft.Margin = new Padding(0, 0, 10, 0);
        draft.Enabled = _candidates.Count > 0;
        draft.Click += (_, _) => Finish(DecisionGateChoice.Draft);
        buttons.Controls.Add(draft, 0, 0);
        AcceptButton = draft;

        var asIs = MakeButton("不用，直接送", 130, primary: false);
        asIs.Margin = new Padding(0, 0, 10, 0);
        asIs.Click += (_, _) => Finish(DecisionGateChoice.SendAsIs);
        buttons.Controls.Add(asIs, 1, 0);

        var cancel = MakeButton("取消", 90, primary: false);
        cancel.Anchor = AnchorStyles.Left;
        cancel.Click += (_, _) => Finish(DecisionGateChoice.Cancel);
        buttons.Controls.Add(cancel, 2, 0);
        CancelButton = cancel;

        shell.Controls.Add(buttons, 0, 3);
    }

    private void Finish(DecisionGateChoice choice)
    {
        Choice = choice;
        DialogResult = choice == DecisionGateChoice.Cancel ? DialogResult.Cancel : DialogResult.OK;
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
