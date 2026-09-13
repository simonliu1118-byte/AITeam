using System.Reflection;
using AITeam.Models;
using AITeam.Services;

namespace AITeam;

public sealed class MainWindow : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);

    private readonly string _runtimeRoot;
    private readonly ProjectRegistryService _projectRegistry;
    private readonly ProviderHealthService _providerHealth;
    private readonly InquiryService _inquiryService;
    private readonly TaskHistoryService _taskHistory;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly ComboBox _projectBox = new();
    private readonly Label _projectInfo = new();
    private readonly TextBox _requestBox = new();
    private readonly TextBox _currentTaskBox = new();
    private readonly RichTextBox _outputBox = new();
    private readonly Button _recheckButton = new();
    private readonly Button _sendButton = new();
    private readonly Button _projectButton = new();
    private readonly Button _historyButton = new();
    private readonly StatusBadge _modeBadge = new();
    private readonly Label _currentTaskState = new();
    private readonly Label _taskClock = new();
    private readonly TaskStageStrip _stageStrip = new();
    private readonly Label _stageDetail = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1000 };
    private readonly Dictionary<ProviderId, ProviderStatusRow> _providerCards = new();

    private IReadOnlyList<ProjectEntry> _projects = Array.Empty<ProjectEntry>();
    private bool _taskRunning;
    private DateTime? _taskStartedAt;
    private DateTime? _stageStartedAt;
    private TaskProgress? _currentProgress;
    private TaskKind? _stageKind;
    private readonly System.Text.StringBuilder _taskLog = new();
    private string _taskRequest = "";
    private string _taskProjectName = "";

    public MainWindow(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
        var runner = new ProcessRunner();
        _projectRegistry = new ProjectRegistryService(runtimeRoot);
        _providerHealth = new ProviderHealthService(runtimeRoot, runner);
        _inquiryService = new InquiryService(runtimeRoot, runner);
        _taskHistory = new TaskHistoryService(runtimeRoot);

        Text = "AITeam";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 690);
        Size = new Size(1240, 760);
        Font = new Font("Microsoft JhengHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
        LoadProjects();

        _clockTimer.Tick += (_, _) => UpdateClock();

        Shown += async (_, _) => await RecheckProvidersAsync();
        FormClosing += OnFormClosing;
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = AppBackground,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        // 單欄 TableLayoutPanel 一定要明確指定 Percent 欄寬：沒有指定時該欄預設是
        // AutoSize，內容需要多寬就撐多寬，視窗變窄時不會跟著縮，整塊內容會溢出容器
        // 右緣被裁掉（這正是「左半邊右緣被切到」的成因）。
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(shell);
        shell.Controls.Add(BuildHeader(), 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = AppBackground,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
        shell.Controls.Add(body, 0, 1);

        var left = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground };
        var gutter = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground };
        var right = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground };
        body.Controls.Add(left, 0, 0);
        body.Controls.Add(gutter, 1, 0);
        body.Controls.Add(right, 2, 0);
        BuildLeftPanel(left);
        BuildRightPanel(right);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            Padding = new Padding(22, 14, 22, 12)
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var title = new Label
        {
            Text = "AITeam",
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 17F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Location = new Point(22, 13)
        };

        var version = new Label
        {
            Text = ResolveVersionText(),
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 9F),
            ForeColor = SecondaryText,
            Location = new Point(24, 44)
        };

        _modeBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _modeBadge.BackColor = CardBackground;

        header.Controls.Add(title);
        header.Controls.Add(version);
        header.Controls.Add(_modeBadge);

        void PositionBadge() =>
            _modeBadge.Location = new Point(Math.Max(0, header.ClientSize.Width - _modeBadge.Width - 22), 20);

        // 徽章寬度會隨文字變動（「待命」↔「實作中 4/7」），所以寬度變了也要重新靠右。
        header.Resize += (_, _) => PositionBadge();
        _modeBadge.SizeChanged += (_, _) => PositionBadge();
        PositionBadge();
        return header;
    }

    private void BuildLeftPanel(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 10, 16),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = AppBackground,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parent.Controls.Add(layout);

        layout.Controls.Add(SectionTitle("專案", new Padding(2, 0, 0, 7)), 0, 0);
        layout.Controls.Add(BuildProjectCard(), 0, 1);
        layout.Controls.Add(BuildProviderArea(), 0, 2);
        layout.Controls.Add(SectionTitle("任務 / 查詢", new Padding(2, 16, 0, 7)), 0, 3);

        var requestCard = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(13),
            Margin = new Padding(0, 0, 2, 0)
        };
        _requestBox.Multiline = true;
        _requestBox.ScrollBars = ScrollBars.Vertical;
        _requestBox.BorderStyle = BorderStyle.None;
        _requestBox.Dock = DockStyle.Fill;
        _requestBox.BackColor = CardBackground;
        _requestBox.ForeColor = PrimaryText;
        _requestBox.Font = new Font("Microsoft JhengHei UI", 11F);
        _requestBox.PlaceholderText = "輸入任務或查詢內容…";
        _requestBox.TextChanged += (_, _) => RefreshSendButton();
        requestCard.Controls.Add(_requestBox);
        layout.Controls.Add(requestCard, 0, 4);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 12, 2, 0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(new Label
        {
            Text = "任務執行中仍可先輸入下一件；完成前「送出」會保持鎖定。",
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SecondaryText,
            Margin = new Padding(2, 0, 10, 0)
        }, 0, 0);

        _sendButton.Text = "送出";
        _sendButton.AutoSize = false;
        _sendButton.Size = new Size(116, 42);
        _sendButton.FlatStyle = FlatStyle.Flat;
        _sendButton.FlatAppearance.BorderSize = 0;
        _sendButton.BackColor = Accent;
        _sendButton.ForeColor = Color.White;
        _sendButton.Font = new Font("Microsoft JhengHei UI", 10.5F, FontStyle.Bold);
        _sendButton.Cursor = Cursors.Hand;
        _sendButton.Click += async (_, _) => await HandleSendAsync();
        bottom.Controls.Add(_sendButton, 1, 0);
        layout.Controls.Add(bottom, 0, 5);
        RefreshSendButton();
    }

    private Control BuildProjectCard()
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(13, 12, 13, 10),
            Margin = new Padding(0, 0, 2, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _projectBox.Font = Font;
        _projectBox.Dock = DockStyle.Fill;
        _projectBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _projectBox.Margin = new Padding(0, 0, 10, 0);
        _projectBox.SelectedIndexChanged += (_, _) => UpdateProjectInfo();

        var projectButtonHeight = _projectBox.PreferredHeight + 2;
        _projectButton.Text = "管理專案";
        _projectButton.AutoSize = false;
        _projectButton.Size = new Size(104, projectButtonHeight);
        _projectButton.MinimumSize = new Size(104, projectButtonHeight);
        _projectButton.MaximumSize = new Size(104, projectButtonHeight);
        _projectButton.FlatStyle = FlatStyle.Flat;
        _projectButton.BackColor = Color.White;
        _projectButton.ForeColor = PrimaryText;
        _projectButton.Margin = Padding.Empty;
        _projectButton.FlatAppearance.BorderColor = BorderColor;
        _projectButton.Click += (_, _) => OpenProjectManager();

        layout.Controls.Add(_projectBox, 0, 0);
        layout.Controls.Add(_projectButton, 1, 0);

        _projectInfo.AutoSize = false;
        _projectInfo.AutoEllipsis = true;
        _projectInfo.Dock = DockStyle.Fill;
        _projectInfo.TextAlign = ContentAlignment.TopLeft;
        _projectInfo.ForeColor = SecondaryText;
        _projectInfo.Margin = new Padding(0, 8, 0, 0);
        layout.SetColumnSpan(_projectInfo, 2);
        layout.Controls.Add(_projectInfo, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildProviderArea()
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 16, 2, 0),
            BackColor = AppBackground
        };
        wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = AppBackground
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.Controls.Add(SectionTitle("AI 狀態", new Padding(2, 7, 0, 0)), 0, 0);

        _recheckButton.Text = "重新檢查";
        _recheckButton.AutoSize = false;
        _recheckButton.Size = new Size(94, 32);
        _recheckButton.FlatStyle = FlatStyle.Flat;
        _recheckButton.BackColor = Color.White;
        _recheckButton.ForeColor = PrimaryText;
        _recheckButton.FlatAppearance.BorderColor = BorderColor;
        _recheckButton.Click += async (_, _) => await RecheckProvidersAsync();
        titleRow.Controls.Add(_recheckButton, 1, 0);
        wrapper.Controls.Add(titleRow, 0, 0);

        var card = new RoundedCard
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(16, 2, 14, 2)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 5,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddProviderRow(grid, 0, ProviderId.Codex, "GPT / Codex");
        AddProviderRow(grid, 1, ProviderId.Claude, "Claude");
        AddProviderRow(grid, 2, ProviderId.Antigravity, "Gemini / Antigravity");

        card.Controls.Add(grid);
        wrapper.Controls.Add(card, 0, 1);
        return wrapper;
    }

    private void AddProviderRow(TableLayoutPanel grid, int row, ProviderId provider, string name)
    {
        var status = new ProviderStatusRow(provider, name);
        status.SessionEnabledChanged += (_, enabled) =>
        {
            if (!enabled) status.SetManualDisabled();
            else status.SetHealth(new ProviderHealth(provider, ProviderHealthState.Unknown, "待重新檢查", TimeSpan.Zero));
        };
        _providerCards[provider] = status;
        status.AddTo(grid, row);
    }

    private void BuildRightPanel(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 16, 18, 16),
            RowCount = 4,
            ColumnCount = 1,
            BackColor = AppBackground,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 卡片要放得下：狀態列 + 任務內容 + 階段進度條 + 目前動作說明
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 172F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        parent.Controls.Add(layout);

        var currentTitleRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        currentTitleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        currentTitleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        currentTitleRow.Controls.Add(SectionTitle("目前任務", new Padding(2, 4, 0, 7)), 0, 0);

        _historyButton.Text = "歷史任務";
        _historyButton.AutoSize = false;
        _historyButton.Size = new Size(94, 30);
        _historyButton.FlatStyle = FlatStyle.Flat;
        _historyButton.BackColor = Color.White;
        _historyButton.ForeColor = PrimaryText;
        _historyButton.Margin = new Padding(0, 0, 2, 7);
        _historyButton.FlatAppearance.BorderColor = BorderColor;
        _historyButton.Click += (_, _) => OpenTaskHistory();
        currentTitleRow.Controls.Add(_historyButton, 1, 0);
        layout.Controls.Add(currentTitleRow, 0, 0);

        var currentCard = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 2, 0)
        };
        var currentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        currentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 第一列：狀態文字（左）＋ 經過時間（右）
        var headRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 4) };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _currentTaskState.Text = "待命";
        _currentTaskState.AutoSize = true;
        _currentTaskState.ForeColor = SecondaryText;
        _currentTaskState.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        _currentTaskState.Margin = Padding.Empty;
        _taskClock.AutoSize = true;
        _taskClock.Text = "";
        _taskClock.ForeColor = SecondaryText;
        _taskClock.Font = new Font("Microsoft JhengHei UI", 8.5F);
        _taskClock.Margin = new Padding(10, 1, 0, 0);
        _taskClock.Anchor = AnchorStyles.Right;
        headRow.Controls.Add(_currentTaskState, 0, 0);
        headRow.Controls.Add(_taskClock, 1, 0);
        currentLayout.Controls.Add(headRow, 0, 0);

        // 任務內容框起來：原本整塊留白讓卡片看起來空空的，給它一個底色框就有邊界感。
        var requestFrame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(247, 249, 251),
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 0, 0, 2)
        };
        _currentTaskBox.Multiline = true;
        _currentTaskBox.ReadOnly = true;
        _currentTaskBox.BorderStyle = BorderStyle.None;
        _currentTaskBox.BackColor = Color.FromArgb(247, 249, 251);
        _currentTaskBox.ForeColor = PrimaryText;
        _currentTaskBox.Text = "尚未送出任務。";
        _currentTaskBox.Dock = DockStyle.Fill;
        _currentTaskBox.ScrollBars = ScrollBars.Vertical;
        requestFrame.Controls.Add(_currentTaskBox);
        currentLayout.Controls.Add(requestFrame, 0, 1);

        _stageStrip.Dock = DockStyle.Top;
        _stageStrip.BackColor = CardBackground;
        _stageStrip.Margin = new Padding(0, 6, 0, 0);
        _stageStrip.Visible = false;
        currentLayout.Controls.Add(_stageStrip, 0, 2);

        _stageDetail.AutoSize = false;
        _stageDetail.AutoEllipsis = true;
        _stageDetail.Dock = DockStyle.Top;
        _stageDetail.Height = 20;
        _stageDetail.TextAlign = ContentAlignment.MiddleLeft;
        _stageDetail.ForeColor = SecondaryText;
        _stageDetail.Font = new Font("Microsoft JhengHei UI", 9F);
        _stageDetail.Margin = new Padding(0, 2, 0, 0);
        _stageDetail.Visible = false;
        currentLayout.Controls.Add(_stageDetail, 0, 3);

        currentCard.Controls.Add(currentLayout);
        layout.Controls.Add(currentCard, 0, 1);

        layout.Controls.Add(SectionTitle("執行進度 / 結果", new Padding(2, 16, 0, 7)), 0, 2);

        var logCard = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(11),
            Margin = new Padding(0, 0, 2, 0)
        };
        _outputBox.Dock = DockStyle.Fill;
        _outputBox.ReadOnly = true;
        _outputBox.BorderStyle = BorderStyle.None;
        _outputBox.BackColor = CardBackground;
        _outputBox.ForeColor = Color.FromArgb(55, 62, 70);
        _outputBox.Font = new Font("Microsoft JhengHei UI", 9F);
        _outputBox.DetectUrls = false;
        logCard.Controls.Add(_outputBox);
        layout.Controls.Add(logCard, 0, 3);
    }

    private static string ResolveVersionText()
    {
        // 版號來自建置時嵌入的 InformationalVersion（格式：X.Y.Z+build.N），
        // 依共用規則顯示成 VX.Y.Z，Build 大於 0 時顯示成 VX.Y.Z Build N。
        var informational = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var parts = informational.Split('+', 2);
            var baseVersion = parts[0];
            if (parts.Length == 2 &&
                parts[1].StartsWith("build.", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[1]["build.".Length..], out var build) &&
                build > 0)
            {
                return $"V{baseVersion} Build {build}";
            }
            return $"V{baseVersion}";
        }

        var v = typeof(MainWindow).Assembly.GetName().Version;
        return v is null ? "V0.0.0" : $"V{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }

    private static Label SectionTitle(string text, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = PrimaryText,
        Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),
        Margin = margin
    };

    private void LoadProjects(string? preferredName = null)
    {
        try
        {
            _projects = _projectRegistry.Load();
            var currentName = preferredName ?? (_projectBox.SelectedItem as ProjectEntry)?.Name;
            _projectBox.Items.Clear();
            foreach (var project in _projects) _projectBox.Items.Add(project);

            if (_projectBox.Items.Count == 0)
            {
                _projectInfo.Text = "尚未登錄專案，請按「管理專案」新增。";
                return;
            }

            var index = 0;
            if (!string.IsNullOrWhiteSpace(currentName))
            {
                for (var i = 0; i < _projectBox.Items.Count; i++)
                {
                    if ((_projectBox.Items[i] as ProjectEntry)?.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        index = i;
                        break;
                    }
                }
            }
            _projectBox.SelectedIndex = index;
            AppendLog($"Runtime root: {_runtimeRoot}");
            AppendLog($"Project registry: {_projectRegistry.RegistryPath ?? "(not found)"}");
            AppendLog($"Projects loaded: {_projects.Count}");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] 載入專案失敗：{ex.Message}");
        }
    }

    private void OpenProjectManager()
    {
        var selected = _projectBox.SelectedItem as ProjectEntry;
        using var form = new ProjectManagerForm(_runtimeRoot, _projectRegistry, selected);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            LoadProjects(form.SavedProjectName ?? selected?.Name);
            AppendLog("專案登錄已更新。");
        }
    }

    private void UpdateProjectInfo()
    {
        if (_projectBox.SelectedItem is not ProjectEntry project)
        {
            _projectInfo.Text = "";
            return;
        }
        _projectInfo.Text = $"Repo：{project.GitHubRepo}\r\n本機位置：{project.PhysicalPath}";
    }

    private async Task RecheckProvidersAsync()
    {
        _recheckButton.Enabled = false;
        AppendLog("開始檢查三個 AI...");
        foreach (var provider in Enum.GetValues<ProviderId>())
        {
            if (_providerCards[provider].SessionEnabled)
                _providerCards[provider].SetHealth(ProviderHealth.Checking(provider));
        }

        var tasks = Enum.GetValues<ProviderId>()
            .Where(p => _providerCards[p].SessionEnabled)
            .ToDictionary(p => p, p => _providerHealth.ProbeAsync(p, _lifetimeCts.Token));

        foreach (var item in tasks)
        {
            var health = await item.Value;
            _providerCards[item.Key].SetHealth(health);
            AppendLog($"{item.Key.ToFriendlyName()}：{health.State.ToFriendlyName()} ({health.Duration.TotalSeconds:0.0}s)");
            // 只要 CLI 有話說就寫進紀錄。卡片上只有「錯誤」兩個字，看不出到底是額度用完、
            // 沒登入還是參數不合，等於無從查起；一次檢查成功但中途換過參數時也要留痕跡。
            if (!string.IsNullOrWhiteSpace(health.Detail))
                AppendLog($"    └ CLI 回報：{health.Detail}");
        }
        _recheckButton.Enabled = true;
        AppendLog("AI 檢查完成。");
    }

    private async Task HandleSendAsync()
    {
        if (_taskRunning || string.IsNullOrWhiteSpace(_requestBox.Text)) return;
        if (_projectBox.SelectedItem is not ProjectEntry project)
        {
            MessageBox.Show("請先選擇專案。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var request = _requestBox.Text.Trim();
        _currentTaskBox.Text = request;
        _requestBox.Clear();
        _taskRequest = request;
        _taskProjectName = project.Name;
        _taskLog.Clear();
        StartTaskProgress();
        SetTaskRunning(true);
        _outputBox.Clear();

        try
        {
            var candidates = GetProviderCandidates();
            var result = await _inquiryService.RunAsync(
                project,
                request,
                candidates,
                AskPlanGateAsync,
                text => AppendLog(text),
                ReportStage,
                _lifetimeCts.Token);

            if (result.Intent == RequestIntent.Change && result.Change is { } change)
            {
                FinishTaskProgress($"已完成 · {change.Version}");
                AppendLog("");
                AppendLog(result.Answer);
            }
            else
            {
                FinishTaskProgress($"已完成 · {result.Provider.ToFriendlyName()}");
                AppendLog("");
                AppendLog(result.Answer);
            }
            SaveTaskHistory(TaskOutcome.Completed, result.Subject, result.Answer);
        }
        catch (OperationCanceledException)
        {
            FailTaskProgress("已取消");
            AppendLog("任務已取消。");
            SaveTaskHistory(TaskOutcome.Cancelled, "", "任務已取消。");
        }
        catch (Exception ex)
        {
            FailTaskProgress("執行失敗");
            AppendLog("[ERROR] " + ex.Message);
            SaveTaskHistory(TaskOutcome.Failed, "", ex.Message);
        }
        finally
        {
            if (!IsDisposed) SetTaskRunning(false);
        }
    }

    private void StartTaskProgress()
    {
        _taskStartedAt = DateTime.Now;
        _stageStartedAt = DateTime.Now;
        _currentProgress = null;
        _currentTaskState.Text = "執行中";
        _currentTaskState.ForeColor = SecondaryText;
        _stageDetail.Text = "準備中…";
        _stageDetail.Visible = true;
        _stageStrip.Visible = true;
        _stageStrip.Clear();
        _stageKind = null;
        _modeBadge.SetStatus("執行中", "", Color.FromArgb(255, 247, 225), Color.FromArgb(158, 104, 0), Color.FromArgb(214, 158, 46), Color.Empty);
        UpdateClock();
        _clockTimer.Start();
    }

    /// <summary>Core 回報階段變化的進入點，可能來自背景執行緒。</summary>
    private void ReportStage(TaskProgress progress)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<TaskProgress>(ReportStage), progress);
            return;
        }
        if (IsDisposed) return;

        var stageChanged = _currentProgress is null || _currentProgress.Stage != progress.Stage;
        var startedWaiting = progress.Activity == TaskActivity.WaitingForUser &&
                             _currentProgress?.Activity != TaskActivity.WaitingForUser;
        _currentProgress = progress;
        if (stageChanged) _stageStartedAt = DateTime.Now;

        // 只有任務種類變了才重建階段清單（查詢中途轉成修改任務時會換成七格），
        // 否則每次回報都重建會把「階段只往前走」的記錄一起洗掉。
        if (_stageKind != progress.Kind)
        {
            _stageKind = progress.Kind;
            _stageStrip.ShowStages(TaskStages.For(progress.Kind));
        }
        _stageStrip.SetCurrent(progress.Stage, progress.Activity == TaskActivity.WaitingForUser);
        _stageStrip.Visible = true;

        var who = progress.Provider is { } provider ? provider.ToFriendlyName() + "：" : "";
        var round = progress.Round > 1 ? $"（第 {progress.Round} 輪）" : "";
        _stageDetail.Text = who + progress.Detail + round;
        _stageDetail.Visible = true;

        _currentTaskState.Text = progress.Activity == TaskActivity.WaitingForUser ? "等你回覆" : "執行中";
        _currentTaskState.ForeColor = progress.Activity == TaskActivity.WaitingForUser ? Accent : SecondaryText;

        UpdateBadge();
        UpdateClock();

        // 只有在使用者已經切到別的程式時才閃工作列，避免人就在畫面前還一直閃。
        if (startedWaiting && Form.ActiveForm is null) FlashTaskbar();
    }

    private void FinishTaskProgress(string stateText)
    {
        _clockTimer.Stop();
        _stageStrip.MarkAllDone();
        _currentProgress = null;
        _currentTaskState.Text = stateText;
        _currentTaskState.ForeColor = Color.FromArgb(36, 122, 72);
        _stageDetail.Text = "";
        _stageDetail.Visible = false;
        _modeBadge.SetStatus("已完成", "", Color.FromArgb(236, 248, 240), Color.FromArgb(36, 122, 72), Color.FromArgb(46, 160, 92), Color.Empty);
        UpdateClock(totalOnly: true);
        if (Form.ActiveForm is null) FlashTaskbar();
    }

    private void FailTaskProgress(string stateText)
    {
        _clockTimer.Stop();
        _currentProgress = null;
        _currentTaskState.Text = stateText;
        _currentTaskState.ForeColor = Color.FromArgb(176, 54, 54);
        _stageDetail.Visible = false;
        _modeBadge.SetStatus(stateText, "", Color.FromArgb(251, 237, 236), Color.FromArgb(176, 54, 54), Color.FromArgb(176, 54, 54), Color.Empty);
        UpdateClock(totalOnly: true);
        if (Form.ActiveForm is null) FlashTaskbar();
    }

    private void UpdateBadge()
    {
        if (_currentProgress is not { } progress)
        {
            _modeBadge.SetStatus("待命", "", Color.FromArgb(236, 248, 240), Color.FromArgb(36, 122, 72), Color.FromArgb(46, 160, 92), Color.Empty);
            return;
        }

        var stages = TaskStages.For(progress.Kind);
        var step = 0;
        for (var i = 0; i < stages.Count; i++)
            if (stages[i] == progress.Stage) { step = i + 1; break; }
        var meter = step > 0 ? $"{step}/{stages.Count}" : "";

        if (progress.Activity == TaskActivity.WaitingForUser)
        {
            _modeBadge.SetStatus("等你回覆", meter, Color.FromArgb(231, 239, 248), Color.FromArgb(35, 92, 153), Accent, Accent);
            return;
        }

        _modeBadge.SetStatus(
            TaskStages.DisplayName(progress.Stage) + "中",
            meter,
            Color.FromArgb(255, 247, 225),
            Color.FromArgb(158, 104, 0),
            Color.FromArgb(214, 158, 46),
            Color.Empty);
    }

    private void UpdateClock(bool totalOnly = false)
    {
        if (_taskStartedAt is not { } started)
        {
            _taskClock.Text = "";
            return;
        }

        var total = Format(DateTime.Now - started);
        if (totalOnly || _stageStartedAt is not { } stageStarted)
        {
            _taskClock.Text = $"總共 {total}";
            return;
        }
        _taskClock.Text = $"本階段 {Format(DateTime.Now - stageStarted)} · 總共 {total}";

        static string Format(TimeSpan span) =>
            span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private void FlashTaskbar()
    {
        var info = new NativeMethods.FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.FLASHWINFO>(),
            hwnd = Handle,
            dwFlags = NativeMethods.FLASHW_ALL | NativeMethods.FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0
        };
        NativeMethods.FlashWindowEx(ref info);
    }

    private Task<PlanGateResponse> AskPlanGateAsync(PlanGatePrompt prompt, CancellationToken cancellationToken)
    {
        var response = InvokeRequired
            ? (PlanGateResponse?)Invoke(new Func<PlanGateResponse?>(() => ShowPlanGateDialog(prompt)))
            : ShowPlanGateDialog(prompt);

        if (response is null)
            throw new OperationCanceledException("使用者取消了 Plan Gate 討論。", cancellationToken);
        return Task.FromResult(response);
    }

    private PlanGateResponse? ShowPlanGateDialog(PlanGatePrompt prompt)
    {
        // 「等你回覆」的狀態由 ReportStage 在呼叫 askUser 之前就設好了，這裡只負責開視窗。
        using var dialog = new PlanGateDialog(prompt);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Response : null;
    }

    private IReadOnlyList<ProviderId> GetProviderCandidates()
    {
        var preferred = new[] { ProviderId.Codex, ProviderId.Antigravity, ProviderId.Claude };
        return preferred
            .Where(p => _providerCards.TryGetValue(p, out var card) &&
                        card.SessionEnabled &&
                        card.State == ProviderHealthState.Online)
            .ToList();
    }

    private void SetTaskRunning(bool running)
    {
        _taskRunning = running;
        _sendButton.Text = running ? "執行中…" : "送出";
        _projectButton.Enabled = !running;
        _projectBox.Enabled = !running;
        RefreshSendButton();
    }

    private void RefreshSendButton()
    {
        var canSend = !_taskRunning && !string.IsNullOrWhiteSpace(_requestBox.Text);
        _sendButton.Enabled = canSend;
        _sendButton.BackColor = canSend ? Accent : Color.FromArgb(181, 190, 200);
        _sendButton.Cursor = canSend ? Cursors.Hand : Cursors.Default;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_taskRunning && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(
                "目前任務仍在執行。確定要關閉 AITeam 並中止目前任務嗎？",
                "AITeam",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        _clockTimer.Stop();
        _lifetimeCts.Cancel();
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), text);
            return;
        }
        _outputBox.SelectionStart = _outputBox.TextLength;
        _outputBox.SelectionLength = 0;
        _outputBox.SelectionFont = _outputBox.Font;
        var line = text.Length == 0 ? Environment.NewLine : $"[{DateTime.Now:HH:mm:ss}] {text}\r\n";
        _outputBox.AppendText(line);
        _taskLog.Append(line);

        _outputBox.SelectionStart = _outputBox.TextLength;
        _outputBox.ScrollToCaret();
    }

    private void OpenTaskHistory()
    {
        using var form = new TaskHistoryForm(_taskHistory);
        form.ShowDialog(this);
    }

    private void SaveTaskHistory(TaskOutcome outcome, string subject, string result)
    {
        if (_taskStartedAt is not { } startedAt) return;

        try
        {
            _taskHistory.Save(
                new TaskHistoryEntry
                {
                    Id = startedAt.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6],
                    ProjectName = _taskProjectName,
                    Subject = string.IsNullOrWhiteSpace(subject) ? FallbackSubject(_taskRequest) : subject,
                    Request = _taskRequest,
                    Kind = _stageKind ?? TaskKind.Inquiry,
                    Outcome = outcome,
                    StartedAt = startedAt,
                    FinishedAt = DateTime.Now,
                    Result = result
                },
                _taskLog.ToString());
        }
        catch (Exception ex)
        {
            // 寫不進歷史紀錄不該讓剛跑完的任務看起來像失敗，只在 log 說一聲。
            AppendLog("[提醒] 這次任務的歷史紀錄寫入失敗：" + ex.Message);
        }
    }

    /// <summary>AI 沒給主旨時的退路：取使用者輸入的開頭當標題。</summary>
    private static string FallbackSubject(string request)
    {
        var firstLine = request
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? "";
        if (firstLine.Length == 0) return "（無標題任務）";
        return firstLine.Length <= 24 ? firstLine : firstLine[..24] + "…";
    }
}
