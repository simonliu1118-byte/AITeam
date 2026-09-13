using AITeam.Models;
using AITeam.Services;

namespace AITeam;

public sealed class ProjectManagerForm : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);
    private static readonly Color Danger = Color.FromArgb(176, 54, 54);
    private static readonly Color DangerBorder = Color.FromArgb(227, 195, 195);
    private static readonly Color MetaBackground = Color.FromArgb(247, 249, 251);

    private readonly ProjectRegistryService _registry;
    private readonly KnownRepositoryService _knownRepos;
    private readonly GitRepositoryService _git;
    private readonly CancellationTokenSource _cts = new();

    private readonly ListBox _projectList = new();
    private readonly TextBox _nameBox = new();
    private readonly ComboBox _repoBox = new();
    private readonly Label _repoInfo = new();
    private readonly TreeView _tree = new();
    private readonly Label _selectedLabel = new();
    private readonly Button _loadButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _newButton = new();

    private ProjectEntry? _editing;
    private RepoTreeSnapshot? _snapshot;
    private string _selectedSubpath = "";
    private bool _changed;
    private bool _suppressSelection;

    public string? SavedProjectName { get; private set; }

    public ProjectManagerForm(string runtimeRoot, ProjectRegistryService registry, ProjectEntry? selectedProject)
    {
        _registry = registry;
        _knownRepos = new KnownRepositoryService(runtimeRoot);
        _git = new GitRepositoryService(runtimeRoot, new ProcessRunner());

        Text = "AITeam - 專案管理";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(940, 680);
        Size = new Size(1020, 760);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
        PopulateKnownRepos();
        RefreshProjectList(selectedProject?.Name);
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = AppBackground
        };
        // 單欄 TableLayoutPanel 必須明確指定 Percent 欄寬，否則該欄預設 AutoSize，
        // 內容多寬就撐多寬、視窗變窄時不會縮，整塊內容會溢出右緣被裁掉。
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(shell);

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            Text = "專案管理",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 15F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 14)
        }, 0, 0);

        ConfigurePrimaryButton(_newButton, "＋ 新增專案", 120);
        _newButton.Click += (_, _) => StartNewProject();
        header.Controls.Add(_newButton, 1, 0);
        shell.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AppBackground,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.Controls.Add(content, 0, 1);

        content.Controls.Add(BuildProjectListPanel(), 0, 0);
        content.Controls.Add(BuildEditorPanel(), 1, 0);
    }

    private Control BuildProjectListPanel()
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 14, 0)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            Text = "已登錄專案",
            AutoSize = true,
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold),
            Margin = new Padding(2, 0, 0, 8)
        }, 0, 0);

        _projectList.Dock = DockStyle.Fill;
        _projectList.BorderStyle = BorderStyle.None;
        _projectList.IntegralHeight = false;
        _projectList.Margin = new Padding(0, 0, 0, 8);
        _projectList.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSelection) return;
            if (_projectList.SelectedItem is ProjectEntry entry) LoadProject(entry);
        };
        panel.Controls.Add(_projectList, 0, 1);

        var foot = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        var divider = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Color.FromArgb(238, 241, 244), Margin = new Padding(0, 8, 0, 8) };
        foot.Controls.Add(divider, 0, 0);
        foot.Controls.Add(new Label
        {
            Text = "新增專案不會覆蓋既有專案；選取清單項目後才會進入編輯模式。",
            AutoSize = true,
            MaximumSize = new Size(195, 0),
            Font = new Font("Microsoft JhengHei UI", 8.5F),
            ForeColor = SecondaryText
        }, 0, 1);
        panel.Controls.Add(foot, 0, 2);

        card.Controls.Add(panel);
        return card;
    }

    private Control BuildEditorPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 0, 0, 0),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = AppBackground
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildBasicInfoSection(), 0, 0);
        root.Controls.Add(BuildRepoSection(), 0, 1);
        root.Controls.Add(BuildLocationSection(), 0, 2);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 12, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.Controls.Add(BuildVersionRuleReferenceSection(), 0, 0);
        bottom.Controls.Add(BuildFooter(), 0, 1);
        root.Controls.Add(bottom, 0, 3);

        return root;
    }

    private Control BuildBasicInfoSection()
    {
        var card = MakeSectionCard(out var body, extraRows: 1);
        body.Controls.Add(SectionHeader(1, "基本資訊"), 0, 0);
        body.Controls.Add(MakeFieldLabel("專案名稱"), 0, 1);
        _nameBox.Dock = DockStyle.Top;
        _nameBox.Margin = new Padding(0, 5, 0, 0);
        body.Controls.Add(_nameBox, 0, 2);
        return card;
    }

    private Control BuildRepoSection()
    {
        var card = MakeSectionCard(out var body, extraRows: 2);
        card.Margin = new Padding(0, 12, 2, 0);
        body.Controls.Add(SectionHeader(2, "GitHub Repo"), 0, 0);
        body.Controls.Add(MakeFieldLabel("可選已知 Repo，或直接輸入 owner/repo"), 0, 1);

        var repoRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 5, 0, 0) };
        repoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        repoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _repoBox.Font = Font;
        _repoBox.Dock = DockStyle.Fill;
        _repoBox.DropDownStyle = ComboBoxStyle.DropDown;
        _repoBox.Margin = new Padding(0, 0, 10, 0);
        ConfigureSecondaryButton(_loadButton, "載入 Repo", 104);
        _loadButton.Height = _repoBox.PreferredHeight + 2;
        _loadButton.Click += async (_, _) => await LoadRepoAsync();
        repoRow.Controls.Add(_repoBox, 0, 0);
        repoRow.Controls.Add(_loadButton, 1, 0);
        body.Controls.Add(repoRow, 0, 2);

        var metaPanel = new Panel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            BackColor = MetaBackground,
            Padding = new Padding(11, 8, 11, 8),
            Margin = new Padding(0, 10, 0, 0)
        };
        _repoInfo.AutoSize = true;
        _repoInfo.ForeColor = SecondaryText;
        _repoInfo.Font = new Font("Microsoft JhengHei UI", 8.5F);
        metaPanel.Controls.Add(_repoInfo);
        body.Controls.Add(metaPanel, 0, 3);

        return card;
    }

    private Control BuildLocationSection()
    {
        var card = MakeSectionCard(out var body, extraRows: 2);
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 12, 2, 0);
        body.RowStyles[body.RowCount - 1] = new RowStyle(SizeType.Percent, 100F);

        body.Controls.Add(SectionHeader(3, "專案在 Repo 內的位置"), 0, 0);
        body.Controls.Add(new Label
        {
            Text = "整個 Repo 就是一個專案時，選「Repo 根目錄」。",
            AutoSize = true,
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 8.5F),
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 1);

        _selectedLabel.AutoSize = true;
        _selectedLabel.Dock = DockStyle.Top;
        _selectedLabel.BackColor = MetaBackground;
        _selectedLabel.Padding = new Padding(11, 7, 11, 7);
        _selectedLabel.ForeColor = PrimaryText;
        _selectedLabel.Font = new Font("Microsoft JhengHei UI", 9F);
        _selectedLabel.Margin = new Padding(0, 0, 0, 8);
        body.Controls.Add(_selectedLabel, 0, 2);

        _tree.Dock = DockStyle.Fill;
        _tree.BorderStyle = BorderStyle.FixedSingle;
        _tree.HideSelection = false;
        _tree.Margin = Padding.Empty;
        _tree.AfterSelect += (_, e) =>
        {
            _selectedSubpath = e.Node.Tag as string ?? "";
            RefreshSelectedLabel();
        };
        body.Controls.Add(_tree, 0, 3);

        return card;
    }

    private Control BuildVersionRuleReferenceSection()
    {
        var card = MakeSectionCard(out var body);
        card.Margin = new Padding(0, 0, 2, 0);

        var toggle = new Label
        {
            AutoSize = true,
            ForeColor = Accent,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft JhengHei UI", 9F),
            Text = "▸ 版本升號規則（參考，依共用規則自動處理，不需手動設定）"
        };
        body.Controls.Add(toggle, 0, 0);

        var refBody = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Visible = false,
            Margin = new Padding(0, 10, 0, 0),
            BackColor = MetaBackground,
            Padding = new Padding(11, 9, 11, 9),
            ForeColor = SecondaryText,
            Font = new Font("Microsoft JhengHei UI", 8.5F),
            Text =
                "‧ 版本檔一律是專案根目錄下的 VERSION（內容 X.Y.Z），AITeam 自動讀寫，不可另外指定路徑。\r\n" +
                "‧ 建議 tag 一律自動產生為「專案名稱-vX.Y.Z」，合併完成後才由使用者自行決定要不要正式打 tag／發 Release。\r\n" +
                "‧ 合併 PR 一律使用 merge commit，不提供 squash／rebase 選擇。\r\n" +
                "‧ Z 版號：開始新的獨立修改項目時遞增；同一項目還沒修好、繼續修正時改為 Build 遞增，不再升 Z。\r\n" +
                "‧ Y 版號：有明顯新功能／完整功能階段時升版，Z、Build 歸零。X（大版）只由使用者決定。"
        };
        toggle.Click += (_, _) =>
        {
            refBody.Visible = !refBody.Visible;
            toggle.Text = refBody.Visible
                ? "▾ 版本升號規則（參考，依共用規則自動處理，不需手動設定）"
                : "▸ 版本升號規則（參考，依共用規則自動處理，不需手動設定）";
        };

        body.Controls.Add(refBody, 0, 1);
        return card;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        ConfigureSecondaryButton(_removeButton, "移除登錄", 100);
        _removeButton.ForeColor = Danger;
        _removeButton.FlatAppearance.BorderColor = DangerBorder;
        _removeButton.Click += (_, _) => RemoveRegistration();
        footer.Controls.Add(_removeButton, 0, 0);

        var close = MakeSecondaryButton("完成", 90);
        close.Margin = new Padding(8, 0, 0, 0);
        close.Click += (_, _) =>
        {
            DialogResult = _changed ? DialogResult.OK : DialogResult.Cancel;
            Close();
        };
        footer.Controls.Add(close, 1, 0);

        ConfigurePrimaryButton(_saveButton, "儲存", 100);
        _saveButton.Margin = new Padding(8, 0, 0, 0);
        _saveButton.Click += async (_, _) => await SaveAsync();
        footer.Controls.Add(_saveButton, 2, 0);

        return footer;
    }

    private static RoundedCard MakeSectionCard(out TableLayoutPanel body, int extraRows = 0)
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(18, 15, 18, 15)
        };
        body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2 + extraRows,
            BackColor = Color.White
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < body.RowCount; i++)
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(body);
        return card;
    }

    private static Control SectionHeader(int number, string title)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.Controls.Add(new CircleBadge(number.ToString()) { Margin = new Padding(0, 1, 8, 0) }, 0, 0);
        row.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 10.5F, FontStyle.Bold),
            ForeColor = PrimaryText
        }, 1, 0);
        return row;
    }

    private sealed class CircleBadge : Label
    {
        public CircleBadge(string number)
        {
            Text = number;
            AutoSize = false;
            Size = new Size(20, 20);
            TextAlign = ContentAlignment.MiddleCenter;
            ForeColor = Color.White;
            Font = new Font("Microsoft JhengHei UI", 8.5F, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Accent);
            e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private void RefreshProjectList(string? preferredName = null)
    {
        _suppressSelection = true;
        try
        {
            var projects = _registry.Load();
            _projectList.Items.Clear();
            foreach (var project in projects) _projectList.Items.Add(project);

            if (_projectList.Items.Count == 0)
            {
                StartNewProject(clearSelection: false);
                return;
            }

            var target = preferredName;
            var index = 0;
            if (!string.IsNullOrWhiteSpace(target))
            {
                for (var i = 0; i < _projectList.Items.Count; i++)
                {
                    if ((_projectList.Items[i] as ProjectEntry)?.Name.Equals(target, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        index = i;
                        break;
                    }
                }
            }
            _projectList.SelectedIndex = index;
            if (_projectList.SelectedItem is ProjectEntry entry) LoadProject(entry);
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private void PopulateKnownRepos()
    {
        _repoBox.Items.Clear();
        foreach (var repo in _knownRepos.Load(_registry.Load()))
            if (_repoBox.Items.IndexOf(repo) < 0) _repoBox.Items.Add(repo);
    }

    private void LoadProject(ProjectEntry entry)
    {
        _editing = entry;
        _snapshot = null;
        _nameBox.Text = entry.Name;
        _repoBox.Text = entry.GitHubRepo;
        _selectedSubpath = entry.RepoSubpath ?? "";
        _repoInfo.Text = $"Repo：{entry.GitHubRepo}\r\n本機位置：{entry.PhysicalPath}";
        _tree.Nodes.Clear();
        _selectedLabel.Text = string.IsNullOrWhiteSpace(_selectedSubpath)
            ? "目前選擇：Repo 根目錄（按「載入 Repo」可重新選擇）"
            : $"目前選擇：{_selectedSubpath}（按「載入 Repo」可重新選擇）";
        _removeButton.Enabled = true;
    }

    private void StartNewProject(bool clearSelection = true)
    {
        _editing = null;
        _snapshot = null;
        _nameBox.Clear();
        _repoBox.Text = "";
        _repoInfo.Text = "";
        _tree.Nodes.Clear();
        _selectedSubpath = "";
        _removeButton.Enabled = false;
        RefreshSelectedLabel();
        if (clearSelection)
        {
            _suppressSelection = true;
            _projectList.ClearSelected();
            _suppressSelection = false;
        }
        _nameBox.Focus();
    }

    private async Task LoadRepoAsync()
    {
        var repo = _repoBox.Text.Trim();
        if (repo.Length == 0)
        {
            MessageBox.Show("請先選擇或輸入 GitHub Repo。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "正在載入…");
        try
        {
            _snapshot = await _git.LoadRemoteTreeAsync(repo, _cts.Token);
            _repoBox.Text = _snapshot.GitHubRepo;
            _repoInfo.Text = $"Repo：{_snapshot.GitHubRepo}\r\n本機位置：{_snapshot.RepoPath}\r\n目錄來源：origin/{_snapshot.DefaultBranch}";
            BuildTree(_snapshot.Directories);
            _knownRepos.Remember(_snapshot.GitHubRepo, _registry.Load());
            PopulateKnownRepos();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "載入 Repo 失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, "載入 Repo");
        }
    }

    private void BuildTree(IEnumerable<string> directories)
    {
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            var root = new TreeNode("Repo 根目錄") { Tag = "" };
            _tree.Nodes.Add(root);
            foreach (var path in directories) AddTreePath(root, path);
            root.Expand();
            SelectTreePath(_selectedSubpath);
        }
        finally { _tree.EndUpdate(); }
    }

    private static void AddTreePath(TreeNode root, string path)
    {
        var current = root;
        var accumulated = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated.Add(part);
            var next = current.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                next = new TreeNode(part) { Tag = string.Join('/', accumulated) };
                current.Nodes.Add(next);
            }
            current = next;
        }
    }

    private void SelectTreePath(string subpath)
    {
        var target = (subpath ?? "").Trim('/');
        var stack = new Stack<TreeNode>(_tree.Nodes.Cast<TreeNode>());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (string.Equals(node.Tag as string ?? "", target, StringComparison.OrdinalIgnoreCase))
            {
                _tree.SelectedNode = node;
                node.EnsureVisible();
                return;
            }
            foreach (TreeNode child in node.Nodes) stack.Push(child);
        }
        if (_tree.Nodes.Count > 0) _tree.SelectedNode = _tree.Nodes[0];
    }

    private async Task SaveAsync()
    {
        var name = _nameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("請填寫專案名稱。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var repoText = _repoBox.Text.Trim();
        if (_snapshot is null || !_snapshot.GitHubRepo.Equals(repoText, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("請先按「載入 Repo」，確認目前 Repo 與目錄。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "儲存中…");
        try
        {
            var branch = await _git.SafeSyncAsync(_snapshot.RepoPath, _editing?.DefaultBranch, _cts.Token);
            var subpath = _selectedSubpath.Trim('/');
            var fullPath = string.IsNullOrWhiteSpace(subpath)
                ? _snapshot.RepoPath
                : Path.Combine(_snapshot.RepoPath, subpath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullPath))
                throw new InvalidOperationException($"同步後找不到所選專案目錄：{subpath}");

            var entry = new ProjectEntry
            {
                Name = name,
                ProjectPath = fullPath,
                RepoName = _snapshot.GitHubRepo.Split('/')[1],
                RepoPath = _snapshot.RepoPath,
                RepoSubpath = subpath,
                GitHubRepo = _snapshot.GitHubRepo,
                DefaultBranch = branch,
                Active = true,
                Extra = _editing?.Extra
            };

            if (_editing is null) _registry.Add(entry);
            else _registry.Update(_editing.Name, entry);

            _knownRepos.Remember(entry.GitHubRepo, _registry.Load());
            _changed = true;
            SavedProjectName = entry.Name;
            PopulateKnownRepos();
            RefreshProjectList(entry.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "儲存專案失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed) SetBusy(false, "載入 Repo");
        }
    }

    private void RemoveRegistration()
    {
        if (_editing is null) return;
        var name = _editing.Name;
        var answer = MessageBox.Show(
            $"確定要從 AITeam 移除「{name}」？\r\n\r\n只會移除專案登錄，不會刪除本機 Repo 或 GitHub Repo。",
            "移除專案登錄",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _registry.Remove(name);
        _changed = true;
        SavedProjectName = null;
        RefreshProjectList();
    }

    private void SetBusy(bool busy, string loadText)
    {
        _loadButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        _newButton.Enabled = !busy;
        _projectList.Enabled = !busy;
        _removeButton.Enabled = !busy && _editing is not null;
        _loadButton.Text = loadText;
        UseWaitCursor = busy;
    }

    private void RefreshSelectedLabel()
    {
        _selectedLabel.Text = string.IsNullOrWhiteSpace(_selectedSubpath)
            ? "目前選擇：Repo 根目錄"
            : $"目前選擇：{_selectedSubpath}";
    }

    private static Label MakeFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SecondaryText,
        Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 0)
    };

    private static Button MakeSecondaryButton(string text, int width)
    {
        var button = new Button();
        ConfigureSecondaryButton(button, text, width);
        return button;
    }

    private static void ConfigureSecondaryButton(Button button, string text, int width)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Size = new Size(width, 34);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.BackColor = Color.White;
        button.ForeColor = PrimaryText;
        button.Margin = Padding.Empty;
    }

    private static void ConfigurePrimaryButton(Button button, string text, int width)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Size = new Size(width, 34);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        button.Margin = Padding.Empty;
    }
}
