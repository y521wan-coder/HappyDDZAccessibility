param([Parameter(Mandatory)][int]$X, [Parameter(Mandatory)][int]$Y)

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GameInput {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@
$game = Get-Process HLDDZ3D -ErrorAction Stop | Select-Object -First 1
$handle = $game.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw 'Game window is unavailable.' }
$point = New-Object GameInput+POINT
$point.X = $X
$point.Y = $Y
if (-not [GameInput]::ClientToScreen($handle, [ref]$point)) { throw 'ClientToScreen failed.' }
[GameInput]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 150
if ([GameInput]::GetForegroundWindow() -ne $handle) { throw 'Game window is not foreground; input cancelled.' }
[GameInput]::SetCursorPos($point.X, $point.Y) | Out-Null
[GameInput]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 50
[GameInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
[pscustomobject]@{ X=$X; Y=$Y; ScreenX=$point.X; ScreenY=$point.Y; Window=$game.MainWindowTitle }
