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
        MinimumSize = new Size(900, 650);
        Size = new Size(980, 720);
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
            RowCount = 3,
            BackColor = AppBackground
        };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

        ConfigureSecondaryButton(_newButton, "新增專案", 100);
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

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 14, 0, 0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _removeButton.Text = "移除登錄";
        ConfigureSecondaryButton(_removeButton, "移除登錄", 100);
        _removeButton.ForeColor = Color.FromArgb(176, 54, 54);
        _removeButton.Click += (_, _) => RemoveRegistration();
        bottom.Controls.Add(_removeButton, 0, 0);

        var close = MakeSecondaryButton("完成", 90);
        close.Margin = new Padding(8, 0, 0, 0);
        close.Click += (_, _) =>
        {
            DialogResult = _changed ? DialogResult.OK : DialogResult.Cancel;
            Close();
        };
        bottom.Controls.Add(close, 1, 0);

        _saveButton.Text = "儲存";
        _saveButton.Size = new Size(100, 38);
        _saveButton.Margin = new Padding(8, 0, 0, 0);
        _saveButton.FlatStyle = FlatStyle.Flat;
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.BackColor = Accent;
        _saveButton.ForeColor = Color.White;
        _saveButton.Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold);
        _saveButton.Click += async (_, _) => await SaveAsync();
        bottom.Controls.Add(_saveButton, 2, 0);
        shell.Controls.Add(bottom, 0, 2);
    }

    private Control BuildProjectListPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 14, 0),
            Padding = new Padding(0),
            BackColor = AppBackground
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(MakeFieldLabel("已登錄專案"), 0, 0);
        _projectList.Dock = DockStyle.Fill;
        _projectList.BorderStyle = BorderStyle.FixedSingle;
        _projectList.IntegralHeight = false;
        _projectList.Margin = new Padding(0, 6, 0, 6);
        _projectList.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSelection) return;
            if (_projectList.SelectedItem is ProjectEntry entry) LoadProject(entry);
        };
        panel.Controls.Add(_projectList, 0, 1);
        panel.Controls.Add(new Label
        {
            Text = "新增專案不會覆蓋既有專案；選取清單項目後才會進入編輯模式。",
            AutoSize = true,
            MaximumSize = new Size(215, 0),
            ForeColor = SecondaryText
        }, 0, 2);
        return panel;
    }

    private Control BuildEditorPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 0, 0, 0),
            ColumnCount = 1,
            RowCount = 7,
            BackColor = AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(MakeFieldLabel("專案名稱"), 0, 0);
        _nameBox.Dock = DockStyle.Top;
        _nameBox.Margin = new Padding(0, 5, 0, 12);
        root.Controls.Add(_nameBox, 0, 1);

        root.Controls.Add(MakeFieldLabel("GitHub Repo（可選已知 Repo，或直接輸入 owner/repo）"), 0, 2);

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
        root.Controls.Add(repoRow, 0, 3);

        _repoInfo.AutoSize = true;
        _repoInfo.ForeColor = SecondaryText;
        _repoInfo.Margin = new Padding(0, 9, 0, 9);
        root.Controls.Add(_repoInfo, 0, 4);

        var treeWrap = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        treeWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        treeWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        treeWrap.Controls.Add(new Label
        {
            Text = "Repo 內的專案位置（若整個 Repo 就是一個專案，選「Repo 根目錄」）",
            AutoSize = true,
            ForeColor = PrimaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);

        _tree.Dock = DockStyle.Fill;
        _tree.BorderStyle = BorderStyle.FixedSingle;
        _tree.HideSelection = false;
        _tree.AfterSelect += (_, e) =>
        {
            _selectedSubpath = e.Node.Tag as string ?? "";
            RefreshSelectedLabel();
        };
        treeWrap.Controls.Add(_tree, 0, 1);
        root.Controls.Add(treeWrap, 0, 5);

        _selectedLabel.AutoSize = true;
        _selectedLabel.ForeColor = SecondaryText;
        _selectedLabel.Margin = new Padding(0, 8, 0, 0);
        root.Controls.Add(_selectedLabel, 0, 6);
        return root;
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

            var versionFile = _editing?.VersionFile ?? "";
            if (string.IsNullOrWhiteSpace(versionFile) && File.Exists(Path.Combine(fullPath, "VERSION"))) versionFile = "VERSION";

            var entry = new ProjectEntry
            {
                Name = name,
                ProjectPath = fullPath,
                RepoName = _snapshot.GitHubRepo.Split('/')[1],
                RepoPath = _snapshot.RepoPath,
                RepoSubpath = subpath,
                GitHubRepo = _snapshot.GitHubRepo,
                DefaultBranch = branch,
                VersionFile = versionFile,
                TagPrefix = string.IsNullOrWhiteSpace(_editing?.TagPrefix)
                    ? new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() + "-v"
                    : _editing!.TagPrefix,
                MergeStrategy = string.IsNullOrWhiteSpace(_editing?.MergeStrategy) ? "merge" : _editing.MergeStrategy,
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
        ForeColor = PrimaryText,
        Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold)
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
}
