using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HappyDDZ.Assistant;

internal enum WindowKind { Auto, Hall, Game, Login }

internal sealed record GameWindow(int ProcessId, IntPtr Handle, string Title,
    WindowKind Kind, string ExecutablePath)
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);

    public static GameWindow? Find(WindowKind requested)
    {
        if (requested is WindowKind.Game or WindowKind.Auto)
        {
            var game = FindProcess("HLDDZ3D", "欢乐斗地主", WindowKind.Game);
            if (game is not null) return game;
        }
        if (requested is WindowKind.Login or WindowKind.Auto)
        {
            var login = FindProcess("QQGame", "主账号登录窗口", WindowKind.Login);
            if (login is not null) return login;
        }
        if (requested is WindowKind.Hall or WindowKind.Auto)
        {
            var hall = FindProcess("QQGame", "QQ游戏", WindowKind.Hall);
            if (hall is not null) return hall;
        }
        return null;
    }

    private static GameWindow? FindProcess(string processName, string title, WindowKind kind)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                var handle = kind is WindowKind.Hall or WindowKind.Login
                    ? FindVisibleHallWindow(process.Id, title)
                    : process.MainWindowHandle;
                if (handle == IntPtr.Zero ||
                    kind == WindowKind.Game &&
                    !process.MainWindowTitle.Contains(title, StringComparison.Ordinal)) continue;
                var path = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (kind == WindowKind.Game &&
                    !string.Equals(path, @"D:\HappyDDZ\GameNew\HLDDZ3D.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if ((kind is WindowKind.Hall or WindowKind.Login) &&
                    !QqHallInstallation.IsHallExecutable(path)) continue;
                return new GameWindow(process.Id, handle, title, kind, path);
            }
            catch (System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        return null;
    }

    private static IntPtr FindVisibleHallWindow(int processId, string title)
    {
        var result = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !IsWindowVisible(hwnd)) return true;
            var name = new StringBuilder(128);
            GetWindowText(hwnd, name, name.Capacity);
            if (name.ToString() != title) return true;
            result = hwnd;
            return false;
        }, IntPtr.Zero);
        return result;
    }
}
