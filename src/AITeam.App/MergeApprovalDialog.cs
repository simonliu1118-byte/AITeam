using System.Diagnostics;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 高風險變更的人工合併關卡。以前這一關要使用者自己開瀏覽器去 GitHub 按，
/// 現在搬進 AITeam：判斷需要的東西（改了哪些檔案、CI 過了沒、審查怎麼說）一起攤在這裡，
/// 按下去才會合併。
/// </summary>
public sealed class MergeApprovalDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);

    private readonly MergeApprovalPrompt _prompt;

    public MergeDecision? Decision { get; private set; }

    public MergeApprovalDialog(MergeApprovalPrompt prompt)
    {
        _prompt = prompt;

        Text = $"AITeam - 要合併 PR #{prompt.PullNumber} 嗎？";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 580);
        Size = new Size(820, 660);
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
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 46F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 54F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        shell.Controls.Add(new Label
        {
            Text = "這是高風險變更，AITeam 不會自己合併，要你看過再決定。",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        shell.Controls.Add(BuildFactsCard(), 0, 1);
        shell.Controls.Add(BuildTextCard("改了哪些檔案", _prompt.ChangedFiles, monospaceFriendly: true), 0, 2);
        shell.Controls.Add(BuildTextCard("CI 檢查結果與審查結論", $"{_prompt.CiSummary}\r\n\r\n── 最終審查 ──\r\n{_prompt.ReviewSummary}", monospaceFriendly: false), 0, 3);
        shell.Controls.Add(BuildButtons(), 0, 4);
    }

    private Control BuildFactsCard()
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(14, 11, 14, 11),
            Margin = new Padding(0, 0, 0, 10)
        };

        var facts = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = CardBackground
        };
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        AddFact(facts, "專案", _prompt.ProjectName);
        AddFact(facts, "版本", _prompt.Version);
        AddFact(facts, "風險等級", _prompt.Risk);
        AddFact(facts, "把關強度", _prompt.ReviewMode);

        var link = new LinkLabel
        {
            Text = _prompt.PullUrl,
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 9F),
            LinkColor = Accent,
            Margin = new Padding(0, 3, 0, 2)
        };
        link.LinkClicked += (_, _) => OpenInBrowser(_prompt.PullUrl);
        facts.Controls.Add(MakeFactLabel("PR"), 0, facts.RowCount);
        facts.Controls.Add(link, 1, facts.RowCount);
        facts.RowCount++;

        card.Controls.Add(facts);
        return card;
    }

    private static void AddFact(TableLayoutPanel facts, string name, string value)
    {
        var row = facts.RowCount;
        facts.Controls.Add(MakeFactLabel(name), 0, row);
        facts.Controls.Add(new Label
        {
            Text = value,
            AutoSize = true,
            ForeColor = PrimaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 2)
        }, 1, row);
        facts.RowCount = row + 1;
    }

    private static Label MakeFactLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SecondaryText,
        Font = new Font("Microsoft JhengHei UI", 9F),
        Margin = new Padding(0, 4, 10, 2)
    };

    private static Control BuildTextCard(string title, string body, bool monospaceFriendly)
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = AppBackground,
            Margin = new Padding(0, 0, 0, 10)
        };
        wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        wrapper.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold),
            Margin = new Padding(2, 0, 0, 5)
        }, 0, 0);

        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(11)
        };
        card.Controls.Add(new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = CardBackground,
            ForeColor = PrimaryText,
            ScrollBars = monospaceFriendly ? ScrollBars.Both : ScrollBars.Vertical,
            WordWrap = !monospaceFriendly,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft JhengHei UI", 9F),
            Text = body
        });
        wrapper.Controls.Add(card, 0, 1);
        return wrapper;
    }

    private Control BuildButtons()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var open = MakeButton("在 GitHub 上開啟", 140, primary: false);
        open.Click += (_, _) => OpenInBrowser(_prompt.PullUrl);
        row.Controls.Add(open, 0, 0);

        var skip = MakeButton("暫不合併", 110, primary: false);
        skip.Margin = new Padding(0, 0, 10, 0);
        skip.Click += (_, _) => Decide(MergeDecision.Skip);
        row.Controls.Add(skip, 2, 0);

        var merge = MakeButton("合併這個 PR", 150, primary: true);
        merge.Click += (_, _) => Decide(MergeDecision.Merge);
        row.Controls.Add(merge, 3, 0);

        AcceptButton = merge;
        return row;
    }

    private void Decide(MergeDecision decision)
    {
        Decision = decision;
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

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"開啟失敗：{ex.Message}", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
