using AITeam.Services;

namespace AITeam;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var runtimeRoot = RuntimeRootResolver.Resolve();
        CrashLogger.Initialize(runtimeRoot);

        // 使用者在「設定」裡開了隱藏主控台的話，要趕在第一次叫用 CLI（開程式就會做的
        // 上線檢查）之前套用，不然開程式時的那一下還是會閃。預設是關閉的。
        if (new AppPreferencesService(runtimeRoot).Load().HideCliConsole)
        {
            HiddenConsole.Apply(true);
        }

        Application.ThreadException += (_, e) =>
        {
            CrashLogger.Write("UI thread exception", e.Exception);
            MessageBox.Show(
                $"AITeam 發生未預期錯誤：\r\n{e.Exception.Message}\r\n\r\n詳細記錄：\r\n{CrashLogger.LogPath}",
                "AITeam",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                CrashLogger.Write("Unhandled exception", ex);
            }
        };

        try
        {
            Application.Run(new MainWindow(runtimeRoot));
        }
        catch (Exception ex)
        {
            CrashLogger.Write("Startup failure", ex);
            MessageBox.Show(
                $"AITeam 無法啟動：\r\n{ex.Message}\r\n\r\n詳細記錄：\r\n{CrashLogger.LogPath}",
                "AITeam 啟動失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
