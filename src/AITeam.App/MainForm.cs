using AITeam.Models;
using AITeam.Services;

namespace AITeam;

public sealed class MainForm : Form
{
    private readonly string _runtimeRoot;
    private readonly ProjectRegistryService _projectRegistry;
    private readonly ProviderHealthService _providerHealth;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly ComboBox _projectBox = new();
    private readonly Label _projectInfo = new();
    private readonly TextBox _requestBox = new();
    private readonly RichTextBox _outputBox = new();
    private readonly Button _recheckButton = new();
    private readonly Button _startButton = new();

    private readonly Dictionary<ProviderId, CheckBox> _providerChecks = new();
    private readonly Dictionary<ProviderId, Label> _providerLabels = new();

    private IReadOnlyList<ProjectEntry> _projects = Array.Empty<ProjectEntry>();

    public MainForm(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
        _projectRegistry = new ProjectRegistryService(runtimeRoot);
        _providerHealth = new ProviderHealthService(runtimeRoot, new ProcessRunner());

        Text = "AITeam — Portable EXE Alpha";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 650);
        Size = new Size(1180, 720);
        Font = new Font("Microsoft JhengHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
        LoadProjects();

        Shown += async (_, _) => await RecheckProvidersAsync();
        FormClosing += (_, _) => _lifetimeCts.Cancel();
    }

    private void BuildUi()
    {
        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            Panel1MinSize = 400,
            Panel2MinSize = 420
        };
        Controls.Add(root);

        Shown += (_, _) =>
        {
            var desired = Math.Min(520, Math.Max(420, ClientSize.Width / 2));
            if (desired > root.Panel1MinSize && desired < Width - root.Panel2MinSize)
            {
                root.SplitterDistance = desired;
            }
        };

