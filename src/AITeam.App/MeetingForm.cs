using AITeam.Models;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// AI 四方會議。三家 AI 加上使用者針對同一個議題討論，一輪一輪進行，
/// 每輪結束都輪到使用者決定要繼續、收斂、定案還是結束。
/// </summary>
public sealed class MeetingForm : Form
{
    private static readonly Color AppBackground = Color.FromArgb(244, 247, 250);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(190, 199, 210);
    private static readonly Color PrimaryText = Color.FromArgb(34, 40, 49);
    private static readonly Color SecondaryText = Color.FromArgb(104, 113, 123);
    private static readonly Color Accent = Color.FromArgb(43, 108, 176);

    /// <summary>超過這個次數就提醒一次，免得一場會不知不覺燒掉一堆額度。</summary>
    private const int CallWarningThreshold = 12;

    private readonly MeetingService _meetings;
    private readonly TaskHistoryService _history;
    private readonly IReadOnlyList<ProjectEntry> _projects;
    private readonly IReadOnlyList<ProviderId> _participants;

    private readonly TextBox _topicBox = new();
    private readonly ComboBox _projectBox = new();
    private readonly ComboBox _modeBox = new();
    private readonly ComboBox _scaleBox = new();
    private readonly Button _startButton = new();
    private readonly Panel _setupPanel = new();
    private readonly Panel _briefPanel = new();
    private readonly Label _briefTopic = new();
    private readonly Label _briefMeta = new();
    private readonly ComboBox _briefScaleBox = new();

    private readonly Label _statusLabel = new();
    private readonly RichTextBox _transcriptBox = new();
    private readonly LinkFoldingLog _transcript;
    private readonly TextBox _sayBox = new();
    private readonly Button _nextRoundButton = new();
    private readonly Button _concludeButton = new();
    private readonly Button _decideButton = new();
    private readonly Button _handoffButton = new();
    private readonly Button _endButton = new();
    private readonly Button _interjectButton = new();
    private readonly Button _skipSpeakerButton = new();
    private readonly Button _stopRoundButton = new();
    private readonly System.Windows.Forms.Timer _softTimer = new() { Interval = 1000 };

    private readonly List<MeetingRemark> _transcriptEntries = new();
    private MeetingSetup? _setup;
    private MeetingScale _scale = MeetingScale.Standard;
    private int _round;
    private int _calls;
    private bool _running;
    private bool _warnedAboutCalls;
    private DateTime _speakerStartedAt;
    private TimeSpan _softTimeout = TimeSpan.FromMinutes(3);
    private CancellationTokenSource? _speakerCts;
    private CancellationTokenSource? _roundCts;
    private ProviderId? _currentSpeaker;
    // 使用者按了「立刻插話」：砍掉目前這位講到一半的內容，帶著你的話請同一位重講。
    private bool _interjecting;
    // 正在請某一家把會議寫成定案書。這期間只留「停止」，插話跟跳過都沒有意義。
    private bool _drafting;
    private CancellationTokenSource? _draftCts;
    // 寫出來的定案書。歷史紀錄要存這一份，不是程式湊出來的立場並排。
    private MeetingConclusion? _decision;
    // 「正在發言…」那段在逐字稿裡的起點，收到真正的發言後從這裡整段換掉。
    private int _pendingMark = -1;
    private string _lastActivity = "";
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DateTime _startedAt = DateTime.Now;

    /// <summary>
    /// 使用者按「送去執行」時發出。會議只負責談出結論；要對哪個專案做、實際送出什麼需求，
    /// 由主畫面接手處理（它才有專案登錄與新增專案的能力）。
    /// </summary>
    public event EventHandler<MeetingConclusion>? SendToPipelineRequested;

