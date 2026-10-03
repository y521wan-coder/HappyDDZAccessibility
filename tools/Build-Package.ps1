param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build-Release.ps1') }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$destination = Join-Path $root "release\packages\HappyDDZ-$stamp"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$files = @(
    'native\bin\WindowCapture.exe',
    'vendor\RapidV6\Rapid.dll',
    'vendor\RapidV6\resources\manifest.json',
    'vendor\RapidV6\resources\models.json',
    'vendor\RapidV6\resources\models\PP-OCRv6_det_tiny.onnx',
    'vendor\RapidV6\resources\models\PP-OCRv6_rec_tiny.onnx',
    'vendor\RapidV6\resources\models\PP-LCNet_x1_0_textline_ori.onnx',
    'vendor\RapidV6\resources\models\PP-LCNet_x1_0_doc_ori.onnx',
    'vendor\RapidV6\resources\runtime\onnxruntime.dll',
    'vendor\RapidV6\resources\runtime\onnxruntime_providers_shared.dll',
    'vendor\RapidV6\resources\runtime\DirectML.dll',
    'vendor\RapidV6\resources\runtime\msvcp140.dll',
    'vendor\RapidV6\resources\runtime\vcruntime140.dll',
    'vendor\RapidV6\resources\runtime\vcruntime140_1.dll',
    'models\cards\replay-plus-live-one-hand.json',
    'models\timer\clock-v1.json',
    'models\controls\bid-phase-v1.json'
)
$files += Get-ChildItem -LiteralPath (Join-Path $root 'vendor\RapidV6\resources\licenses') -File |
    ForEach-Object { $_.FullName.Substring($root.Length + 1) }
foreach ($relative in $files) {
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
Copy-Item -LiteralPath (Join-Path $root 'release\app\HappyDDZ.Assistant.exe') -Destination $destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Assistant.ps1') -Destination (Join-Path $destination 'Install.ps1')
Copy-Item -LiteralPath (Join-Path $root 'docs\安装与使用.txt') -Destination $destination
'@echo off', 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"', 'if errorlevel 1 pause' |
    Set-Content -LiteralPath (Join-Path $destination 'Install.cmd') -Encoding ASCII
$manifest = Get-ChildItem -LiteralPath $destination -Recurse -File | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($destination.Length + 1); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'files.json') -Encoding UTF8
$archive = "$destination.zip"
Compress-Archive -LiteralPath $destination -DestinationPath $archive -CompressionLevel Fastest
[pscustomobject]@{ Directory = $destination; Archive = $archive; FileCount = @($manifest).Count; SizeMB = [Math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 1) }
