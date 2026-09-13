using System.Drawing.Drawing2D;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 歷史任務清單。左邊是任務摘要（只讀索引，開得很快），點某一筆才去讀那一筆的
/// 完整執行紀錄顯示在右邊。
/// </summary>
public sealed class TaskHistoryForm : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);
    private static readonly Color SelectedItemBackground = Color.FromArgb(234, 241, 248);
    private static readonly Color Danger = Color.FromArgb(176, 54, 54);
    private static readonly Color DangerBorder = Color.FromArgb(227, 195, 195);

    private static readonly Font SubjectFont = new("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
    private static readonly Font MetaFont = new("Microsoft JhengHei UI", 8.5F);

    private readonly TaskHistoryService _history;
    private readonly ListBox _list = new();
    private readonly Label _detailTitle = new();
    private readonly Label _detailMeta = new();
    private readonly TextBox _resultBox = new();
    private readonly RichTextBox _logBox = new();
    private readonly LinkFoldingLog _logView;
    private readonly Button _deleteButton = new();

    private IReadOnlyList<TaskHistoryEntry> _entries = Array.Empty<TaskHistoryEntry>();

    public TaskHistoryForm(TaskHistoryService history)
    {
        _history = history;
        _logView = new LinkFoldingLog(_logBox);

        Text = "AITeam - 歷史任務";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 600);
        Size = new Size(1060, 700);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
        LoadHistory();
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 2,
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(shell);

        shell.Controls.Add(new Label
        {
            Text = "歷史任務",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 15F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);

        var actions = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 2, 12)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        ConfigureFlatButton(_deleteButton, "刪除這筆", 100);
        _deleteButton.ForeColor = Danger;
        _deleteButton.FlatAppearance.BorderColor = DangerBorder;
        _deleteButton.Margin = new Padding(0, 0, 8, 0);
        _deleteButton.Click += (_, _) => DeleteSelected();
        actions.Controls.Add(_deleteButton, 0, 0);

        var close = new Button();
        ConfigureFlatButton(close, "關閉", 90);
        close.Click += (_, _) => Close();
        actions.Controls.Add(close, 1, 0);
        shell.Controls.Add(actions, 1, 0);

        shell.Controls.Add(BuildListPanel(), 0, 1);
        shell.Controls.Add(BuildDetailPanel(), 1, 1);
    }

    private Control BuildListPanel()
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 14, 0)
        };

        _list.Dock = DockStyle.Fill;
        _list.BorderStyle = BorderStyle.None;
        _list.IntegralHeight = false;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 54;
        _list.DrawItem += DrawHistoryItem;
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        card.Controls.Add(_list);
        return card;
    }

    private Control BuildDetailPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AppBackground
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 8) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _detailTitle.AutoSize = false;
        _detailTitle.Dock = DockStyle.Top;
        _detailTitle.Height = 26;
        _detailTitle.AutoEllipsis = true;
        _detailTitle.Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold);
        _detailTitle.ForeColor = PrimaryText;
        _detailTitle.Text = "請從左邊選一筆任務";
        _detailMeta.AutoSize = false;
        _detailMeta.Dock = DockStyle.Top;
        _detailMeta.Height = 20;
        _detailMeta.AutoEllipsis = true;
        _detailMeta.Font = MetaFont;
        _detailMeta.ForeColor = SecondaryText;
        header.Controls.Add(_detailTitle, 0, 0);
        header.Controls.Add(_detailMeta, 0, 1);
        layout.Controls.Add(header, 0, 0);

        layout.Controls.Add(MakeSectionLabelledCard("結果摘要", _resultBox, isLog: false), 0, 1);
        layout.Controls.Add(MakeSectionLabelledCard("完整執行紀錄", _logBox, isLog: true), 0, 2);
        return layout;
    }

    private Control MakeSectionLabelledCard(string title, Control content, bool isLog)
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = AppBackground,
            Margin = new Padding(0, 0, 2, isLog ? 0 : 12)
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
            Margin = new Padding(2, 0, 0, 6)
        }, 0, 0);

        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(11)
        };

        content.Dock = DockStyle.Fill;
        content.BackColor = CardBackground;
        content.ForeColor = isLog ? Color.FromArgb(55, 62, 70) : PrimaryText;
        content.Font = new Font("Microsoft JhengHei UI", 9F);
        if (content is TextBox textBox)
        {
            textBox.Multiline = true;
            textBox.ReadOnly = true;
            textBox.BorderStyle = BorderStyle.None;
            textBox.ScrollBars = ScrollBars.Vertical;
        }
        else if (content is RichTextBox richTextBox)
        {
            richTextBox.ReadOnly = true;
            richTextBox.BorderStyle = BorderStyle.None;
            richTextBox.DetectUrls = false;
        }
        card.Controls.Add(content);
        wrapper.Controls.Add(card, 0, 1);
        return wrapper;
    }

    private static void ConfigureFlatButton(Button button, string text, int width)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Size = new Size(width, 32);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.White;
        button.ForeColor = PrimaryText;
        button.FlatAppearance.BorderColor = BorderColor;
        button.Margin = Padding.Empty;
    }

    private void LoadHistory(int preferredIndex = 0)
    {
        _entries = _history.Load();
        _list.Items.Clear();
        foreach (var entry in _entries) _list.Items.Add(entry);

        if (_entries.Count == 0)
        {
            _deleteButton.Enabled = false;
            _detailTitle.Text = "還沒有任何歷史任務";
            _detailMeta.Text = "送出第一個任務之後，這裡就會留下紀錄。";
            _resultBox.Text = "";
            _logView.Clear();
            return;
        }

        _deleteButton.Enabled = true;
        // 先清掉選取再指定，確保 SelectedIndexChanged 一定會觸發；
        // 刪掉一筆之後索引數字可能沒變，不這樣做右邊會停在已經被刪掉的內容上。
        _list.ClearSelected();
        _list.SelectedIndex = Math.Clamp(preferredIndex, 0, _entries.Count - 1);
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not TaskHistoryEntry entry) return;

        var subject = string.IsNullOrWhiteSpace(entry.Subject) ? entry.Request : entry.Subject;
        var answer = MessageBox.Show(
            $"確定要刪除這筆歷史任務？\r\n\r\n{subject}\r\n\r\n連同它的完整執行紀錄一起刪除，且無法復原。",
            "刪除歷史任務",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        // 刪掉之後停在同一個位置，讓使用者可以連續刪除而不用每次重新找。
        var index = _list.SelectedIndex;
        _history.Delete(entry.Id);
        LoadHistory(index);
    }

    private void ShowSelected()
    {
        if (_list.SelectedItem is not TaskHistoryEntry entry) return;

        _detailTitle.Text = entry.Subject;
        _detailMeta.Text =
            $"{entry.ProjectName} · {DescribeOutcome(entry.Outcome)} · " +
            $"{entry.StartedAt:yyyy-MM-dd HH:mm} · 耗時 {FormatDuration(entry.Duration)}";
        _resultBox.Text = string.IsNullOrWhiteSpace(entry.Result) ? "（沒有結果摘要。）" : entry.Result;

        // 完整 log 只在這時候才從檔案讀進來。
        _logView.SetText(_history.LoadLog(entry.Id));
        _logView.ScrollToTop();
    }

    private void DrawHistoryItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        if (_list.Items[e.Index] is not TaskHistoryEntry entry) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(CardBackground))
            e.Graphics.FillRectangle(background, e.Bounds);

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var card = new Rectangle(e.Bounds.X, e.Bounds.Y + 1, Math.Max(1, e.Bounds.Width - 2), Math.Max(1, e.Bounds.Height - 4));
        if (selected)
        {
            using var path = CreateRoundedPath(card, 7);
            using var fill = new SolidBrush(SelectedItemBackground);
            e.Graphics.FillPath(fill, path);
        }

        var dot = new Rectangle(card.X + 10, card.Y + 12, 8, 8);
        using (var dotBrush = new SolidBrush(OutcomeColor(entry.Outcome)))
            e.Graphics.FillEllipse(dotBrush, dot);

        var textLeft = dot.Right + 8;
        var textWidth = card.Right - textLeft - 8;
        TextRenderer.DrawText(
            e.Graphics,
            string.IsNullOrWhiteSpace(entry.Subject) ? entry.Request : entry.Subject,
            SubjectFont,
            new Rectangle(textLeft, card.Y + 7, textWidth, 20),
            selected ? Accent : PrimaryText,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics,
            $"{entry.StartedAt:MM-dd HH:mm} · {DescribeOutcome(entry.Outcome)} · {FormatDuration(entry.Duration)}",
            MetaFont,
            new Rectangle(textLeft, card.Y + 28, textWidth, 18),
            SecondaryText,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private static Color OutcomeColor(TaskOutcome outcome) => outcome switch
    {
        TaskOutcome.Completed => Color.FromArgb(46, 160, 92),
        TaskOutcome.Failed => Color.FromArgb(194, 58, 52),
        _ => Color.FromArgb(145, 153, 163)
    };

    private static string DescribeOutcome(TaskOutcome outcome) => outcome switch
    {
        TaskOutcome.Completed => "已完成",
        TaskOutcome.Failed => "失敗",
        _ => "已取消"
    };

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (rect.Width <= d || rect.Height <= d)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
