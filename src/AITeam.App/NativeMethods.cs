using System.Runtime.InteropServices;

namespace AITeam;

internal static class NativeMethods
{
    public const uint FLASHW_ALL = 3;
    public const uint FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    /// <summary>閃爍工作列圖示，直到使用者切回這個視窗為止。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
}