    public MeetingForm(
        string runtimeRoot,
        IProcessRunner runner,
        TaskHistoryService history,
        IReadOnlyList<ProjectEntry> projects,
        IReadOnlyList<ProviderId> participants)
    {
        _meetings = new MeetingService(runtimeRoot, runner);
        _history = history;
        _projects = projects;
        _participants = participants;
        _transcript = new LinkFoldingLog(_transcriptBox);

        Text = "AITeam - AI 四方會議";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 660);
        Size = new Size(1040, 760);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = AppBackground;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
        _softTimer.Tick += (_, _) => CheckSoftTimeout();
        FormClosing += OnFormClosing;
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        shell.Controls.Add(BuildSetupCard(), 0, 0);
        shell.Controls.Add(BuildStatusRow(), 0, 1);
        shell.Controls.Add(BuildTranscriptCard(), 0, 2);
        shell.Controls.Add(BuildChairRow(), 0, 3);
        UpdateControls();
    }

    private Control BuildSetupCard()
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        // 同一張卡片裡有兩個面板：還沒開始時是設定表單，開始之後換成置頂的議題摘要。
        // 議題在整場會議裡都要看得到，而設定一旦開始就不該再被改。
        var host = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = CardBackground };
        host.Controls.Add(BuildBriefPanel());
        host.Controls.Add(BuildSetupPanel());
        card.Controls.Add(host);
        return card;
    }

    private Control BuildSetupPanel()
    {
        _setupPanel.Dock = DockStyle.Top;
        _setupPanel.AutoSize = true;
        _setupPanel.BackColor = CardBackground;

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = CardBackground
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        body.Controls.Add(MakeFieldLabel("議題（要討論什麼）"), 0, 0);

        _topicBox.Dock = DockStyle.Top;
        _topicBox.Multiline = true;
        _topicBox.Height = 54;
        _topicBox.ScrollBars = ScrollBars.Vertical;
        _topicBox.Margin = new Padding(0, 4, 0, 8);
        _topicBox.PlaceholderText = "例如：CYAccounting 的資料要不要改用 SQLite 以外的東西？";
        _topicBox.TextChanged += (_, _) => UpdateControls();
        body.Controls.Add(_topicBox, 0, 1);

        // 一列裡有標籤、三個下拉和一顆按鈕，高度各自不同。全部靠 Anchor = Left 垂直置中、
        // 上下邊界留 0，它們才會落在同一條水平線上（先前標籤多給了上邊界，就會偏低）。
        var options = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 7, BackColor = CardBackground };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureCombo(_projectBox);
        _projectBox.Items.Add("不指定（純討論）");
        foreach (var project in _projects) _projectBox.Items.Add(project.Name);
        _projectBox.SelectedIndex = 0;

        ConfigureCombo(_modeBox);
        _modeBox.Items.AddRange(new object[] { "輪流發言（會收斂）", "各自作答（會發散）" });
        _modeBox.SelectedIndex = 0;

        ConfigureCombo(_scaleBox);
        _scaleBox.Items.AddRange(ScaleChoices());
        _scaleBox.SelectedIndex = 1;
        _scaleBox.SelectedIndexChanged += (_, _) => ApplyScale((MeetingScale)_scaleBox.SelectedIndex);

        options.Controls.Add(MakeInlineLabel("專案"), 0, 0);
        options.Controls.Add(_projectBox, 1, 0);
        options.Controls.Add(MakeInlineLabel("模式"), 2, 0);
        options.Controls.Add(_modeBox, 3, 0);
        options.Controls.Add(MakeInlineLabel("規模"), 4, 0);
        options.Controls.Add(_scaleBox, 5, 0);

        ConfigurePrimaryButton(_startButton, "開始會議", 110);
        // 按鈕高度跟下拉一致，才不會一顆比旁邊高出一截。
        _startButton.Height = _projectBox.PreferredHeight + 2;
        _startButton.Anchor = AnchorStyles.Left;
        _startButton.Click += async (_, _) => await StartMeetingAsync();
        options.Controls.Add(_startButton, 6, 0);

        body.Controls.Add(options, 0, 2);
        _setupPanel.Controls.Add(body);
        return _setupPanel;
    }

    private Control BuildBriefPanel()
    {
        _briefPanel.Dock = DockStyle.Top;
        _briefPanel.AutoSize = true;
        _briefPanel.BackColor = CardBackground;
        _briefPanel.Visible = false;

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 3,
            BackColor = CardBackground
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var caption = MakeFieldLabel("議題");
        body.Controls.Add(caption, 0, 0);

        body.Controls.Add(MakeInlineLabel("規模"), 1, 0);
        ConfigureCombo(_briefScaleBox);
        _briefScaleBox.Width = 150;
        _briefScaleBox.Dock = DockStyle.None;
        _briefScaleBox.Anchor = AnchorStyles.Left;
        _briefScaleBox.Margin = new Padding(0, 0, 0, 0);
        _briefScaleBox.Items.AddRange(ScaleChoices());
        _briefScaleBox.SelectedIndex = 1;
        _briefScaleBox.SelectedIndexChanged += (_, _) => ApplyScale((MeetingScale)_briefScaleBox.SelectedIndex);
        body.Controls.Add(_briefScaleBox, 2, 0);

        _briefTopic.AutoSize = true;
        _briefTopic.MaximumSize = new Size(880, 0);
        _briefTopic.Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold);
        _briefTopic.ForeColor = PrimaryText;
        _briefTopic.Margin = new Padding(2, 4, 0, 8);
        body.SetColumnSpan(_briefTopic, 3);
        body.Controls.Add(_briefTopic, 0, 1);

        _briefMeta.AutoSize = true;
        _briefMeta.MaximumSize = new Size(880, 0);
        _briefMeta.Font = new Font("Microsoft JhengHei UI", 9F);
        _briefMeta.ForeColor = SecondaryText;
        _briefMeta.Margin = new Padding(2, 0, 0, 0);
        body.SetColumnSpan(_briefMeta, 3);
        body.Controls.Add(_briefMeta, 0, 2);

        _briefPanel.Controls.Add(body);
        return _briefPanel;
    }

    private static object[] ScaleChoices() =>
        new object[] { "簡短（約300字）", "標準（約800字）", "深入（約2000字）" };

    /// <summary>兩個規模下拉（設定區、置頂摘要）要一起跟著走，不能各講各的。</summary>
    private void ApplyScale(MeetingScale scale)
    {
        if (_scale == scale) return;
        _scale = scale;
        if (_scaleBox.SelectedIndex != (int)scale) _scaleBox.SelectedIndex = (int)scale;
        if (_briefScaleBox.SelectedIndex != (int)scale) _briefScaleBox.SelectedIndex = (int)scale;
        UpdateBrief();
    }

    private void UpdateBrief()
    {
        if (_setup is null) return;

        _briefTopic.Text = _setup.Topic;
        _briefMeta.Text =
            $"參與者：{string.Join("、", _participants.Select(p => p.ToFriendlyName()))}"
            + $"　·　{(_setup.Mode == MeetingMode.RoundRobin ? "輪流發言" : "各自作答")}"
            + $"　·　規模：{_scale.ToFriendlyName()}（約 {_scale.WordBudget()} 字）"
            + $"　·　專案：{_setup.Project?.Name ?? "不指定"}"
            + $"　·　已呼叫 {_calls} 次";
    }


    private Control BuildStatusRow()
    {
        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Height = 22;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = SecondaryText;
        _statusLabel.Font = new Font("Microsoft JhengHei UI", 9F);
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Margin = new Padding(2, 0, 0, 6);
        return _statusLabel;
    }

    private Control BuildTranscriptCard()
    {
        var card = new RoundedCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackground,
            BorderColor = BorderColor,
            Radius = 10,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 8)
        };

        _transcriptBox.Dock = DockStyle.Fill;
        _transcriptBox.ReadOnly = true;
        _transcriptBox.BorderStyle = BorderStyle.None;
        _transcriptBox.BackColor = CardBackground;
        _transcriptBox.ForeColor = Color.FromArgb(55, 62, 70);
        _transcriptBox.Font = new Font("Microsoft JhengHei UI", 9F);
        card.Controls.Add(_transcriptBox);
        return card;
    }


    private Control BuildChairRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        row.Controls.Add(MakeFieldLabel("你的發言（隨時可以打字；下一位開口前會自動插進去）"), 0, 0);

        _sayBox.Dock = DockStyle.Top;
        _sayBox.Multiline = true;
        _sayBox.Height = 56;
        _sayBox.ScrollBars = ScrollBars.Vertical;
        _sayBox.Margin = new Padding(0, 4, 0, 8);
        // 有人在發言的時候也照樣可以打字——這就是「插話」的前提。
        _sayBox.TextChanged += (_, _) => UpdateInterjectButton();
        row.Controls.Add(_sayBox, 0, 1);

        // 一列六顆按鈕，但同一時間只有一組用得上：有人在發言時只剩「跳過這家／停止這一輪」，
        // 輪到使用者時只剩另外四顆。用顯示／隱藏切換，不必再多一條控制列。
        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 9 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 8; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        ConfigureSecondaryButton(_interjectButton, "立刻插話", 100);
        _interjectButton.Margin = new Padding(0, 0, 8, 0);
        _interjectButton.Visible = false;
        _interjectButton.Click += (_, _) => InterjectNow();
        buttons.Controls.Add(_interjectButton, 1, 0);

        ConfigureSecondaryButton(_skipSpeakerButton, "跳過這家", 100);
        _skipSpeakerButton.Margin = new Padding(0, 0, 8, 0);
        _skipSpeakerButton.Visible = false;
        _skipSpeakerButton.Click += (_, _) => SkipCurrentSpeaker();
        buttons.Controls.Add(_skipSpeakerButton, 2, 0);

        ConfigureSecondaryButton(_stopRoundButton, "停止這一輪", 110);
        _stopRoundButton.Margin = new Padding(0, 0, 8, 0);
        _stopRoundButton.ForeColor = Color.FromArgb(176, 54, 54);
        _stopRoundButton.FlatAppearance.BorderColor = Color.FromArgb(227, 195, 195);
        _stopRoundButton.Visible = false;
        _stopRoundButton.Click += (_, _) => StopRound();
        buttons.Controls.Add(_stopRoundButton, 3, 0);

        ConfigureSecondaryButton(_endButton, "結束會議", 100);
        _endButton.Margin = new Padding(0, 0, 8, 0);
        _endButton.Click += async (_, _) => await EndMeetingAsync(TaskOutcome.Completed);
        buttons.Controls.Add(_endButton, 4, 0);

        ConfigureSecondaryButton(_decideButton, "直接定案", 100);
        _decideButton.Margin = new Padding(0, 0, 8, 0);
        _decideButton.Click += async (_, _) => await DecideAsync();
        buttons.Controls.Add(_decideButton, 5, 0);

        ConfigureSecondaryButton(_handoffButton, "送去執行", 100);
        _handoffButton.Margin = new Padding(0, 0, 8, 0);
        _handoffButton.Click += async (_, _) => await HandOffAsync();
        buttons.Controls.Add(_handoffButton, 6, 0);

        ConfigureSecondaryButton(_concludeButton, "請 AI 收斂結論", 130);
        _concludeButton.Margin = new Padding(0, 0, 8, 0);
        _concludeButton.Click += async (_, _) => await RunRoundAsync(concluding: true);
        buttons.Controls.Add(_concludeButton, 7, 0);

        ConfigurePrimaryButton(_nextRoundButton, "繼續下一輪", 130);
        _nextRoundButton.Click += async (_, _) => await RunRoundAsync(concluding: false);
        buttons.Controls.Add(_nextRoundButton, 8, 0);

        row.Controls.Add(buttons, 0, 2);
        return row;
    }

    private async Task StartMeetingAsync()
    {
        var topic = _topicBox.Text.Trim();
        if (topic.Length == 0) return;

        if (_participants.Count == 0)
        {
            MessageBox.Show("目前沒有可用的 AI，無法開會。請先回主畫面按「重新檢查」。",
                "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var project = _projectBox.SelectedIndex > 0 ? _projects[_projectBox.SelectedIndex - 1] : null;
        _setup = new MeetingSetup(topic, project, (MeetingMode)_modeBox.SelectedIndex, _scale);

        _running = true;
        UpdateControls();
        try
        {
            await _meetings.PrepareAsync(project, AppendSystemLine, _lifetime.Token);
        }
        catch (Exception ex)
        {
            _setup = null;
            _running = false;
            UpdateControls();
            MessageBox.Show(ex.Message, "無法開始會議", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _transcript.Clear();
        _transcriptEntries.Clear();

        // 議題與參與者改成置頂顯示，不再當成逐字稿的第一則訊息——那會被後面的發言捲走。
        _setupPanel.Visible = false;
        _briefPanel.Visible = true;
        UpdateBrief();

        _running = false;
        await RunRoundAsync(concluding: false);
    }

    private async Task RunRoundAsync(bool concluding)
    {
        if (_setup is null || _running) return;

        if (!ConfirmCallBudget()) return;

        _running = true;
        UpdateControls();
        try
        {
            RecordUserRemark(concluding);

            _roundCts?.Dispose();
            _roundCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

            _round++;
            foreach (var speaker in MeetingService.SpeakingOrder(_participants, _round))
            {
                if (_roundCts.IsCancellationRequested)
                {
                    AppendSystemLine("這一輪已停止，剩下的 AI 不再發言。");
                    return;
                }

                // 上一位在講的時候你打的字，趕在下一位開口之前插進逐字稿，
                // 這樣它一開口看到的就已經包含你的話了。
                FlushPendingRemark();

                // 你按「立刻插話」時會回 true：同一位帶著你的話重講一次。
                while (await AskOneAsync(speaker, concluding)) { }
            }
        }
        finally
        {
            _running = false;
            UpdateControls();
        }
    }

    private void RecordUserRemark(bool concluding)
    {
        var said = _sayBox.Text.Trim();
        if (concluding)
        {
            said = said.Length == 0
                ? "請不要再發散了，請把目前的討論收斂成一個明確的結論與建議做法。"
                : said + Environment.NewLine + "另外：請把目前的討論收斂成一個明確的結論與建議做法。";
        }

        AddUserRemark(said);
    }

    /// <summary>把留言板上還沒送出的字當成一則使用者發言，插進逐字稿。</summary>
    private void FlushPendingRemark() => AddUserRemark(_sayBox.Text.Trim());

    private void AddUserRemark(string said)
    {
        if (said.Length == 0) return;

        var remark = new MeetingRemark(_round, null, said);
        _transcriptEntries.Add(remark);
        AppendRemark(remark);
        _sayBox.Clear();
    }

    /// <summary>
    /// 請一位 AI 發言。回傳 true 代表使用者中途插話、這一位要帶著新的話重講一次。
    /// </summary>
    private async Task<bool> AskOneAsync(ProviderId speaker, bool concluding)
    {
        _speakerCts?.Dispose();
        _speakerCts = CancellationTokenSource.CreateLinkedTokenSource(_roundCts!.Token);
        _currentSpeaker = speaker;
        _lastActivity = "";
        _speakerStartedAt = DateTime.UtcNow;
        // Gemini / Antigravity 光是啟動就比另外兩家慢很多（健康檢查 28 秒 vs 6～8 秒），
        // 用同一個 3 分鐘門檻會一直誤報「可能卡住了」，讓人以為它壞掉。
        _softTimeout = speaker == ProviderId.Antigravity ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(3);
        ShowTurnPlaceholder(speaker);
        _softTimer.Start();

        try
        {
            _calls++;
            UpdateBrief();
            var remark = await _meetings.AskAsync(
                speaker,
                _setup! with { Mode = concluding ? MeetingMode.RoundRobin : _setup!.Mode },
                _transcriptEntries,
                _round,
                _scale,
                // 硬逾時跟著規模走：簡短 5 分鐘、標準 10 分鐘、深入 15 分鐘。
                _scale.HardTimeout(),
                ReportActivity,
                _speakerCts.Token);

            _transcriptEntries.Add(remark);
            ReplacePlaceholderWith(remark);
            if (remark.SuggestedScale is { } suggested && suggested != _scale) OfferScaleChange(speaker, suggested);
        }
        catch (OperationCanceledException) when (_interjecting && !_roundCts!.IsCancellationRequested)
        {
            // 插話：它講到一半的內容不留（沒看過你的話，留著只會互相矛盾），
            // 但要寫一行說明，不然畫面上會莫名其妙少一段。
            _interjecting = false;
            if (_pendingMark >= 0) _transcript.TruncateTo(_pendingMark);
            _pendingMark = -1;
            AppendSystemLine($"你在 {speaker.ToFriendlyName()} 講到一半時插話，它會帶著你的話重講一次。");
            FlushPendingRemark();
            return true;
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            var reason = _roundCts!.IsCancellationRequested ? "這一輪被你停止了" : "這一輪被你跳過了";
            var skipped = new MeetingRemark(
                _round, speaker, $"（{reason}。）", Failed: true, Elapsed: DateTime.UtcNow - _speakerStartedAt);
            _transcriptEntries.Add(skipped);
            ReplacePlaceholderWith(skipped);
        }
        catch (Exception ex)
        {
            var failed = new MeetingRemark(
                _round, speaker, $"（這一輪失敗：{TextSummary.OneLine(ex.Message, 300)}）", Failed: true,
                Elapsed: DateTime.UtcNow - _speakerStartedAt);
            _transcriptEntries.Add(failed);
            ReplacePlaceholderWith(failed);
        }
        finally
        {
            _interjecting = false;
            _softTimer.Stop();
            _currentSpeaker = null;
            _pendingMark = -1;
        }

        return false;
    }


    private void OfferScaleChange(ProviderId speaker, MeetingScale suggested)
    {
        // AI 幾乎一定會傾向「需要更多篇幅」，所以不自動照做，由使用者決定。
        var answer = MessageBox.Show(
            $"{speaker.ToFriendlyName()} 認為這個議題的篇幅應該是「{suggested.ToFriendlyName()}」，"
            + $"目前是「{_scale.ToFriendlyName()}」。\r\n\r\n要改成它建議的規模嗎？（下一輪開始生效）",
            "調整討論規模",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        // 走同一個入口，兩個規模下拉才會一起跟著變。
        ApplyScale(suggested);
        AppendSystemLine($"討論規模已改成「{suggested.ToFriendlyName()}」，下一輪開始生效。");
    }

    private bool ConfirmCallBudget()
    {
        if (_warnedAboutCalls || _calls < CallWarningThreshold) return true;

        _warnedAboutCalls = true;
        var answer = MessageBox.Show(
            $"這場會議已經呼叫 AI {_calls} 次。要繼續嗎？",
            "AITeam",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        return answer == DialogResult.Yes;
    }

    private void CheckSoftTimeout()
    {
        if (_currentSpeaker is not { } speaker) return;

        var elapsed = DateTime.UtcNow - _speakerStartedAt;
        var warning = elapsed >= _softTimeout ? "　⚠ 想很久了，可按「跳過這家」" : "";
        SetStatus($"第 {_round} 輪 · {speaker.ToFriendlyName()} 發言中 {elapsed.Minutes:00}:{elapsed.Seconds:00}"
            + (_lastActivity.Length == 0 ? "" : $"（{_lastActivity}）")
            + warning);
    }

    /// <summary>
    /// 輪到誰就先把標題寫進逐字稿，讓「現在輪到誰」出現在內容裡而不是另一條列，
    /// 底下再暫時放一行「正在發言…」，等真正的發言回來就整段換掉。
    /// </summary>
    private void ShowTurnPlaceholder(ProviderId speaker)
    {
        _pendingMark = _transcript.Mark();
        _transcript.AppendHeading($"第 {_round} 輪 · {speaker.ToFriendlyName()}{Environment.NewLine}");
        _transcript.Append($"正在發言…{Environment.NewLine}");
        _transcript.ScrollToEnd();
        SetStatus($"第 {_round} 輪 · {speaker.ToFriendlyName()} 發言中 00:00");
    }

    /// <summary>
    /// 中途插話：把目前這位砍掉，讓它帶著你剛打的話重講。
    /// 三家 CLI 的提示都是一次性給進去的，沒有辦法在它已經開始想之後再「補一句進它耳朵」，
    /// 所以真正做得到、而且三家都一樣的做法就是重問一次。
    /// </summary>
    private void InterjectNow()
    {
        if (_sayBox.Text.Trim().Length == 0) return;
        _interjecting = true;
        try { _speakerCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void SkipCurrentSpeaker()
    {
        if (_drafting) return;
        try { _speakerCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>停掉這一輪：目前這家中斷，後面排隊的也不會再發言，但會議本身還在。</summary>
    private void StopRound()
    {
        if (_drafting)
        {
            try { _draftCts?.Cancel(); } catch (ObjectDisposedException) { }
            return;
        }
        try { _roundCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task DecideAsync()
    {
        RecordUserRemark(concluding: false);
        await EndMeetingAsync(TaskOutcome.Completed);
    }

    /// <summary>把這場會議談出來的東西交給主畫面，由它決定要對哪個專案執行。</summary>
    private async Task HandOffAsync()
    {
        if (_setup is null || _running) return;

        // 使用者在輸入框裡打的最後一段話也算結論的一部分，不要漏掉。
        RecordUserRemark(concluding: false);

        using var gate = new DecisionGateDialog(_participants);
        if (gate.ShowDialog(this) != DialogResult.OK || gate.Choice == DecisionGateChoice.Cancel) return;

        var conclusion = gate.Choice == DecisionGateChoice.Draft
            ? await DraftDecisionAsync(gate.Writer)
            : UnconvergedConclusion();

        // 定案書寫失敗而且使用者不想直接送，就留在會議裡，不要把會議收掉。
        if (conclusion is null) return;

        SaveHistory(TaskOutcome.Completed);
        await _meetings.CleanupAsync();
        _setup = null;

        SendToPipelineRequested?.Invoke(this, conclusion);
        Close();
    }

    private MeetingConclusion UnconvergedConclusion() => new(
        _setup!.Topic,
        _setup.Project?.Name,
        MeetingService.Summarise(_transcriptEntries, _round, _calls),
        MeetingConclusionKind.UnconvergedSummary);

    /// <summary>
    /// 請一家 AI 把整場會議寫成定案書。失敗的話問使用者要不要照舊直接送，
    /// 回傳 null 代表他選擇留在會議裡。
    /// </summary>
    private async Task<MeetingConclusion?> DraftDecisionAsync(ProviderId writer)
    {
        _drafting = true;
        _running = true;
        _draftCts?.Dispose();
        _draftCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        UpdateControls();
        SetStatus($"{writer.ToFriendlyName()} 正在整理定案書…");
        _transcript.AppendDivider();
        AppendSystemLine($"請 {writer.ToFriendlyName()} 把這場會議整理成定案書…");

        try
        {
            _calls++;
            UpdateBrief();
            // 定案書只讀逐字稿、不讀專案檔，所以不需要跟發言一樣寬的時間。
            var text = await _meetings.DraftDecisionAsync(
                writer, _setup!, _transcriptEntries, TimeSpan.FromMinutes(5), ReportActivity, _draftCts.Token);

            if (text.Length == 0) throw new InvalidOperationException("它沒有寫出任何內容。");

            _transcript.AppendHeading($"定案書（由 {writer.ToFriendlyName()} 整理）{Environment.NewLine}");
            _transcript.Append(TextSummary.CompactParagraphs(text) + Environment.NewLine);
            _transcript.AppendDivider();
            _transcript.ScrollToEnd();

            _decision = new MeetingConclusion(
                _setup!.Topic, _setup.Project?.Name, text, MeetingConclusionKind.Decision, writer);
            return _decision;
        }
        catch (Exception ex)
        {
            // 會議視窗正在關掉，不要再彈視窗問他問題。
            if (ex is OperationCanceledException && _lifetime.IsCancellationRequested) return null;

            var reason = ex is OperationCanceledException
                ? "定案書被你停掉了。"
                : $"定案書寫失敗：{TextSummary.OneLine(ex.Message, 200)}";
            AppendSystemLine(reason);

            var answer = MessageBox.Show(
                this,
                reason + Environment.NewLine + Environment.NewLine
                + "要改成直接送出目前的討論摘要嗎？（接手的 AI 會被明確告知這場會議沒有收斂，"
                + "不會把它當成已經決定好的事。）"
                + Environment.NewLine + Environment.NewLine
                + "選「否」就留在會議裡，你可以換一家再試一次。",
                "AITeam",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            return answer == DialogResult.Yes ? UnconvergedConclusion() : null;
        }
        finally
        {
            _drafting = false;
            _running = false;
            UpdateControls();
        }
    }

    private async Task EndMeetingAsync(TaskOutcome outcome)
    {
        if (_setup is null) { Close(); return; }

        SaveHistory(outcome);
        await _meetings.CleanupAsync();
        _setup = null;
        Close();
    }

    private void SaveHistory(TaskOutcome outcome)
    {
        if (_transcriptEntries.Count == 0) return;

        // 開完會就是開完了。只有「一個 AI 都沒講到話就關掉」才算沒完成。
        if (outcome == TaskOutcome.Completed
            && !_transcriptEntries.Any(r => r.Speaker is not null && !r.Failed))
        {
            outcome = TaskOutcome.Cancelled;
        }

        var transcript = string.Join(
            Environment.NewLine + Environment.NewLine,
            _transcriptEntries.Select(r => $"【第 {r.Round} 輪 · {r.SpeakerName}】{Environment.NewLine}{r.Text}"));

        if (_decision is { } decision)
        {
            var writer = decision.Writer?.ToFriendlyName() ?? "AI";
            transcript += Environment.NewLine + Environment.NewLine
                + $"【定案書 · 由 {writer} 整理】{Environment.NewLine}{decision.Text}";
        }

        _history.Save(
            new TaskHistoryEntry
            {
                Id = "meeting-" + Guid.NewGuid().ToString("N")[..10],
                ProjectName = _setup?.Project?.Name ?? "（不指定專案）",
                Subject = TextSummary.OneLine(_setup?.Topic ?? "會議", 40),
                Request = _setup?.Topic ?? "",
                Kind = TaskKind.Meeting,
                Outcome = outcome,
                StartedAt = _startedAt,
                FinishedAt = DateTime.Now,
                // 有定案書就存定案書：那才是這場會議真正的結果。
                Result = _decision?.Text ?? MeetingService.Summarise(_transcriptEntries, _round, _calls)
            },
            transcript);
    }

    /// <summary>把「正在發言…」那一段換成真正的內容。</summary>
    private void ReplacePlaceholderWith(MeetingRemark remark)
    {
        if (_pendingMark >= 0) _transcript.TruncateTo(_pendingMark);
        _pendingMark = -1;
        AppendRemark(remark);
    }

    private void AppendRemark(MeetingRemark remark)
    {
        var spent = remark.Elapsed > TimeSpan.Zero
            ? $" · 用時 {(int)remark.Elapsed.TotalMinutes:00}:{remark.Elapsed.Seconds:00}"
            : "";
        _transcript.AppendHeading($"第 {remark.Round} 輪 · {remark.SpeakerName}{spent}{Environment.NewLine}");
        _transcript.Append(TextSummary.CompactParagraphs(remark.Text) + Environment.NewLine);
        // 每則發言後面畫一條線，使用者才知道這個人講完了、下面是另一個人。
        // 有了這條線就不需要再多墊空行——一輪三個人，多墊的空行會讓人一直上下捲。
        _transcript.AppendDivider();
        _transcript.ScrollToEnd();
    }


    private void AppendSystemLine(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendSystemLine), text);
            return;
        }
        _transcript.Append($"· {text}{Environment.NewLine}");
        _transcript.ScrollToEnd();
    }

    private void ReportActivity(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(ReportActivity), text);
            return;
        }
        _lastActivity = text;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void UpdateControls()
    {
        var started = _setup is not null;
        var idle = !_running;

        _startButton.Enabled = !started && idle && _topicBox.Text.Trim().Length > 0;
        _scaleBox.Enabled = idle;
        _briefScaleBox.Enabled = idle;

        // 留言板永遠可以打字——會議開始之後，不管誰在發言你都能先把話打好。
        _sayBox.Enabled = started;
        _nextRoundButton.Visible = idle;
        _concludeButton.Visible = idle;
        _decideButton.Visible = idle;
        _handoffButton.Visible = idle;
        _endButton.Visible = idle;
        _nextRoundButton.Enabled = started;
        _concludeButton.Enabled = started && _round > 0;
        _decideButton.Enabled = started && _round > 0;
        // 至少要有一位 AI 真的講過話，才有結論可以送去執行。
        _handoffButton.Enabled = started && _transcriptEntries.Any(r => r.Speaker is not null && !r.Failed);

        // 有人在發言時，能做的只有插話、跳過它、或停掉這一輪。
        // 在寫定案書的時候只剩「停止」——沒有人在發言，插話跟跳過都沒有對象。
        _interjectButton.Visible = !idle && !_drafting;
        _skipSpeakerButton.Visible = !idle && !_drafting;
        _stopRoundButton.Visible = !idle;
        UpdateInterjectButton();

        if (!started) SetStatus("尚未開始");
        else if (idle) SetStatus($"第 {_round} 輪結束，輪到你");
    }

    /// <summary>留言板是空的就沒有話可以插，按鈕要跟著灰掉。</summary>
    private void UpdateInterjectButton() =>
        _interjectButton.Enabled = _running && _sayBox.Text.Trim().Length > 0;

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_running && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(
                "目前還有 AI 正在發言。確定要關閉會議嗎？",
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

        _softTimer.Stop();
        _lifetime.Cancel();
        if (_setup is not null) SaveHistory(TaskOutcome.Cancelled);
        await _meetings.CleanupAsync();
    }

    private static Label MakeFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SecondaryText,
        Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold),
        Margin = new Padding(2, 0, 0, 0)
    };

    private static Label MakeInlineLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SecondaryText,
        Font = new Font("Microsoft JhengHei UI", 9F),
        // Anchor = Left（沒有 Top）讓它在整列裡垂直置中；上下邊界一定要是 0，
        // 給了上邊界就會被往下推，看起來就跟旁邊的下拉沒對齊。
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 0, 6, 0)
    };

    private static void ConfigureCombo(ComboBox box)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Dock = DockStyle.Fill;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        box.Font = new Font("Microsoft JhengHei UI", 9.5F);
        box.Margin = new Padding(0, 0, 12, 0);
        box.MaxDropDownItems = 20;
    }

    private static void ConfigureSecondaryButton(Button button, string text, int width)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Size = new Size(width, 32);
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
        button.Size = new Size(width, 32);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
        button.Margin = Padding.Empty;
    }
}
