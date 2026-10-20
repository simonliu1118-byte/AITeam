using System.Diagnostics;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 開一個看得到的命令視窗替使用者執行 CLI 指令（登入、更新）。登入要在瀏覽器裡完成、
/// 有時還要在視窗裡按 Enter 或貼代碼，所以不能藏起來跑。跑完停在視窗上讓人看結果，按任意鍵關閉。
/// </summary>
internal static class CliConsole
{
    /// <returns>視窗關閉時回 true；找不到這個 CLI 時回 false（已經告訴使用者）。</returns>
    public static async Task<bool> RunAsync(IWin32Window owner, string command, IReadOnlyList<string> arguments, string title)
    {
        var executable = CliExecutableResolver.Resolve(command);
        if (executable is null)
        {
            MessageBox.Show(owner, $"這台電腦找不到 {command}，沒辦法{title}。", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var commandLine = string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote));
        var script = string.Join(" & ",
            "chcp 65001>nul",
            $"title AITeam - {title}",
            $"echo AITeam 正在{title}。需要登入時會開瀏覽器，照畫面完成即可。",
            "echo.",
            commandLine,
            "echo.",
            "echo 完成了，按任意鍵關閉這個視窗，AITeam 會自動重新檢查。",
            "pause>nul");

        var start = new ProcessStartInfo("cmd.exe")
        {
            // /s 讓 cmd 只拿掉最外層那對引號，裡面每個路徑自己的引號保留。
            Arguments = "/s /c \"" + script + "\"",
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        try
        {
            using var process = Process.Start(start);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"沒辦法開啟視窗{title}：{ex.Message}", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private static string Quote(string value) =>
        value.Length > 0 && value.IndexOfAny(new[] { ' ', '&', '(', ')', '^' }) < 0 ? value : "\"" + value + "\"";
}
