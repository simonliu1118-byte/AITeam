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

    private readonly Label _statusLabel = new();
    private readonly RichTextBox _transcriptBox = new();
    private readonly LinkFoldingLog _transcript;
    private readonly TextBox _sayBox = new();
    private readonly Button _nextRoundButton = new();
    private readonly Button _concludeButton = new();
    private readonly Button _decideButton = new();
    private readonly Button _endButton = new();
    private readonly Panel _waitingBar = new();
    private readonly Label _waitingLabel = new();
    private readonly Button _keepWaitingButton = new();
    private readonly Button _skipSpeakerButton = new();
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
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DateTime _startedAt = DateTime.Now;

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
            RowCount = 5,
            Padding = new Padding(18),
            BackColor = AppBackground
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        shell.Controls.Add(BuildSetupCard(), 0, 0);
        shell.Controls.Add(BuildStatusRow(), 0, 1);
        shell.Controls.Add(BuildTranscriptCard(), 0, 2);
        shell.Controls.Add(BuildWaitingBar(), 0, 3);
        shell.Controls.Add(BuildChairRow(), 0, 4);
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

        var options = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 7, BackColor = CardBackground };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _projectBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _projectBox.Dock = DockStyle.Fill;
        _projectBox.Margin = new Padding(0, 0, 12, 0);
        _projectBox.Items.Add("不指定（純討論）");
        foreach (var project in _projects) _projectBox.Items.Add(project.Name);
        _projectBox.SelectedIndex = 0;

        _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeBox.Dock = DockStyle.Fill;
        _modeBox.Margin = new Padding(0, 0, 12, 0);
        _modeBox.Items.AddRange(new object[] { "輪流發言（會收斂）", "各自作答（會發散）" });
        _modeBox.SelectedIndex = 0;

        _scaleBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _scaleBox.Dock = DockStyle.Fill;
        _scaleBox.Margin = new Padding(0, 0, 12, 0);
        _scaleBox.Items.AddRange(new object[] { "簡短（約300字）", "標準（約800字）", "深入（約2000字）" });
        _scaleBox.SelectedIndex = 1;
        _scaleBox.SelectedIndexChanged += (_, _) => _scale = (MeetingScale)_scaleBox.SelectedIndex;

        options.Controls.Add(MakeInlineLabel("專案"), 0, 0);
        options.Controls.Add(_projectBox, 1, 0);
        options.Controls.Add(MakeInlineLabel("模式"), 2, 0);
        options.Controls.Add(_modeBox, 3, 0);
        options.Controls.Add(MakeInlineLabel("規模"), 4, 0);
        options.Controls.Add(_scaleBox, 5, 0);

        ConfigurePrimaryButton(_startButton, "開始會議", 110);
        _startButton.Click += async (_, _) => await StartMeetingAsync();
        options.Controls.Add(_startButton, 6, 0);

        body.Controls.Add(options, 0, 2);
        card.Controls.Add(body);
        return card;
    }

    private Control BuildStatusRow()
    {
        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Height = 22;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = SecondaryText;
        _statusLabel.Font = new Font("Microsoft JhengHei UI", 9F);
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
        _transcriptBox.Font = new Font("Microsoft JhengHei UI", 9.5F);
        card.Controls.Add(_transcriptBox);
        return card;
    }

    private Control BuildWaitingBar()
    {
        _waitingBar.Dock = DockStyle.Top;
        _waitingBar.Height = 40;
        _waitingBar.BackColor = Color.FromArgb(255, 247, 225);
        _waitingBar.Visible = false;
        _waitingBar.Margin = new Padding(0, 0, 0, 8);

        _waitingLabel.AutoSize = false;
        _waitingLabel.Dock = DockStyle.Fill;
        _waitingLabel.TextAlign = ContentAlignment.MiddleLeft;
        _waitingLabel.ForeColor = Color.FromArgb(140, 94, 0);
        _waitingLabel.Font = new Font("Microsoft JhengHei UI", 9F);
        _waitingLabel.Padding = new Padding(10, 0, 0, 0);

        ConfigureSecondaryButton(_skipSpeakerButton, "跳過這家", 100);
        _skipSpeakerButton.Dock = DockStyle.Right;
        _skipSpeakerButton.Click += (_, _) => SkipCurrentSpeaker();

        ConfigureSecondaryButton(_keepWaitingButton, "繼續等", 90);
        _keepWaitingButton.Dock = DockStyle.Right;
        _keepWaitingButton.Click += (_, _) => KeepWaiting();

        // 先加靠右的按鈕再加填滿的文字：WinForms 是後加入的先吃掉空間，
        // 順序顛倒的話文字會把兩顆按鈕蓋住。
        _waitingBar.Controls.Add(_skipSpeakerButton);
        _waitingBar.Controls.Add(_keepWaitingButton);
        _waitingBar.Controls.Add(_waitingLabel);
        return _waitingBar;
    }

    private Control BuildChairRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        row.Controls.Add(MakeFieldLabel("你的發言（選填）"), 0, 0);

        _sayBox.Dock = DockStyle.Top;
        _sayBox.Multiline = true;
        _sayBox.Height = 56;
        _sayBox.ScrollBars = ScrollBars.Vertical;
        _sayBox.Margin = new Padding(0, 4, 0, 8);
        row.Controls.Add(_sayBox, 0, 1);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 4; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        ConfigureSecondaryButton(_endButton, "結束會議", 100);
        _endButton.Margin = new Padding(0, 0, 8, 0);
        _endButton.Click += async (_, _) => await EndMeetingAsync(TaskOutcome.Cancelled);
        buttons.Controls.Add(_endButton, 1, 0);

        ConfigureSecondaryButton(_decideButton, "直接定案", 100);
        _decideButton.Margin = new Padding(0, 0, 8, 0);
        _decideButton.Click += async (_, _) => await DecideAsync();
        buttons.Controls.Add(_decideButton, 2, 0);

        ConfigureSecondaryButton(_concludeButton, "請 AI 收斂結論", 130);
        _concludeButton.Margin = new Padding(0, 0, 8, 0);
        _concludeButton.Click += async (_, _) => await RunRoundAsync(concluding: true);
        buttons.Controls.Add(_concludeButton, 3, 0);

        ConfigurePrimaryButton(_nextRoundButton, "繼續下一輪", 130);
        _nextRoundButton.Click += async (_, _) => await RunRoundAsync(concluding: false);
        buttons.Controls.Add(_nextRoundButton, 4, 0);

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
        AppendSystemLine($"議題：{topic}");
        AppendSystemLine($"參與者：{string.Join("、", _participants.Select(p => p.ToFriendlyName()))}"
            + $"｜模式：{_modeBox.Text}｜規模：{_scale.ToFriendlyName()}"
            + $"｜專案：{project?.Name ?? "不指定"}");

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

            _round++;
            foreach (var speaker in MeetingService.SpeakingOrder(_participants, _round))
            {
                if (_lifetime.IsCancellationRequested) return;
                await AskOneAsync(speaker, concluding);
            }
        }
        finally
        {
            _running = false;
            StopWaitingBar();
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

        if (said.Length == 0) return;

        var remark = new MeetingRemark(_round, null, said);
        _transcriptEntries.Add(remark);
        AppendRemark(remark);
        _sayBox.Clear();
    }

    private async Task AskOneAsync(ProviderId speaker, bool concluding)
    {
        _speakerCts?.Dispose();
        _speakerCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _speakerStartedAt = DateTime.UtcNow;
        _softTimeout = TimeSpan.FromMinutes(3);
        _softTimer.Start();
        SetStatus($"第 {_round} 輪 · {speaker.ToFriendlyName()} 發言中…");

        try
        {
            _calls++;
            var remark = await _meetings.AskAsync(
                speaker,
                _setup! with { Mode = concluding ? MeetingMode.RoundRobin : _setup!.Mode },
                _transcriptEntries,
                _round,
                _scale,
                // 硬逾時：到這裡幾乎確定是卡死了，不是在思考。
                TimeSpan.FromMinutes(15),
                ReportActivity,
                _speakerCts.Token);

            _transcriptEntries.Add(remark);
            AppendRemark(remark);
            if (remark.SuggestedScale is { } suggested && suggested != _scale) OfferScaleChange(speaker, suggested);
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            AppendSystemLine($"⚠️ {speaker.ToFriendlyName()} 這一輪被你跳過了。");
            _transcriptEntries.Add(new MeetingRemark(_round, speaker, "（這一輪被跳過。）", Failed: true));
        }
        catch (Exception ex)
        {
            AppendSystemLine($"⚠️ {speaker.ToFriendlyName()} 這一輪失敗：{TextSummary.OneLine(ex.Message, 300)}");
            _transcriptEntries.Add(new MeetingRemark(_round, speaker, "（這一輪失敗，沒有發言。）", Failed: true));
        }
        finally
        {
            _softTimer.Stop();
            StopWaitingBar();
        }
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

        _scale = suggested;
        _scaleBox.SelectedIndex = (int)suggested;
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
        if (!_running || _speakerCts is null) return;

        var elapsed = DateTime.UtcNow - _speakerStartedAt;
        SetStatus($"第 {_round} 輪 · 發言中… 已經 {elapsed.Minutes:00}:{elapsed.Seconds:00}");

        // 軟逾時不砍，只問你要不要等。真的比較大的議題不該因為時間到就被丟掉。
        if (elapsed < _softTimeout || _waitingBar.Visible) return;

        _waitingLabel.Text = $"已經等了 {(int)elapsed.TotalMinutes} 分鐘，它可能還在想，也可能卡住了。";
        _waitingBar.Visible = true;
    }

    private void KeepWaiting()
    {
        _softTimeout += TimeSpan.FromMinutes(3);
        StopWaitingBar();
    }

    private void SkipCurrentSpeaker()
    {
        StopWaitingBar();
        try { _speakerCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void StopWaitingBar() => _waitingBar.Visible = false;

    private async Task DecideAsync()
    {
        RecordUserRemark(concluding: false);
        await EndMeetingAsync(TaskOutcome.Completed);
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

        var transcript = string.Join(
            Environment.NewLine + Environment.NewLine,
            _transcriptEntries.Select(r => $"【第 {r.Round} 輪 · {r.SpeakerName}】{Environment.NewLine}{r.Text}"));

        var lastAi = _transcriptEntries.LastOrDefault(r => r.Speaker is not null && !r.Failed);
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
                Result = lastAi is null ? "（沒有任何 AI 發言。）" : TextSummary.OneLine(lastAi.Text, 600)
            },
            transcript);
    }

    private void AppendRemark(MeetingRemark remark)
    {
        _transcript.Append(
            $"【第 {remark.Round} 輪 · {remark.SpeakerName}】{Environment.NewLine}{remark.Text}{Environment.NewLine}{Environment.NewLine}");
        _transcript.ScrollToEnd();
    }

    private void AppendSystemLine(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendSystemLine), text);
            return;
        }
        _transcript.Append($"· {text}{Environment.NewLine}{Environment.NewLine}");
        _transcript.ScrollToEnd();
    }

    private void ReportActivity(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(ReportActivity), text);
            return;
        }
        SetStatus($"第 {_round} 輪 · {text}");
    }

    private void SetStatus(string text) =>
        _statusLabel.Text = $"{text}　|　已呼叫 {_calls} 次　|　規模：{_scale.ToFriendlyName()}";

    private void UpdateControls()
    {
        var started = _setup is not null;
        var idle = !_running;

        _topicBox.Enabled = !started;
        _projectBox.Enabled = !started;
        _modeBox.Enabled = !started;
        _startButton.Enabled = !started && idle && _topicBox.Text.Trim().Length > 0;
        _scaleBox.Enabled = idle;

        _sayBox.Enabled = started && idle;
        _nextRoundButton.Enabled = started && idle;
        _concludeButton.Enabled = started && idle && _round > 0;
        _decideButton.Enabled = started && idle && _round > 0;
        _endButton.Enabled = idle;

        if (!started) SetStatus("尚未開始");
        else if (idle) SetStatus($"第 {_round} 輪結束，輪到你");
    }

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
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 7, 6, 0)
    };

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
