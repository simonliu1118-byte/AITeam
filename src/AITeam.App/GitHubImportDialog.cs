using AITeam.Models;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 從 GitHub 匯入專案，分兩步：先勾要哪些 Repo，AITeam 把它們下載下來、找出裡面的專案，
/// 再勾要登錄哪些專案。換電腦或從頭開始時，不用一個一個手動新增。
/// </summary>
public sealed class GitHubImportDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);
    private static readonly Color ErrorText = Color.FromArgb(176, 54, 54);

    private readonly GitHubImportService _import;
    private readonly ProjectRegistryService _registry;
    private readonly CancellationTokenSource _cts = new();

    private readonly Label _title = new();
    private readonly Label _hint = new();
    private readonly CheckedListBox _list = new();
    private readonly Label _status = new();
    private readonly Button _selectAllButton = new();
    private readonly Button _nextButton = new();
    private readonly Button _loginButton = new();

    private bool _choosingProjects;
    private bool _busy;

    /// <summary>這次匯入登錄的專案名稱，照登錄順序。</summary>
    public IReadOnlyList<string> ImportedNames { get; private set; } = Array.Empty<string>();

    /// <summary>這次下載過的 Repo，讓專案管理的 Repo 下拉選單記得它們。</summary>
    public IReadOnlyList<string> LoadedRepos { get; private set; } = Array.Empty<string>();

    public GitHubImportDialog(string runtimeRoot, ProjectRegistryService registry)
    {
        _registry = registry;
        _import = new GitHubImportService(runtimeRoot, new ProcessRunner());

        Text = "AITeam - 從 GitHub 匯入專案";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(620, 520);
        Size = new Size(720, 600);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
        Shown += async (_, _) => await LoadRepositoriesAsync();
        FormClosing += (_, _) => _cts.Cancel();
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
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        _title.AutoSize = true;
        _title.Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold);
        _title.ForeColor = PrimaryText;
        _title.Margin = new Padding(0, 0, 0, 6);
        shell.Controls.Add(_title, 0, 0);

        _hint.AutoSize = true;
        _hint.ForeColor = SecondaryText;
        _hint.Font = new Font("Microsoft JhengHei UI", 9.5F);
        _hint.Margin = new Padding(0, 0, 0, 10);
        shell.Controls.Add(_hint, 0, 1);

        _list.Dock = DockStyle.Fill;
        _list.CheckOnClick = true;
        _list.IntegralHeight = false;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.HorizontalScrollbar = true;
        _list.Font = new Font("Microsoft JhengHei UI", 10F);
        // ItemCheck 發生在勾選狀態真正改變之前，等它改完再更新按鈕。
        _list.ItemCheck += (_, _) => BeginInvoke(new Action(RefreshButtons));
        shell.Controls.Add(_list, 0, 2);

        _status.AutoSize = true;
        _status.ForeColor = SecondaryText;
        _status.Font = new Font("Microsoft JhengHei UI", 9.5F);
        _status.Margin = new Padding(0, 8, 0, 8);
        shell.Controls.Add(_status, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        StyleSecondary(_selectAllButton, "全選", 90);
        _selectAllButton.Click += (_, _) => ToggleAll();
        footer.Controls.Add(_selectAllButton, 0, 0);

        StyleSecondary(_loginButton, "登入 GitHub", 120);
        _loginButton.ForeColor = Accent;
        _loginButton.Anchor = AnchorStyles.Left;
        _loginButton.Visible = false;
        _loginButton.Click += async (_, _) => await LoginToGitHubAsync();
        footer.Controls.Add(_loginButton, 1, 0);

        var cancel = new Button();
        StyleSecondary(cancel, "取消", 90);
        cancel.Click += (_, _) => Close();
        footer.Controls.Add(cancel, 2, 0);

        _nextButton.AutoSize = false;
        _nextButton.Size = new Size(150, 34);
        _nextButton.FlatStyle = FlatStyle.Flat;
        _nextButton.FlatAppearance.BorderSize = 0;
        _nextButton.BackColor = Accent;
        _nextButton.ForeColor = Color.White;
        _nextButton.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        _nextButton.Margin = new Padding(8, 0, 0, 0);
        _nextButton.Click += async (_, _) =>
        {
            if (_choosingProjects) ImportChecked();
            else await FindProjectsAsync();
        };
        footer.Controls.Add(_nextButton, 3, 0);
        shell.Controls.Add(footer, 0, 4);

        Resize += (_, _) => FitLabels();
        ShowRepoStep();
    }

    private void FitLabels()
    {
        var width = Math.Max(200, ClientSize.Width - 40);
        _title.MaximumSize = new Size(width, 0);
        _hint.MaximumSize = new Size(width, 0);
        _status.MaximumSize = new Size(width, 0);
    }

    private void ShowRepoStep()
    {
        _title.Text = "第 1 步：勾選要匯入的 GitHub Repo";
        _hint.Text = "下面是你的 GitHub 帳號看得到的 Repo（已封存的不列）。勾選之後，AITeam 會把它們下載到本機，找出裡面的專案。";
        _nextButton.Text = "下一步：找出專案";
        FitLabels();
    }

    private async Task LoadRepositoriesAsync()
    {
        SetBusy(true, "正在向 GitHub 取得 Repo 清單，請稍候。");
        try
        {
            var repos = await _import.ListRepositoriesAsync(_cts.Token);
            var registered = _registry.Load();
            _list.Items.Clear();
            foreach (var repo in repos)
                _list.Items.Add(new RepoItem(repo, registered.Count(p => p.GitHubRepo.Equals(repo.FullName, StringComparison.OrdinalIgnoreCase))));
            SetStatus(repos.Count == 0
                ? "GitHub 上找不到任何 Repo。"
                : $"共 {repos.Count} 個 Repo。已經有專案登錄的 Repo 也可以再勾，只會列出還沒登錄的專案。", isError: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
            _loginButton.Visible = ex is GitHubLoginRequiredException;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async Task LoginToGitHubAsync()
    {
        var (command, arguments, title) = CliMaintenance.GitHubLogin;
        _loginButton.Enabled = false;
        try
        {
            if (!await CliConsole.RunAsync(this, command, arguments, title)) return;
            _loginButton.Visible = false;
            await LoadRepositoriesAsync();
        }
        finally
        {
            _loginButton.Enabled = true;
        }
    }

    private async Task FindProjectsAsync()
    {
        var repos = _list.CheckedItems.OfType<RepoItem>().Select(r => r.Repo.FullName).ToList();
        if (repos.Count == 0) return;

        var registered = _registry.Load();
        var candidates = new List<ImportCandidate>();
        var failures = new List<string>();
        var loaded = new List<string>();

        SetBusy(true, "");
        try
        {
            for (var i = 0; i < repos.Count; i++)
            {
                SetStatus($"正在下載並檢查 {repos[i]}（{i + 1}/{repos.Count}）。第一次下載比較久，請稍候。", isError: false);
                try
                {
                    candidates.AddRange(await _import.FindProjectsAsync(repos[i], registered, _cts.Token));
                    loaded.Add(repos[i]);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add($"{repos[i]}：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (!IsDisposed) SetBusy(false, null);
        }

        LoadedRepos = loaded;
        _choosingProjects = true;
        _title.Text = "第 2 步：勾選要登錄的專案";
        _hint.Text = "有 VERSION 檔的資料夾會被當成一個專案；整個 Repo 都沒有 VERSION 的話，就把整個 Repo 當成一個專案。說明先帶入 README 的第一段，之後可以在專案管理裡修改。已經登錄過的專案不會再列出來。";
        _nextButton.Text = "匯入勾選的專案";
        FitLabels();

        _list.Items.Clear();
        foreach (var candidate in candidates) _list.Items.Add(new CandidateItem(candidate), true);

        var summary = candidates.Count == 0
            ? "這些 Repo 裡沒有還沒登錄的專案。"
            : $"找到 {candidates.Count} 個還沒登錄的專案。";
        if (failures.Count > 0) summary += "\r\n以下 Repo 沒辦法讀取：\r\n" + string.Join("\r\n", failures);
        SetStatus(summary, isError: failures.Count > 0);
        RefreshButtons();
    }

    private void ImportChecked()
    {
        var chosen = _list.CheckedItems.OfType<CandidateItem>().Select(c => c.Candidate).ToList();
        if (chosen.Count == 0) return;

        var taken = _registry.Load().Select(p => p.Name).ToList();
        var imported = new List<string>();
        try
        {
            foreach (var candidate in chosen)
            {
                var name = GitHubImportService.UniqueName(candidate.Name, candidate.GitHubRepo, taken);
                _registry.Add(GitHubImportService.ToProjectEntry(candidate, name));
                taken.Add(name);
                imported.Add(name);
            }
        }
        catch (Exception ex)
        {
            ImportedNames = imported;
            MessageBox.Show(ex.Message, "匯入專案失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            DialogResult = imported.Count > 0 ? DialogResult.OK : DialogResult.Cancel;
            return;
        }

        ImportedNames = imported;
        DialogResult = DialogResult.OK;
    }

    private void ToggleAll()
    {
        var check = _list.CheckedItems.Count < _list.Items.Count;
        for (var i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, check);
        RefreshButtons();
    }

    private void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _list.Enabled = !busy;
        if (!string.IsNullOrEmpty(status)) SetStatus(status, isError: false);
        RefreshButtons();
    }

    private void SetStatus(string text, bool isError)
    {
        _status.Text = text;
        _status.ForeColor = isError ? ErrorText : SecondaryText;
    }

    private void RefreshButtons()
    {
        if (IsDisposed) return;
        _nextButton.Enabled = !_busy && _list.CheckedItems.Count > 0;
        _selectAllButton.Enabled = !_busy && _list.Items.Count > 0;
        _selectAllButton.Text = _list.Items.Count > 0 && _list.CheckedItems.Count == _list.Items.Count ? "全不選" : "全選";
    }

    private static void StyleSecondary(Button button, string text, int width)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Size = new Size(width, 34);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.BackColor = Color.White;
        button.ForeColor = PrimaryText;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private sealed record RepoItem(GitHubRepoListing Repo, int RegisteredCount)
    {
        public override string ToString()
        {
            var text = Repo.FullName + (Repo.IsPrivate ? "（私人）" : "");
            if (RegisteredCount > 0) text += $"　已登錄 {RegisteredCount} 個專案";
            if (Repo.Description.Length > 0) text += "　" + Repo.Description;
            return text;
        }
    }

    private sealed record CandidateItem(ImportCandidate Candidate)
    {
        public override string ToString()
        {
            var location = Candidate.RepoSubpath.Length == 0
                ? $"{Candidate.GitHubRepo}（整個 Repo）"
                : $"{Candidate.GitHubRepo}/{Candidate.RepoSubpath}";
            return $"{Candidate.Name}　{location}";
        }
    }
}
