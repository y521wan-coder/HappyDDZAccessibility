using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HappyDDZ.Assistant;

internal sealed record GlobalKeyEvent(Keys Key, bool Control, bool Alt, bool Shift);

/// <summary>
/// Captures only the small set of play-mode keys while the game is foreground.
/// The hook is installed only while play mode is active and never receives text
/// entered into the official QQ login window.
/// </summary>
internal sealed class GlobalKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkShift = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback,
        IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private readonly HashSet<uint> _pressed = [];
    private readonly HookProc _callback;
    private IntPtr _hook;
    private bool _disposed;

    public GlobalKeyboardHook()
    {
        _callback = Callback;
    }

    public Func<GlobalKeyEvent, bool>? KeyDown { get; set; }

    public void Start()
    {
        if (_disposed || _hook != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback,
            GetModuleHandle(process.MainModule?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("无法安装游戏键盘模式；没有接管方向键。");
    }

    public void Stop()
    {
        _pressed.Clear();
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
        var data = Marshal.PtrToStructure<KeyboardData>(lParam);
        var keyDown = wParam.ToInt32() is WmKeyDown or WmSysKeyDown;
        var keyUp = wParam.ToInt32() is WmKeyUp or WmSysKeyUp;
        if (keyUp) _pressed.Remove(data.VirtualKey);
        if (keyDown && _pressed.Add(data.VirtualKey))
        {
            var key = (Keys)data.VirtualKey;
            var modifiers = new GlobalKeyEvent(key,
                (GetAsyncKeyState(VkControl) & 0x8000) != 0,
                (GetAsyncKeyState(VkMenu) & 0x8000) != 0,
                (GetAsyncKeyState(VkShift) & 0x8000) != 0);
            if (KeyDown?.Invoke(modifiers) == true) return (IntPtr)1;
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
