param(
    [string]$OutputPath = 'D:\HappyDDZAccessibility\samples\window.png',
    [ValidateSet('Screen', 'PrintWindow')][string]$Method = 'Screen'
)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeWindowCapture {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hDC, uint flags);
}
'@
$process = Get-Process HLDDZ3D -ErrorAction Stop | Select-Object -First 1
$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw 'Game main window is unavailable.' }
$rect = New-Object NativeWindowCapture+RECT
$point = New-Object NativeWindowCapture+POINT
if (-not [NativeWindowCapture]::GetClientRect($handle, [ref]$rect)) { throw 'GetClientRect failed.' }
if (-not [NativeWindowCapture]::ClientToScreen($handle, [ref]$point)) { throw 'ClientToScreen failed.' }
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw 'Game client area has no size.' }
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    if ($Method -eq 'Screen') {
        $graphics.CopyFromScreen($point.X, $point.Y, 0, 0, $bitmap.Size)
        $ok = $true
    } else {
        $dc = $graphics.GetHdc()
        try { $ok = [NativeWindowCapture]::PrintWindow($handle, $dc, 1) }
        finally { $graphics.ReleaseHdc($dc) }
    }
    $folder = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{
        Path = $OutputPath
        Method = $Method
        Success = $ok
        Width = $width
        Height = $height
        ScreenX = $point.X
        ScreenY = $point.Y
        Foreground = ([NativeWindowCapture]::GetForegroundWindow() -eq $handle)
    }
} finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
