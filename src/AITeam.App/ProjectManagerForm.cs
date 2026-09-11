using AITeam.Models;
using AITeam.Services;

namespace AITeam;

public sealed class ProjectManagerForm : Form
{
    private static readonly Color AppBackground = Color.FromArgb(245, 247, 250);
    private static readonly Color BorderColor = Color.FromArgb(205, 212, 220);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);

    private readonly ProjectRegistryService _registry;
    private readonly KnownRepositoryService _knownRepos;
    private readonly GitRepositoryService _git;
    private readonly CancellationTokenSource _cts = new();

    private readonly TextBox _nameBox = new();
    private readonly ComboBox _repoBox = new();
    private readonly Label _repoInfo = new();
    private readonly TreeView _tree = new();
    private readonly Label _selectedLabel = new();
    private readonly Button _loadButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _removeButton = new();

    private ProjectEntry? _editing;
    private RepoTreeSnapshot? _snapshot;
    private string _selectedSubpath = "";

    public ProjectManagerForm(string runtimeRoot, ProjectRegistryService registry, ProjectEntry? selectedProject)
    {
        _registry = registry;
        _knownRepos = new KnownRepositoryService(runtimeRoot);
        _git = new GitRepositoryService(runtimeRoot, new ProcessRunner());
        _editing = selectedProject;

        Text = "AITeam - 管理專案";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 620);
        Size = new Size(860, 680);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;

        BuildUi();
        PopulateKnownRepos();
        LoadEditingProject();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 9,
            BackColor = AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var titleRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.Controls.Add(new Label
        {
            Text = "專案管理",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 15F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 14)
        }, 0, 0);

        var newButton = MakeSecondaryButton("新增專案", 96);
        newButton.Click += (_, _) => StartNewProject();
        titleRow.Controls.Add(newButton, 1, 0);
        root.Controls.Add(titleRow, 0, 0);

        root.Controls.Add(MakeFieldLabel("專案名稱"), 0, 1);
        _nameBox.Dock = DockStyle.Top;
        _nameBox.Margin = new Padding(0, 5, 0, 12);
        root.Controls.Add(_nameBox, 0, 2);

        root.Controls.Add(new Label
        {
            Text = "GitHub Repo（可選已知 Repo，或直接輸入 owner/repo）",
            AutoSize = true,
            ForeColor = PrimaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 5)
        }, 0, 3);

        var repoRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        repoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        repoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _repoBox.Dock = DockStyle.Fill;
        _repoBox.DropDownStyle = ComboBoxStyle.DropDown;
        _repoBox.Margin = new Padding(0, 0, 10, 0);
        ConfigureSecondaryButton(_loadButton, "載入 Repo", 104);
        _loadButton.Click += async (_, _) => await LoadRepoAsync();
        repoRow.Controls.Add(_repoBox, 0, 0);
        repoRow.Controls.Add(_loadButton, 1, 0);
        root.Controls.Add(repoRow, 0, 4);

        var treeWrap = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 12, 0, 0)
        };
        treeWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        treeWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        treeWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _repoInfo.AutoSize = true;
        _repoInfo.ForeColor = SecondaryText;
        _repoInfo.Margin = new Padding(0, 0, 0, 8);
        treeWrap.Controls.Add(_repoInfo, 0, 0);
        treeWrap.Controls.Add(new Label
        {
            Text = "Repo 內的專案位置（若整個 Repo 就是一個專案，選「Repo 根目錄」）",
            AutoSize = true,
            ForeColor = PrimaryText,
            Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 1);

        _tree.Dock = DockStyle.Fill;
        _tree.BorderStyle = BorderStyle.FixedSingle;
        _tree.HideSelection = false;
        _tree.AfterSelect += (_, e) =>
        {
            _selectedSubpath = e.Node.Tag as string ?? "";
            RefreshSelectedLabel();
        };
        treeWrap.Controls.Add(_tree, 0, 2);
        root.Controls.Add(treeWrap, 0, 5);

        _selectedLabel.AutoSize = true;
        _selectedLabel.ForeColor = SecondaryText;
        _selectedLabel.Margin = new Padding(0, 8, 0, 0);
        root.Controls.Add(_selectedLabel, 0, 6);

        root.Controls.Add(new Label
        {
            Text = "移除專案只會取消 AITeam 登錄，不會刪除本機 Repo 或 GitHub Repo。",
            AutoSize = true,
            ForeColor = SecondaryText,
            Margin = new Padding(0, 8, 0, 8)
        }, 0, 7);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        ConfigureSecondaryButton(_removeButton, "移除登錄", 96);
        _removeButton.ForeColor = Color.FromArgb(176, 54, 54);
        _removeButton.Click += (_, _) => RemoveRegistration();
        buttons.Controls.Add(_removeButton, 0, 0);

        var cancel = MakeSecondaryButton("取消", 88);
        cancel.Margin = new Padding(8, 0, 0, 0);
        cancel.Click += (_, _) => Close();
        buttons.Controls.Add(cancel, 1, 0);

        _saveButton.Text = "儲存";
        _saveButton.Size = new Size(96, 38);
        _saveButton.Margin = new Padding(8, 0, 0, 0);
        _saveButton.FlatStyle = FlatStyle.Flat;
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.BackColor = Accent;
        _saveButton.ForeColor = Color.White;
        _saveButton.Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold);
        _saveButton.Click += async (_, _) => await SaveAsync();
        buttons.Controls.Add(_saveButton, 2, 0);

        root.Controls.Add(buttons, 0, 8);
    }

    private void PopulateKnownRepos()
    {
        foreach (var repo in _knownRepos.Load(_registry.Load()))
        {
            if (_repoBox.Items.IndexOf(repo) < 0) _repoBox.Items.Add(repo);
        }
    }

    private void LoadEditingProject()
    {
        _removeButton.Enabled = _editing is not null;
        if (_editing is null)
        {
            StartNewProject();
            return;
        }

        _nameBox.Text = _editing.Name;
        _repoBox.Text = _editing.GitHubRepo;
        _selectedSubpath = _editing.RepoSubpath ?? "";
        _repoInfo.Text = $"本機位置：{_editing.RepoPath}";
        RefreshSelectedLabel();
    }

    private void StartNewProject()
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
            _repoInfo.Text = $"本機位置：{_snapshot.RepoPath}   |   目錄來源：origin/{_snapshot.DefaultBranch}";
            BuildTree(_snapshot.Directories);
            _knownRepos.Remember(_snapshot.GitHubRepo, _registry.Load());
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
        finally
        {
            _tree.EndUpdate();
        }
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
        if (_snapshot is null || !_snapshot.GitHubRepo.Equals(_repoBox.Text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("請先按「載入 Repo」，確認目前 Repo 的目錄。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = _nameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("請填寫專案名稱。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "儲存中…");
        try
        {
            var branch = await _git.SafeSyncAsync(_snapshot.RepoPath, _cts.Token);
            var subpath = _selectedSubpath.Trim('/');
            var fullPath = string.IsNullOrWhiteSpace(subpath)
                ? _snapshot.RepoPath
                : Path.Combine(_snapshot.RepoPath, subpath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullPath)) throw new InvalidOperationException($"同步後找不到所選專案目錄：{subpath}");

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
                Active = true,
                Extra = _editing?.Extra
            };

            _registry.Upsert(entry, _editing?.Name);
            _knownRepos.Remember(entry.GitHubRepo, _registry.Load());
            DialogResult = DialogResult.OK;
            Close();
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
        var answer = MessageBox.Show(
            $"確定要從 AITeam 移除「{_editing.Name}」？\r\n\r\n只會移除專案登錄，不會刪除本機 Repo 或 GitHub Repo。",
            "移除專案登錄",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _registry.Remove(_editing.Name);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetBusy(bool busy, string loadText)
    {
        _loadButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
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
        button.Size = new Size(width, 34);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.White;
        button.ForeColor = PrimaryText;
        button.Margin = Padding.Empty;
        button.FlatAppearance.BorderColor = BorderColor;
    }
}
