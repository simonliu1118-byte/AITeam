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
    private readonly IProcessRunner _runner;
    private readonly ProjectRegistryService _projectRegistry;
    private readonly ProviderHealthService _providerHealth;
    private readonly InquiryService _inquiryService;
    private readonly TaskHistoryService _taskHistory;
    private readonly AppPreferencesService _preferencesService;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly ComboBox _projectBox = new();
    private readonly Label _projectInfo = new();
    private readonly TextBox _requestBox = new();
    private readonly TextBox _currentTaskBox = new();
    private readonly RichTextBox _outputBox = new();
    private readonly LinkFoldingLog _outputLog;
    private readonly Button _recheckButton = new();
    private readonly Button _sendButton = new();
    private readonly Button _stopButton = new();
    private readonly CheckBox _pauseAfterImplement = new();
    private readonly CheckBox _applyNoteNow = new();
    private readonly Label _bottomHint = new();
    private readonly Button _projectButton = new();
    private readonly Button _historyButton = new();
    private readonly Button _meetingButton = new();
    private readonly Button _settingsButton = new();
    private readonly StatusBadge _modeBadge = new();
    private readonly StatusBadge _reviewBadge = new();
    private readonly ToolTip _reviewTip = new() { InitialDelay = 250, ShowAlways = true };
    private readonly Label _currentTaskState = new();
    private readonly Label _taskClock = new();
    private readonly TaskStageStrip _stageStrip = new();
    private readonly Label _stageDetail = new();
    // 「AI 現在在做什麼」的即時一行；跟階段說明分開，階段換了就清掉。
    private readonly AutoFitLabel _activityLabel = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1000 };
    private readonly Dictionary<ProviderId, ProviderStatusRow> _providerCards = new();

    private IReadOnlyList<ProjectEntry> _projects = Array.Empty<ProjectEntry>();
    private bool _taskRunning;
    // 這一次任務專用的取消來源；接在程式生命週期底下，關程式時也會一起取消。
    private CancellationTokenSource? _taskCts;
    // 這一輪任務的留言板；任務沒在跑的時候是 null。
    private TaskNoteBoard? _notes;
    private DateTime? _taskStartedAt;
    private DateTime? _stageStartedAt;
    private TaskProgress? _currentProgress;
    private TaskKind? _stageKind;
    private readonly System.Text.StringBuilder _taskLog = new();
    private string _taskRequest = "";
    private string _taskProjectName = "";
    // 即時活動可能一秒好幾行，太密的更新對畫面沒有幫助，節流到每 120ms 一次。
    private DateTime _lastActivityAt = DateTime.MinValue;

    public MainWindow(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
        _outputLog = new LinkFoldingLog(_outputBox);
        var runner = new ProcessRunner();
        _runner = runner;
        _projectRegistry = new ProjectRegistryService(runtimeRoot);
        _providerHealth = new ProviderHealthService(runtimeRoot, runner);
        _inquiryService = new InquiryService(runtimeRoot, runner);
        _taskHistory = new TaskHistoryService(runtimeRoot);
        _preferencesService = new AppPreferencesService(runtimeRoot);

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

        // 會議是跟「送任務」不同性質的功能，所以樣式也要不一樣：實心強調色的膠囊，
        // 放在產品名稱旁邊，而不是混在 AI 狀態那排普通按鈕裡。
        _meetingButton.Text = "＋ 發起會議";
        _meetingButton.AutoSize = false;
        _meetingButton.Size = new Size(118, 30);
        _meetingButton.FlatStyle = FlatStyle.Flat;
        _meetingButton.FlatAppearance.BorderSize = 0;
        _meetingButton.BackColor = Color.FromArgb(63, 81, 181);
        _meetingButton.ForeColor = Color.White;
        _meetingButton.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        _meetingButton.Cursor = Cursors.Hand;
        _meetingButton.Location = new Point(title.PreferredWidth + 34, 18);
        _meetingButton.Click += (_, _) => OpenMeeting();

        // 右上角固定顯示把關強度（完整／降級／受限）。詳情放 tooltip，不要把一長串
        // 說明塞在畫面上——字會被縮到看不清楚。
        _reviewBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _reviewBadge.BackColor = CardBackground;

        _settingsButton.Text = "設定";
        _settingsButton.AutoSize = false;
        _settingsButton.Size = new Size(72, 30);
        _settingsButton.FlatStyle = FlatStyle.Flat;
        _settingsButton.FlatAppearance.BorderColor = BorderColor;
        _settingsButton.BackColor = CardBackground;
        _settingsButton.ForeColor = SecondaryText;
        _settingsButton.Font = new Font("Microsoft JhengHei UI", 9.5F);
        _settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _settingsButton.Cursor = Cursors.Hand;
        _settingsButton.Click += (_, _) => OpenSettings();

        header.Controls.Add(title);
        header.Controls.Add(version);
        header.Controls.Add(_meetingButton);
        header.Controls.Add(_settingsButton);
        header.Controls.Add(_reviewBadge);

        void PositionBadge()
        {
            var badgeLeft = Math.Max(0, header.ClientSize.Width - _reviewBadge.Width - 22);
            _reviewBadge.Location = new Point(badgeLeft, 20);
            _settingsButton.Location = new Point(Math.Max(0, badgeLeft - _settingsButton.Width - 10), 20);
        }

        // 徽章寬度會隨文字變動，所以寬度變了也要重新靠右。
        header.Resize += (_, _) => PositionBadge();
        _reviewBadge.SizeChanged += (_, _) => PositionBadge();
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
            ColumnCount = 3,
            Margin = new Padding(0, 12, 2, 0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 左側是提示文字 + 一個依狀態切換的勾選框（待命時是檢查點，執行中是立刻套用）。
        var hintArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(2, 0, 10, 0),
            BackColor = AppBackground
        };
        hintArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // 兩個勾選框各佔一列（同時只會有一個是可見的）：TableLayoutPanel 一格只能放一個控制項，
        // 硬塞兩個會被擠到下一格去。隱藏的那一列是 AutoSize，不會佔到高度。
        hintArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        hintArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        hintArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 這一欄的寬度被兩顆按鈕吃掉不少，句子太長就會被截成「按「補充...」。
        _bottomHint.Text = "執行中可繼續輸入補充";
        _bottomHint.AutoSize = false;
        _bottomHint.AutoEllipsis = true;
        _bottomHint.Dock = DockStyle.Top;
        _bottomHint.Height = 20;
        _bottomHint.TextAlign = ContentAlignment.MiddleLeft;
        _bottomHint.ForeColor = SecondaryText;
        _bottomHint.Margin = Padding.Empty;
        hintArea.Controls.Add(_bottomHint, 0, 0);

        _pauseAfterImplement.Text = "實作完成後先讓我看過（只影響修改任務）";
        _pauseAfterImplement.AutoSize = true;
        _pauseAfterImplement.ForeColor = SecondaryText;
        _pauseAfterImplement.Font = new Font("Microsoft JhengHei UI", 9F);
        _pauseAfterImplement.Margin = new Padding(0, 2, 0, 0);
        hintArea.Controls.Add(_pauseAfterImplement, 0, 1);

        _applyNoteNow.Text = "立刻中斷目前這一步並套用補充";
        _applyNoteNow.AutoSize = true;
        _applyNoteNow.ForeColor = SecondaryText;
        _applyNoteNow.Font = new Font("Microsoft JhengHei UI", 9F);
        _applyNoteNow.Margin = new Padding(0, 2, 0, 0);
        _applyNoteNow.Visible = false;
        hintArea.Controls.Add(_applyNoteNow, 0, 2);

        bottom.Controls.Add(hintArea, 0, 0);

        _sendButton.Text = "送出";
        _sendButton.AutoSize = false;
        // 兩顆按鈕的尺寸、邊界與垂直對齊必須一致；先前「送出」沿用預設的 3px 邊界、
        // 又沒有指定 Anchor，就會跟旁邊的「停止」差半格。
        _sendButton.Size = new Size(108, 40);
        _sendButton.Anchor = AnchorStyles.Left;
        _sendButton.Margin = Padding.Empty;
        _sendButton.FlatStyle = FlatStyle.Flat;
        _sendButton.FlatAppearance.BorderSize = 0;
        _sendButton.BackColor = Accent;
        _sendButton.ForeColor = Color.White;
        _sendButton.Font = new Font("Microsoft JhengHei UI", 10.5F, FontStyle.Bold);
        _sendButton.Cursor = Cursors.Hand;
        _sendButton.Click += async (_, _) => await HandleSendAsync();

        _stopButton.Text = "停止";
        _stopButton.AutoSize = false;
        _stopButton.Size = new Size(108, 40);
        _stopButton.Anchor = AnchorStyles.Left;
        _stopButton.FlatStyle = FlatStyle.Flat;
        _stopButton.BackColor = Color.White;
        _stopButton.ForeColor = Color.FromArgb(176, 54, 54);
        _stopButton.FlatAppearance.BorderColor = Color.FromArgb(227, 195, 195);
        _stopButton.Font = new Font("Microsoft JhengHei UI", 10.5F, FontStyle.Bold);
        _stopButton.Margin = new Padding(0, 0, 10, 0);
        _stopButton.Click += (_, _) => StopCurrentTask();

        bottom.Controls.Add(_stopButton, 1, 0);
        bottom.Controls.Add(_sendButton, 2, 0);
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
        // 專案多的時候不要只露出前幾個，展開就一次看到（超過再捲）。
        _projectBox.MaxDropDownItems = 20;
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

        UpdateReviewModeLabel();
        return wrapper;
    }

    /// <summary>
    /// 現在按下去會是哪一種把關強度。這在送出之前就該看得到，不然使用者不知道
    /// 這次修改會不會有第二個 AI 幫忙看。
    /// </summary>
    private void UpdateReviewModeLabel()
    {
        var count = _providerCards.Values.Count(c => c.SessionEnabled && c.State == ProviderHealthState.Online);
        if (count == 0)
        {
            _reviewBadge.SetStatus(
                "沒有可用 AI", "",
                Color.FromArgb(240, 242, 245), Color.FromArgb(101, 108, 117),
                Color.FromArgb(145, 153, 163), Color.Empty);
            _reviewTip.SetToolTip(_reviewBadge, "目前沒有任何 AI 上線，按「重新檢查」再試一次。");
            return;
        }

        var mode = ReviewModeExtensions.ForProviderCount(count);
        var (fill, fore, dot) = mode switch
        {
            ReviewMode.Full => (Color.FromArgb(236, 248, 240), Color.FromArgb(36, 122, 72), Color.FromArgb(46, 160, 92)),
            ReviewMode.Degraded => (Color.FromArgb(255, 247, 225), Color.FromArgb(158, 104, 0), Color.FromArgb(214, 158, 46)),
            _ => (Color.FromArgb(251, 237, 236), Color.FromArgb(176, 54, 54), Color.FromArgb(194, 58, 52))
        };

        _reviewBadge.SetStatus(mode.ToFriendlyName(), "", fill, fore, dot, Color.Empty);
        _reviewTip.SetToolTip(_reviewBadge, $"{count} 個 AI 可用。{mode.Describe()}");
    }

    private void AddProviderRow(TableLayoutPanel grid, int row, ProviderId provider, string name)
    {
        var status = new ProviderStatusRow(provider, name);
        // 勾選會改變「這一輪有幾個 AI 可用」，把關強度要跟著重算。
        status.SessionEnabledChanged += (_, _) => UpdateReviewModeLabel();
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        parent.Controls.Add(layout);

        // 欄位順序：標題｜狀態徽章｜彈性空白｜歷史任務。徽章緊跟在標題後面（留一點間距），
        // 而不是被彈性欄推到最右邊跟按鈕擠在一起。
        var currentTitleRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = Padding.Empty };
        currentTitleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        currentTitleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        currentTitleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        currentTitleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // 標題和徽章高度不一樣（文字約 19px、徽章 30px），兩個都用 Anchor = Left 讓
        // TableLayoutPanel 各自垂直置中、上下邊界也一致，中心線才會落在同一條水平線上。
        // 標題若沿用上邊界 4px，它會被往下推、徽章卻是置中的，看起來就沒對齊。
        var currentTitle = SectionTitle("目前任務", new Padding(2, 0, 0, 7));
        currentTitle.Anchor = AnchorStyles.Left;
        currentTitleRow.Controls.Add(currentTitle, 0, 0);

        // 待命／執行中講的是「目前任務」的狀態，就放在它旁邊；右上角讓給把關強度。
        _modeBadge.BackColor = AppBackground;
        _modeBadge.Anchor = AnchorStyles.Left;
        _modeBadge.Margin = new Padding(12, 0, 0, 7);
        currentTitleRow.Controls.Add(_modeBadge, 1, 0);

        _historyButton.Text = "歷史任務";
        _historyButton.AutoSize = false;
        _historyButton.Size = new Size(94, 30);
        _historyButton.FlatStyle = FlatStyle.Flat;
        _historyButton.BackColor = Color.White;
        _historyButton.ForeColor = PrimaryText;
        _historyButton.Anchor = AnchorStyles.Right;
        _historyButton.Margin = new Padding(0, 0, 2, 7);
        _historyButton.FlatAppearance.BorderColor = BorderColor;
        _historyButton.Click += (_, _) => OpenTaskHistory();
        currentTitleRow.Controls.Add(_historyButton, 3, 0);
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
            RowCount = 5,
            BackColor = Color.Transparent
        };
        currentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        currentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        _currentTaskState.Visible = false;
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

        _activityLabel.Dock = DockStyle.Top;
        _activityLabel.Height = 17;
        _activityLabel.Font = new Font("Microsoft JhengHei UI", 8.5F);
        _activityLabel.ForeColor = Color.FromArgb(133, 142, 152);
        _activityLabel.BackColor = CardBackground;
        _activityLabel.Margin = new Padding(0, 1, 0, 0);
        _activityLabel.Visible = false;
        currentLayout.Controls.Add(_activityLabel, 0, 4);

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
        var result = form.ShowDialog(this);

        // 不論是按「完成」還是直接關視窗，都重新載入一次。之前只在 DialogResult.OK
        // 時才重載，使用者用右上角 X 關掉時新增的專案就不會出現在上面的下拉選單裡。
        LoadProjects(form.SavedProjectName ?? selected?.Name);
        if (result == DialogResult.OK) AppendLog("專案登錄已更新。");
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
        // 三家都檢查，包含這一輪沒有要用的。狀態是狀態、用不用是用不用，兩件事分開。
        foreach (var provider in Enum.GetValues<ProviderId>())
            _providerCards[provider].SetHealth(ProviderHealth.Checking(provider));

        var tasks = Enum.GetValues<ProviderId>()
            .ToDictionary(p => p, p => _providerHealth.ProbeAsync(p, _lifetimeCts.Token));

        foreach (var item in tasks)
        {
            var health = await item.Value;
            _providerCards[item.Key].SetHealth(health);
            AppendLog($"{item.Key.ToFriendlyName()}：{health.State.ToFriendlyName()} ({health.Duration.TotalSeconds:0.0}s)");
            // 只要 CLI 有話說就寫進紀錄。卡片上只有「錯誤」兩個字，看不出到底是額度用完、
            // 沒登入還是參數不合，等於無從查起；一次檢查成功但中途換過參數時也要留痕跡。
            UpdateReviewModeLabel();
            if (!string.IsNullOrWhiteSpace(health.Detail))
                AppendLog($"    └ CLI 回報：{health.Detail}");
        }
        _recheckButton.Enabled = true;
        UpdateReviewModeLabel();
        AppendLog("AI 檢查完成。");
    }

    /// <param name="presetRequest">
    /// 非 null 代表這次任務不是從輸入框送出的（目前是會議結論交接過來）。
    /// </param>
    /// <param name="background">
    /// 一併交給 AI 的背景（會議結論）。它只影響送給 AI 的內容，不會顯示在「目前任務」裡——
    /// 那一格要留給使用者自己講的那句需求。
    /// </param>
    private async Task HandleSendAsync(string? presetRequest = null, string? background = null)
    {
        if (presetRequest is null && string.IsNullOrWhiteSpace(_requestBox.Text)) return;
        if (_taskRunning)
        {
            if (presetRequest is null) AddNoteToRunningTask();
            return;
        }

        if (_projectBox.SelectedItem is not ProjectEntry project)
        {
            MessageBox.Show("請先選擇專案。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var candidates = GetProviderCandidates();
        if (!PassesPreflight(project, candidates.Count)) return;

        var request = presetRequest ?? _requestBox.Text.Trim();
        _currentTaskBox.Text = request;
        if (presetRequest is null) _requestBox.Clear();
        _taskRequest = request;
        _taskProjectName = project.Name;
        _taskLog.Clear();
        StartTaskProgress();
        SetTaskRunning(true);
        _outputLog.Clear();
        LogPreflightWarnings(project, candidates.Count);

        _taskCts?.Dispose();
        _taskCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _notes = new TaskNoteBoard();
        var interaction = new TaskInteraction(
            _notes, _pauseAfterImplement.Checked, AskCheckpointAsync, AskMergeApprovalAsync);

        try
        {
            var result = await _inquiryService.RunAsync(
                project,
                ComposeRequest(request, background),
                candidates,
                AskPlanGateAsync,
                text => AppendLog(text),
                ReportStage,
                ReportActivity,
                interaction,
                _taskCts.Token);

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

    /// <summary>
    /// 會議結論當成背景接在需求後面。講明它已經被使用者確認過，讓 Plan Gate 不必把
    /// 討論過的東西再問一次。
    /// </summary>
    private static string ComposeRequest(string request, string? background)
    {
        if (string.IsNullOrWhiteSpace(background)) return request;

        // 背景怎麼介紹自己，由 MeetingConclusion.ComposeBackground 決定：
        // 收斂過的定案書跟沒收斂的討論摘要，說法必須不一樣，不然就是在騙 Planner。
        return request + Environment.NewLine + Environment.NewLine + background;
    }

    /// <summary>
    /// 送出前先檢查。有擋下來的問題就直接說清楚並停住，不要等到 AI 額度燒掉一半才發現。
    /// </summary>
    private bool PassesPreflight(ProjectEntry project, int onlineProviderCount)
    {
        var blockers = ProjectPreflight.Inspect(project, onlineProviderCount)
            .Where(issue => issue.Severity == PreflightSeverity.Blocker)
            .ToList();
        if (blockers.Count == 0) return true;

        MessageBox.Show(
            "這次任務無法送出：\r\n\r\n" +
            string.Join("\r\n\r\n", blockers.Select(issue => $"● {issue.Title}\r\n　 {issue.Detail}")),
            "送出前檢查",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private void LogPreflightWarnings(ProjectEntry project, int onlineProviderCount)
    {
        foreach (var issue in ProjectPreflight.Inspect(project, onlineProviderCount))
        {
            if (issue.Severity != PreflightSeverity.Warning) continue;
            AppendLog($"⚠️ {issue.Title}：{issue.Detail}");
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
        _activityLabel.Text = "";
        _activityLabel.Visible = true;
        _lastActivityAt = DateTime.MinValue;
        _stageStrip.Visible = true;
        _stageStrip.Clear();
        _stageKind = null;
        _modeBadge.SetStatus("執行中", "", Color.FromArgb(255, 247, 225), Color.FromArgb(158, 104, 0), Color.FromArgb(214, 158, 46), Color.Empty);
        UpdateClock();
        _clockTimer.Start();
    }

    /// <summary>
    /// Core 回報「AI 這一秒在做什麼」的進入點，來自讀取 CLI 輸出的背景執行緒。
    /// 只更新畫面上那一行，不寫進執行紀錄——這種訊息量太大，寫進去會把紀錄洗掉。
    /// </summary>
    private void ReportActivity(string text)
    {
        if (InvokeRequired)
        {
            // 節流放在背景執行緒這一側：丟到 UI 執行緒之後會再進來一次，
            // 那一次不能被自己剛剛寫下的時間戳擋掉。
            var now = DateTime.UtcNow;
            if ((now - _lastActivityAt).TotalMilliseconds < 120) return;
            _lastActivityAt = now;

            BeginInvoke(new Action<string>(ReportActivity), text);
            return;
        }

        if (!_taskRunning) return;
        _activityLabel.Text = text;
        _activityLabel.Visible = true;
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
        // 換階段了，上一個階段的即時動作就不要再留在畫面上。
        _activityLabel.Text = "";

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
        _activityLabel.Text = "";
        _activityLabel.Visible = false;
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
        _activityLabel.Text = "";
        _activityLabel.Visible = false;
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
        // 執行中「送出」變成「補充說明」：任務跑到一半還是可以把話加進去，
        // 這是定案之後使用者唯一能插嘴的管道。
        _sendButton.Text = running ? "補充說明" : "送出";
        _projectButton.Enabled = !running;
        _projectBox.Enabled = !running;
        _stopButton.Enabled = running;
        _stopButton.Cursor = running ? Cursors.Hand : Cursors.Default;
        _pauseAfterImplement.Visible = !running;
        _applyNoteNow.Visible = running;
        _bottomHint.Text = running ? "輸入補充後按「補充說明」" : "執行中可繼續輸入補充";
        if (!running)
        {
            _notes = null;
            _applyNoteNow.Checked = false;
        }
        RefreshSendButton();
    }

    /// <summary>
    /// 把使用者在任務執行中打的話交給留言板。沒勾「立刻套用」就排隊，等下一步派工前併進提示；
    /// 勾了就中斷目前這一步、帶著補充重來。
    /// </summary>
    private void AddNoteToRunningTask()
    {
        var note = _requestBox.Text.Trim();
        if (note.Length == 0 || _notes is null) return;

        var immediate = _applyNoteNow.Checked;
        _notes.Add(note, immediate);
        _requestBox.Clear();

        AppendLog(immediate
            ? $"已收到你的補充，立刻中斷目前這一步並套用：{note}"
            : $"已收到你的補充，下一步開始前會一併交給 AI：{note}");
    }

    /// <summary>實作完成後的確認點；只有送出時勾了「先讓我看過」才會被呼叫。</summary>
    private Task<CheckpointResponse> AskCheckpointAsync(CheckpointPrompt prompt, CancellationToken cancellationToken)
    {
        var response = InvokeRequired
            ? (CheckpointResponse?)Invoke(new Func<CheckpointResponse?>(() => ShowCheckpointDialog(prompt)))
            : ShowCheckpointDialog(prompt);

        // 關掉視窗而不按任何按鈕，等於什麼都不決定；當成繼續比當成中止安全。
        return Task.FromResult(response ?? new CheckpointResponse(CheckpointAction.Continue));
    }

    private CheckpointResponse? ShowCheckpointDialog(CheckpointPrompt prompt)
    {
        using var dialog = new CheckpointDialog(prompt);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Response : null;
    }

    /// <summary>高風險變更的人工合併關卡，現在在 AITeam 裡完成，不必再開瀏覽器。</summary>
    private Task<MergeDecision> AskMergeApprovalAsync(MergeApprovalPrompt prompt, CancellationToken cancellationToken)
    {
        var decision = InvokeRequired
            ? (MergeDecision?)Invoke(new Func<MergeDecision?>(() => ShowMergeApprovalDialog(prompt)))
            : ShowMergeApprovalDialog(prompt);

        // 視窗被直接關掉等於沒有做決定，當成「暫不合併」——不合併是可以反悔的，合併不行。
        return Task.FromResult(decision ?? MergeDecision.Skip);
    }

    private MergeDecision? ShowMergeApprovalDialog(MergeApprovalPrompt prompt)
    {
        using var dialog = new MergeApprovalDialog(prompt);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Decision : null;
    }

    /// <summary>
    /// 中止目前任務。取消會一路傳到底層，連正在跑的 CLI 子行程都會被結束，
    /// 所以按下去是真的停，不是只把畫面切回待命。
    /// </summary>
    private void StopCurrentTask()
    {
        if (!_taskRunning || _taskCts is null) return;

        var answer = MessageBox.Show(
            "確定要中止目前任務嗎？\r\n\r\n正在執行的 AI 會被結束。如果已經改過檔案，變更會保留在隔離工作區裡，不會合併也不會影響你的本機專案。",
            "中止任務",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        AppendLog("使用者要求中止任務，正在結束執行中的 AI…");
        _stopButton.Enabled = false;
        try
        {
            _taskCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 任務剛好在這瞬間結束了，不需要處理。
        }
    }

    private void RefreshSendButton()
    {
        // 執行中一樣可以按（那時候是「補充說明」），所以只看有沒有打字。
        var canSend = !string.IsNullOrWhiteSpace(_requestBox.Text);
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
        var line = text.Length == 0 ? Environment.NewLine : $"[{DateTime.Now:HH:mm:ss}] {text}\r\n";
        // 畫面上把參照折成檔名（完整路徑用 tooltip 顯示），但存進歷史紀錄的是原文，
        // 路徑不會因為畫面好看而被丟掉。
        _outputLog.Append(line);
        _taskLog.Append(line);
        _outputLog.ScrollToEnd();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_preferencesService, _preferencesService.Load());
        form.ShowDialog(this);
    }

    /// <summary>
    /// 開 AI 四方會議。會議跟主畫面的任務完全分開，開著會議也照樣可以送任務。
    /// </summary>
    private void OpenMeeting()
    {
        var participants = GetProviderCandidates();
        if (participants.Count == 0)
        {
            MessageBox.Show("目前沒有可用的 AI。請先按「重新檢查」。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 非強制回應：開著會議的同時還可以在主畫面送任務。
        var form = new MeetingForm(_runtimeRoot, _runner, _taskHistory, _projects, participants);
        // 用 BeginInvoke 延到會議視窗真的關掉之後才處理，否則交接視窗會卡在
        // 會議視窗前面、而會議視窗還沒關。
        form.SendToPipelineRequested += (_, conclusion) =>
            BeginInvoke(new Action(async () => await HandOffMeetingAsync(conclusion)));
        form.FormClosed += (_, _) => form.Dispose();
        form.Show(this);
    }

    /// <summary>
    /// 會議談完之後把結論送進修改管線。要對哪個專案做由使用者在這裡決定——會議可以是
    /// 純討論、沒有綁專案，而且討論完才想新增一個專案也很正常。
    /// </summary>
    private async Task HandOffMeetingAsync(MeetingConclusion conclusion)
    {
        if (_taskRunning)
        {
            MessageBox.Show(
                "目前還有任務在執行，請等它結束或按「停止」之後再送出。",
                "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new MeetingHandoffDialog(conclusion, _projects);
        dialog.ManageProjects = () =>
        {
            OpenProjectManager();
            return _projects;
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Project is null) return;

        SelectProject(dialog.Project.Name);
        // 送出去的是使用者在交接視窗裡看過、也可能改過的那一份，不是會議自己產生的原文。
        var agreed = conclusion with { Text = dialog.Conclusion };
        await HandleSendAsync(dialog.Request, agreed.ComposeBackground());
    }

    private void SelectProject(string name)
    {
        for (var i = 0; i < _projectBox.Items.Count; i++)
        {
            if ((_projectBox.Items[i] as ProjectEntry)?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) != true) continue;
            _projectBox.SelectedIndex = i;
            return;
        }
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
