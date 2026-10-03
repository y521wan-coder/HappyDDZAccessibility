function Get-AssistantWindow {
    if (-not ('AssistantWindowNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public static class AssistantWindowNative {
    public delegate bool Callback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(Callback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
    public static IntPtr FindAssistant() {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) => {
            if (!IsWindowVisible(hwnd)) return true;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            try {
                if (Process.GetProcessById((int)pid).ProcessName != "HappyDDZ.Assistant")
                    return true;
            } catch { return true; }
            var title = new StringBuilder(128);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString().Contains(" - ") && title.Length == 21) {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
    }
    $handle = [AssistantWindowNative]::FindAssistant()
    if ($handle -eq [IntPtr]::Zero -or
        -not [AssistantWindowNative]::IsWindowVisible($handle)) {
        throw 'Assistant window is unavailable or hidden. Press Ctrl+Alt+D to show it.'
    }
    $processId = [uint32]0
    [AssistantWindowNative]::GetWindowThreadProcessId($handle,
        [ref]$processId) | Out-Null
    $process = Get-Process -Id $processId -ErrorAction Stop
    if ($process.ProcessName -ne 'HappyDDZ.Assistant') {
        throw 'Assistant window belongs to an unexpected process.'
    }
    [pscustomobject]@{ Process = $process; Handle = $handle }
}
