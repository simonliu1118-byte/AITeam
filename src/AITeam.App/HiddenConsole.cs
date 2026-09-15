using System.Runtime.InteropServices;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 「開程式時閃一下 PowerShell 黑框」的對策，做成可開可關。
///
/// 閃的不是我們自己叫起來的 CLI，而是 CLI 再去叫起來的孫程序——Codex 在 Windows 上
/// 是用 PowerShell 去跑它的工具。孫程序怎麼開視窗我們管不到，唯一能做的是：
/// 先幫自己配一個「隱藏起來的主控台」，然後讓 CLI 沿用它。這樣孫程序繼承到的就是
/// 那個隱藏的主控台，不會再自己開一個看得見的。
///
/// 這個手法不保證有效（孫程序如果明講要開新主控台，誰都攔不住），所以預設關閉，
/// 而且隨時可以關回去＝完全等於原本的行為。
/// </summary>
internal static class HiddenConsole
{
    private const int SW_HIDE = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>目前是不是真的處於「有隱藏主控台可以給 CLI 沿用」的狀態。</summary>
    public static bool IsActive => ProcessRunner.InheritHiddenConsole;

    /// <summary>
    /// 套用使用者的選擇。回傳「有沒有真的套用成功」——配不出隱藏的主控台時會回 false
    /// 並且維持原本的行為，絕對不會留在「沒有主控台卻叫 CLI 去沿用」這種更糟的半套狀態
    /// （那會變成每個 CLI 自己開一個看得見的視窗）。
    /// </summary>
    public static bool Apply(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (ProcessRunner.InheritHiddenConsole)
                {
                    FreeConsole();
                    ProcessRunner.InheritHiddenConsole = false;
                }
                return true;
            }

            if (ProcessRunner.InheritHiddenConsole) return true;

            var window = GetConsoleWindow();
            if (window == IntPtr.Zero)
            {
                if (!AllocConsole()) return false;
                window = GetConsoleWindow();
            }

            if (window == IntPtr.Zero) return false;

            ShowWindow(window, SW_HIDE);
            if (IsWindowVisible(window)) return false;

            // 從現在開始 CLI 會跟我們共用同一個主控台，它們產生的 Ctrl+C 之類的訊號
            // 也會傳到我們身上。忽略掉，不然使用者停止任務時可能連主程式一起關掉。
            SetConsoleCtrlHandler(IntPtr.Zero, true);

            ProcessRunner.InheritHiddenConsole = true;
            return true;
        }
        catch
        {
            ProcessRunner.InheritHiddenConsole = false;
            return false;
        }
    }
}