        BuildLeftPanel(root.Panel1);
        BuildRightPanel(root.Panel2);
    }

    private void BuildLeftPanel(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 8
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parent.Controls.Add(layout);

        var heading = new Label
        {
            Text = "AITeam",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };
        layout.Controls.Add(heading);

        var projectRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
        projectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        projectRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _projectBox.Dock = DockStyle.Fill;
        _projectBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _projectBox.SelectedIndexChanged += (_, _) => UpdateProjectInfo();

        var projectButton = new Button
        {
            Text = "管理專案",
            AutoSize = true,
            Enabled = false,
            Margin = new Padding(8, 0, 0, 0)
        };
        var tip = new ToolTip();
        tip.SetToolTip(projectButton, "EXE 版 Project Manager 會在下一個 Alpha 接入；目前沿用既有專案設定。");

        projectRow.Controls.Add(_projectBox, 0, 0);
        projectRow.Controls.Add(projectButton, 1, 0);
        layout.Controls.Add(projectRow);

        _projectInfo.AutoSize = true;
        _projectInfo.ForeColor = SystemColors.GrayText;
        _projectInfo.Margin = new Padding(0, 5, 0, 0);
        layout.Controls.Add(_projectInfo);

        var providerGroup = new GroupBox
        {
            Text = "AI 使用狀態",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            Margin = new Padding(0, 14, 0, 0)
        };

        var providerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 3
        };
        providerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        providerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        providerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddProviderRow(providerLayout, 0, ProviderId.Codex, "GPT / Codex");
        AddProviderRow(providerLayout, 1, ProviderId.Claude, "Claude");
        AddProviderRow(providerLayout, 2, ProviderId.Antigravity, "Gemini / Antigravity");

        providerGroup.Controls.Add(providerLayout);
        layout.Controls.Add(providerGroup);

        _recheckButton.Text = "重新檢查 AI";
        _recheckButton.AutoSize = true;
        _recheckButton.Margin = new Padding(0, 8, 0, 8);
        _recheckButton.Click += async (_, _) => await RecheckProvidersAsync();
        layout.Controls.Add(_recheckButton);

        var requestLabel = new Label
        {
            Text = "任務 / 查詢",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4)
        };
        layout.Controls.Add(requestLabel);

        _requestBox.Multiline = true;
        _requestBox.ScrollBars = ScrollBars.Vertical;
        _requestBox.Dock = DockStyle.Fill;
        _requestBox.PlaceholderText = "Portable EXE 第一階段先驗證 GUI、專案載入與三個 AI 健康檢查。";
        layout.Controls.Add(_requestBox);

        _startButton.Text = "任務引擎：下一階段接入";
        _startButton.AutoSize = true;
        _startButton.Enabled = false;
        _startButton.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(_startButton);

        var alphaNote = new Label
        {
            Text = "v0.2.0-alpha.1：先與既有 PowerShell 版並行，不會修改任何專案。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(alphaNote);
    }

    private void BuildRightPanel(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 2,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        parent.Controls.Add(layout);

        var label = new Label
        {
            Text = "執行進度 / 結果",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };
        layout.Controls.Add(label);

        _outputBox.Dock = DockStyle.Fill;
        _outputBox.ReadOnly = true;
        _outputBox.BackColor = SystemColors.Window;
        _outputBox.Font = new Font("Consolas", 9.5F);
        _outputBox.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(_outputBox);
    }

    private void AddProviderRow(TableLayoutPanel layout, int row, ProviderId provider, string name)
    {
        var check = new CheckBox
        {
            Checked = true,
            AutoSize = true,
            Text = name,
            Margin = new Padding(0, 4, 10, 4)
        };

        var status = new Label
        {
            Text = "待檢查",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 10, 4)
        };

        var session = new Label
        {
            Text = "本次",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 0, 4)
        };

        check.CheckedChanged += (_, _) =>
        {
            if (!check.Checked)
            {
                status.Text = "手動暫停（本次）";
            }
            else
            {
                status.Text = "待重新檢查";
            }
        };

        _providerChecks[provider] = check;
        _providerLabels[provider] = status;

        layout.Controls.Add(check, 0, row);
        layout.Controls.Add(status, 1, row);
        layout.Controls.Add(session, 2, row);
    }

    private void LoadProjects()
    {
        try
        {
            _projects = _projectRegistry.Load();
            _projectBox.Items.Clear();

            foreach (var project in _projects)
            {
                _projectBox.Items.Add(project);
            }

            if (_projectBox.Items.Count > 0)
            {
                _projectBox.SelectedIndex = 0;
            }
            else
            {
                _projectInfo.Text = "找不到既有專案設定。";
            }

            AppendLog($"Runtime root: {_runtimeRoot}");
            AppendLog($"Project registry: {_projectRegistry.RegistryPath ?? "(not found)"}");
            AppendLog($"Projects loaded: {_projects.Count}");
            AppendLog("EXE Alpha 已啟動；此版本不會修改任何 project source。");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] 載入專案失敗：{ex.Message}");
        }
    }

    private void UpdateProjectInfo()
    {
        if (_projectBox.SelectedItem is not ProjectEntry project)
        {
            _projectInfo.Text = "";
            return;
        }

        _projectInfo.Text =
            $"{project.GitHubRepo}  |  {(string.IsNullOrWhiteSpace(project.RepoSubpath) ? "(repo root)" : project.RepoSubpath)}";
    }

    private async Task RecheckProvidersAsync()
    {
        _recheckButton.Enabled = false;
        AppendLog("開始檢查三個 AI...");

        foreach (var provider in Enum.GetValues<ProviderId>())
        {
            if (_providerChecks[provider].Checked)
            {
                _providerLabels[provider].Text = "檢查中...";
            }
        }

        var tasks = Enum.GetValues<ProviderId>()
            .Where(p => _providerChecks[p].Checked)
            .ToDictionary(
                p => p,
                p => _providerHealth.ProbeAsync(p, _lifetimeCts.Token));

        foreach (var item in tasks)
        {
            var health = await item.Value;
            _providerLabels[item.Key].Text = FriendlyState(health);
            AppendLog($"{FriendlyProvider(item.Key)}：{FriendlyState(health)} ({health.Duration.TotalSeconds:0.0}s)");

            if (health.State is ProviderHealthState.Error or
                ProviderHealthState.TemporaryError or
                ProviderHealthState.AuthenticationRequired or
                ProviderHealthState.Quota)
            {
                if (!string.IsNullOrWhiteSpace(health.Message) &&
                    health.Message != FriendlyState(health))
                {
                    AppendLog($"  {health.Message}");
                }
            }
        }

        _recheckButton.Enabled = true;
        AppendLog("AI 檢查完成。");
    }

    private static string FriendlyProvider(ProviderId provider) =>
        provider switch
        {
            ProviderId.Codex => "GPT / Codex",
            ProviderId.Claude => "Claude",
            ProviderId.Antigravity => "Gemini / Antigravity",
            _ => provider.ToString()
        };

    private static string FriendlyState(ProviderHealth health) =>
        health.State switch
        {
            ProviderHealthState.Unknown => "待檢查",
            ProviderHealthState.Checking => "檢查中...",
            ProviderHealthState.Online => "上線",
            ProviderHealthState.Quota => "超過限額",
            ProviderHealthState.AuthenticationRequired => "需要重新登入",
            ProviderHealthState.TemporaryError => "暫時異常",
            ProviderHealthState.Error => "異常（本次先跳過）",
            ProviderHealthState.Missing => "CLI 未安裝",
            _ => health.State.ToString()
        };

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), text);
            return;
        }

        _outputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
        _outputBox.SelectionStart = _outputBox.TextLength;
        _outputBox.ScrollToCaret();
    }
}
