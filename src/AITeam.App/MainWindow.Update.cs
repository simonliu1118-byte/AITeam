using AITeam.Services;

namespace AITeam;

/// <summary>
/// 從 GitHub Release 自動更新（跟 CYERPAutoInput 一樣）：開啟後 20 秒先查一次，之後每 4 小時查一次。
/// 有新版就在背景下載、核對；等 AITeam 閒著（沒有任務在跑、沒有其他視窗開著）才倒數 10 秒、
/// 更新、重新開啟。有任務在跑就等，關掉 AITeam 時會順便換上新版。
/// </summary>
public sealed partial class MainWindow
{
    private const string UpdateProgramName = "AITeam";
    private const int UpdateCountdownSeconds = 10;
    private static readonly TimeSpan UpdateLater = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan UpdateRetryAfterFailure = TimeSpan.FromHours(8);

    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 30_000 };
    private readonly UpdateBanner _updateBanner = new();
    private DateTime _nextUpdateCheck = DateTime.Now.AddSeconds(20);
    private DateTime _updateNotBefore = DateTime.MinValue;
    private (string Folder, Version Version)? _updateReady;
    private bool _updating;
    private bool _updateChecksStarted;

    private void StartUpdateChecks()
    {
        if (_updateChecksStarted) return;
        _updateChecksStarted = true;

        var updater = new AppUpdater(_runtimeRoot);
        // 停靠順序：後加入的先佔空間。提示列要在最上面，所以把填滿的那一塊排到它後面。
        var fill = Controls.Cast<Control>().FirstOrDefault(c => c.Dock == DockStyle.Fill);
        Controls.Add(_updateBanner);
        fill?.BringToFront();

        ShowUpdateOutcome(updater.TakeOutcome());
        _updateTimer.Tick += async (_, _) => await UpdateTickAsync(updater);
        _updateTimer.Start();
        FormClosing += (_, e) =>
        {
            // 有任務在跑時使用者可能取消關閉；那時候程式還開著，不能去換檔案。
            if (e.Cancel) return;
            _updateTimer.Stop();
            if (_updateReady is { } ready && !_updating) TryApplyUpdate(updater, ready, restart: false);
        };
    }

    private void ShowUpdateOutcome(UpdateOutcome? outcome)
    {
        if (outcome is null) return;
        var current = AppUpdater.CurrentVersion?.ToString(3) ?? "?";
        if (outcome.Ok && outcome.Version == current)
        {
            _updateBanner.Show(UpdateBannerKind.Success, $"已更新到 V{outcome.Version}。", TimeSpan.FromSeconds(10));
            return;
        }

        _nextUpdateCheck = DateTime.Now + UpdateRetryAfterFailure;
        _updateBanner.Show(UpdateBannerKind.Failure,
            $"更新到 V{outcome.Version} 失敗，目前仍是 V{current}；8 小時後會再試，也可以手動更新"
            + $"（到 {AppUpdateCheck.ReleasesPage} 下載 zip，把裡面的 AITeam.exe 蓋過現在這個）。"
            + (outcome.Detail.Length == 0 ? "" : $"原因：{outcome.Detail}"));
    }

    /// <summary>沒有任務在跑、也沒有其他視窗（會議、對話框）開著。</summary>
    private bool IdleForUpdate(int windows = 1) =>
        !_taskRunning && Application.OpenForms.Cast<Form>().Count(f => f.Visible) == windows;

    private async Task UpdateTickAsync(AppUpdater updater)
    {
        if (_updating) return;

        if (_updateReady is null)
        {
            if (DateTime.Now < _nextUpdateCheck) return;
            _nextUpdateCheck = DateTime.Now.AddHours(4);
            _updating = true;
            try
            {
                if (await updater.PrepareAsync(CancellationToken.None, offer =>
                        BeginInvoke(() => _updateBanner.Show(UpdateBannerKind.Progress, $"發現新版 V{offer.Version.ToString(3)}，下載中…"))) is { } ready)
                {
                    _updateReady = (ready.Folder, ready.Offer.Version);
                    _updateBanner.Show(UpdateBannerKind.Progress,
                        $"新版 V{ready.Offer.Version.ToString(3)} 已下載，AITeam 閒著的時候會自動更新。");
                }
            }
            catch
            {
                // 查不到、下載失敗都不能影響正常使用，下一次再試。
                _updateBanner.HideBanner();
            }
            finally
            {
                _updating = false;
            }
        }

        if (_updateReady is not { } update || DateTime.Now < _updateNotBefore || !IdleForUpdate()) return;

        _updating = true;
        DialogResult answer;
        using (var countdown = new UpdateCountdownForm(
                   UpdateProgramName, update.Version.ToString(3), UpdateCountdownSeconds, () => !IdleForUpdate(windows: 2)))
            answer = countdown.ShowDialog(this);

        if (answer == DialogResult.Ignore)
        {
            _updateNotBefore = DateTime.Now + UpdateLater;
            _updateBanner.Show(UpdateBannerKind.Progress,
                $"新版 V{update.Version.ToString(3)} 已延後，{_updateNotBefore:HH:mm} 之後 AITeam 閒著的時候會自動更新。");
        }

        if (answer == DialogResult.OK && IdleForUpdate() && TryApplyUpdate(updater, update, restart: true))
        {
            Close();
            return;
        }

        _updating = false;
    }

    private bool TryApplyUpdate(AppUpdater updater, (string Folder, Version Version) update, bool restart)
    {
        try
        {
            updater.ApplyAfterExit(update.Folder, update.Version, restart);
            return true;
        }
        catch
        {
            _updateReady = null;
            _updateBanner.HideBanner();
            return false;
        }
    }
}
