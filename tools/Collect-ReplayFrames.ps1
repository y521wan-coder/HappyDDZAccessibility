param(
    [ValidateRange(1, 32)][int]$Steps = 32,
    [ValidateSet('Next', 'Previous')][string]$Direction = 'Next',
    [switch]$NoCapture
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$captureExe = Join-Path $root 'native\bin\WindowCapture.exe'
$outputDir = Join-Path $root 'samples\private\replay-frames'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ReplayInput {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@

$game = Get-Process HLDDZ3D -ErrorAction Stop | Select-Object -First 1
if ($game.MainModule.FileName -ne 'D:\HappyDDZ\GameNew\HLDDZ3D.exe') { throw 'Unexpected game executable.' }
$handle = $game.MainWindowHandle
if ($handle -eq [IntPtr]::Zero -or [ReplayInput]::IsIconic($handle)) { throw 'Game is unavailable or minimized.' }
$rect = New-Object ReplayInput+RECT
if (-not [ReplayInput]::GetClientRect($handle, [ref]$rect) -or
    $rect.Right -ne 1280 -or $rect.Bottom -ne 720) { throw 'Unexpected game client size.' }
if ([ReplayInput]::GetForegroundWindow() -ne $handle) { throw 'Game is not foreground.' }

# Replay controls have a green play/pause bar at the bottom. Guard against
# accidentally using fixed replay coordinates on the lobby or a paid popup.
Add-Type -AssemblyName System.Drawing
$preflight = Join-Path $outputDir 'preflight.bmp'
& $captureExe $game.Id $handle.ToInt64() $preflight | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not verify replay controls.' }
try {
    $bitmap = New-Object System.Drawing.Bitmap($preflight)
    try {
        foreach ($x in @(1140, 1175)) {
            $pixel = $bitmap.GetPixel($x, 699)
            if ($pixel.G -lt 170 -or $pixel.G -le $pixel.R + 40 -or
                $pixel.G -le $pixel.B + 35) {
                throw 'Replay play/pause bar was not confirmed; no step clicks sent.'
            }
        }
    } finally { $bitmap.Dispose() }
} finally { Remove-Item -LiteralPath $preflight -ErrorAction SilentlyContinue }

for ($step = 1; $step -le $Steps; $step++) {
    if ([ReplayInput]::GetForegroundWindow() -ne $handle -or [ReplayInput]::IsIconic($handle)) {
        throw "Replay focus lost at step $step."
    }
    $point = New-Object ReplayInput+POINT
    $point.X = if ($Direction -eq 'Next') { 1230 } else { 1086 }
    $point.Y = 698
    if (-not [ReplayInput]::ClientToScreen($handle, [ref]$point)) { throw 'ClientToScreen failed.' }
    [ReplayInput]::SetCursorPos($point.X, $point.Y) | Out-Null
    [ReplayInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [ReplayInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    if (-not $NoCapture) {
        $prefix = if ($Direction -eq 'Next') { 'step' } else { 'reverse' }
        $path = Join-Path $outputDir ('{0}-{1:d2}.bmp' -f $prefix, $step)
        & $captureExe $game.Id $handle.ToInt64() $path | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Capture failed at step $step." }
    }
}
if ($NoCapture) { Write-Output "Moved $Steps replay steps $Direction without capture" }
else { Write-Output "Captured $Steps private replay frames in $outputDir" }
