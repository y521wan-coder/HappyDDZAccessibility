using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HappyDDZ.Assistant;

/// <summary>Hides the two visible QQ Hall windows while leaving its login process running.</summary>
internal static class HallWindowVisibility
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);

    private static IReadOnlyList<IntPtr> FindWindows()
    {
        var pids = Process.GetProcessesByName("QQGame")
            .Select(process => process.Id).ToHashSet();
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains((int)pid)) return true;
            var title = new StringBuilder(128);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString() is "QQ游戏" or "QQGameHall")
                windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static string SetVisible(bool visible)
    {
        if (!visible && GameWindow.Find(WindowKind.Game) is null)
            throw new InvalidOperationException("欢乐斗地主游戏窗口未运行；没有隐藏大厅入口。");
        var windows = FindWindows();
        if (windows.Count == 0)
            throw new InvalidOperationException("没有找到 QQ 游戏大厅窗口。大厅进程可能未运行。");
        foreach (var hwnd in windows)
            if (IsWindowVisible(hwnd) != visible)
                ShowWindow(hwnd, visible ? 8 : 0);
        var changed = windows.Count(hwnd => IsWindowVisible(hwnd) == visible);
        if (changed != windows.Count)
            throw new InvalidOperationException("部分 QQ 游戏大厅窗口未切换成功，请重试。");
        return visible
            ? $"已显示 {changed} 个 QQ 游戏大厅窗口；大厅进程一直保留。"
            : $"已隐藏 {changed} 个 QQ 游戏大厅窗口；大厅进程仍运行，斗地主窗口可继续使用。";
    }
}
