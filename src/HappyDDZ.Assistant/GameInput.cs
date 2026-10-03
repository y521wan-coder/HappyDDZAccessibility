using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HappyDDZ.Assistant;

internal static class GameInput
{
    private const uint RootAncestor = 2;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    public static async Task ClickOnceAsync(GameWindow game, int clientX, int clientY,
        int expectedWidth, int expectedHeight, IntPtr assistantWindow,
        Func<Task>? verifyBeforeClick = null, Action? onClickSent = null,
        bool restoreAssistantFocus = true)
    {
        if (!IsWindow(game.Handle) || IsIconic(game.Handle)) throw new InvalidOperationException("游戏窗口已关闭或最小化，点击已取消。");
        GetWindowThreadProcessId(game.Handle, out var processId);
        if (processId != game.ProcessId) throw new InvalidOperationException("游戏窗口进程已变化，点击已取消。");
        using var process = Process.GetProcessById(game.ProcessId);
        if (!string.Equals(process.MainModule?.FileName, game.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目标程序路径已变化，点击已取消。");
        if (!GetClientRect(game.Handle, out var rect) ||
            rect.Right - rect.Left != expectedWidth || rect.Bottom - rect.Top != expectedHeight)
            throw new InvalidOperationException("游戏窗口尺寸已变化，请重新扫描。");
        if (clientX < 0 || clientY < 0 || clientX >= expectedWidth || clientY >= expectedHeight)
            throw new InvalidOperationException("目标坐标不在游戏客户区内，点击已取消。");

        var target = new Point { X = clientX, Y = clientY };
        if (!ClientToScreen(game.Handle, ref target)) throw new InvalidOperationException("无法换算游戏坐标，点击已取消。");
        if (!SetForegroundWindow(game.Handle)) throw new InvalidOperationException("无法激活游戏窗口，点击已取消。");
        try
        {
            // Unity may need several render frames after activation before it
            // accepts a click; a shorter wait missed replay controls on this PC.
            await Task.Delay(400);
            if (GetForegroundWindow() != game.Handle ||
                GetAncestor(WindowFromPoint(target), RootAncestor) != game.Handle)
                throw new InvalidOperationException("目标位置当前不属于前台游戏窗口，点击已取消。");
            if (verifyBeforeClick is not null)
            {
                await verifyBeforeClick();
                if (GetForegroundWindow() != game.Handle ||
                    GetAncestor(WindowFromPoint(target), RootAncestor) != game.Handle)
                    throw new InvalidOperationException("最终核对后游戏窗口失去前台，点击已取消。");
                if (!GetClientRect(game.Handle, out var currentRect) ||
                    currentRect.Right - currentRect.Left != expectedWidth ||
                    currentRect.Bottom - currentRect.Top != expectedHeight)
                    throw new InvalidOperationException("最终核对后游戏窗口尺寸变化，点击已取消。");
            }
            // A window can move during OCR without changing its size or
            // losing focus. Never reuse its old screen-space coordinates.
            var finalTarget = new Point { X = clientX, Y = clientY };
            if (!ClientToScreen(game.Handle, ref finalTarget) ||
                finalTarget.X != target.X || finalTarget.Y != target.Y)
                throw new InvalidOperationException("最终核对期间游戏窗口移动，点击已取消；请重新扫描。");
            if (!GetCursorPos(out var previous)) throw new InvalidOperationException("无法保存当前光标位置，点击已取消。");
            try
            {
                if (!SetCursorPos(target.X, target.Y)) throw new InvalidOperationException("无法定位光标，点击已取消。");
                mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
                try
                {
                    onClickSent?.Invoke();
                    await Task.Delay(50);
                }
                finally { mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero); }
                // The game reads cursor position on its render loop after the mouse-up event.
                await Task.Delay(450);
            }
            finally { SetCursorPos(previous.X, previous.Y); }
        }
        finally
        {
            if (restoreAssistantFocus) SetForegroundWindow(assistantWindow);
        }
    }
}
