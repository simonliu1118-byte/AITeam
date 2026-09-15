using AITeam.Models;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 把會議結論交給修改管線之前的最後一關：確認要對哪個專案做、以及真正要送出的需求。
/// 會議可以不指定專案（純討論），所以這裡一定要能選；也要能當場去新增一個專案。
/// </summary>
public sealed class MeetingHandoffDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);

    private readonly ComboBox _projectBox = new();
    private readonly TextBox _requestBox = new();
    private readonly TextBox _conclusionBox = new();
    private readonly Button _manageButton = new();

    private IReadOnlyList<ProjectEntry> _projects;

    public ProjectEntry? Project { get; private set; }
    public string Request { get; private set; } = "";
    /// <summary>使用者實際同意送出去的那一份文字——他在下面那個框裡改過的版本。</summary>
    public string Conclusion { get; private set; }

    /// <summary>按「管理專案…」時交還給主畫面處理，回來時帶著最新的專案清單。</summary>
    public Func<IReadOnlyList<ProjectEntry>>? ManageProjects { get; set; }

    public MeetingHandoffDialog(MeetingConclusion conclusion, IReadOnlyList<ProjectEntry> projects)
    {
        Conclusion = conclusion.Text;
        _projects = projects;

        Text = "AITeam - 把會議結論送去執行";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 560);
        Size = new Size(760, 620);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        MinimizeBox = false;
        ShowInTaskbar = false;

        BuildUi(conclusion);
    }

    private void BuildUi(MeetingConclusion conclusion)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(18),
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 4; i++) shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        shell.Controls.Add(new Label
        {
            Text = "這個結論要對哪個專案執行？",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        var projectRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = AppBackground };
        projectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        projectRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _projectBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _projectBox.Dock = DockStyle.Fill;
        _projectBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _projectBox.Font = new Font("Microsoft JhengHei UI", 9.5F);
        _projectBox.MaxDropDownItems = 20;
        _projectBox.Margin = new Padding(0, 0, 10, 12);
        projectRow.Controls.Add(_projectBox, 0, 0);

        _manageButton.Text = "管理專案…";
        _manageButton.AutoSize = false;
        _manageButton.Size = new Size(110, _projectBox.PreferredHeight + 2);
        _manageButton.FlatStyle = FlatStyle.Flat;
        _manageButton.BackColor = Color.White;
        _manageButton.ForeColor = PrimaryText;
        _manageButton.FlatAppearance.BorderColor = BorderColor;
        _manageButton.Anchor = AnchorStyles.Left;
        _manageButton.Margin = new Padding(0, 0, 0, 12);
        _manageButton.Click += (_, _) => OpenProjectManager();
        projectRow.Controls.Add(_manageButton, 1, 0);
        shell.Controls.Add(projectRow, 0, 1);

        FillProjects(conclusion.ProjectName);

        shell.Controls.Add(MakeFieldLabel("要送出的需求（可以改寫成更明確的一句話）"), 0, 2);

        _requestBox.Dock = DockStyle.Fill;
        _requestBox.Multiline = true;
        _requestBox.ScrollBars = ScrollBars.Vertical;
        _requestBox.Font = new Font("Microsoft JhengHei UI", 10F);
        _requestBox.Margin = new Padding(0, 4, 0, 12);
        // 以前這裡直接塞整份結論，但結論本來就會當成背景一起送過去——
        // 等於同一段話讓 Planner 讀兩次。需求欄留一句短的就好，該說的在下面那份。
        _requestBox.Text = $"依照下面這份會議結論執行：{conclusion.Topic}";
        shell.Controls.Add(_requestBox, 0, 4);

        shell.Controls.Add(MakeFieldLabel(DescribeConclusion(conclusion)), 0, 3);

        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(11),
            Margin = new Padding(0, 4, 0, 12)
        };
        _conclusionBox.Multiline = true;
        // 這一份才是真正交給改程式的 AI 的東西，所以要讓使用者能當場改：
        // 刪掉不同意的、補上自己的決定。送出去的就是他看過並改過的版本。
        _conclusionBox.ReadOnly = false;
        _conclusionBox.BorderStyle = BorderStyle.None;
        _conclusionBox.BackColor = CardBackground;
        _conclusionBox.ForeColor = Color.FromArgb(55, 62, 70);
        _conclusionBox.ScrollBars = ScrollBars.Vertical;
        _conclusionBox.Dock = DockStyle.Fill;
        _conclusionBox.Font = new Font("Microsoft JhengHei UI", 9F);
        _conclusionBox.Text = conclusion.Text;
        card.Controls.Add(_conclusionBox);
        shell.Controls.Add(card, 0, 5);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var cancel = MakeButton("取消", 100, primary: false);
        cancel.Margin = new Padding(0, 0, 10, 0);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(cancel, 1, 0);

        var send = MakeButton("送出任務", 130, primary: true);
        send.Click += (_, _) => Confirm();
        buttons.Controls.Add(send, 2, 0);
        AcceptButton = send;

        shell.Controls.Add(buttons, 0, 6);
    }

    private void FillProjects(string? preferredName)
    {
        _projectBox.Items.Clear();
        foreach (var project in _projects) _projectBox.Items.Add(project.Name);

        if (_projectBox.Items.Count == 0) return;

        var index = 0;
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            for (var i = 0; i < _projects.Count; i++)
            {
                if (!_projects[i].Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase)) continue;
                index = i;
                break;
            }
        }
        _projectBox.SelectedIndex = index;
    }

    private void OpenProjectManager()
    {
        if (ManageProjects is null) return;

        var chosen = _projectBox.SelectedIndex >= 0 ? _projects[_projectBox.SelectedIndex].Name : null;
        _projects = ManageProjects();
        FillProjects(chosen);
    }

    private void Confirm()
    {
        if (_projectBox.SelectedIndex < 0 || _projects.Count == 0)
        {
            MessageBox.Show("請先選一個專案；還沒有登錄專案的話，按「管理專案…」新增一個。",
                "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var request = _requestBox.Text.Trim();
        if (request.Length == 0)
        {
            MessageBox.Show("請填寫要送出的需求。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var conclusion = _conclusionBox.Text.Trim();
        if (conclusion.Length == 0)
        {
            MessageBox.Show("會議結論不能是空的——那是接手的 AI 唯一看得到的背景。",
                "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Project = _projects[_projectBox.SelectedIndex];
        Request = request;
        Conclusion = conclusion;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 標題要說實話。沒有收斂過的會議送出去時，使用者一定要看得出來下面那份東西
    /// 是三個可能互相打架的立場，不是定案。
    /// </summary>
    private static string DescribeConclusion(MeetingConclusion conclusion) =>
        conclusion.Kind == MeetingConclusionKind.Decision
            ? $"定案書（由 {conclusion.Writer?.ToFriendlyName() ?? "AI"} 整理）——可以直接修改，送出去的就是這一份"
            : "⚠ 這場會議沒有收斂出定案，下面只是每位最後的立場，可能互相矛盾——可以直接修改";

    private static Label MakeFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SecondaryText,
        Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold),
        Margin = new Padding(2, 0, 0, 0)
    };

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
