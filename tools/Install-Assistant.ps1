param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'HappyDDZAccessibility'),
    [switch]$SkipLaunch,
    [switch]$NoShortcuts
)
$ErrorActionPreference = 'Stop'
try {
    $source = $PSScriptRoot
    $target = [IO.Path]::GetFullPath($InstallDirectory)
    if ($target.TrimEnd('\') -eq $source.TrimEnd('\')) { throw '安装目录不能和解压目录相同。' }
    $exe = Join-Path $target 'HappyDDZ.Assistant.exe'
    $running = Get-Process -Name HappyDDZ.Assistant -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe }
    if ($running) { throw '助手正在运行，请关闭助手后再安装更新。' }
    $manifest = Get-Content -LiteralPath (Join-Path $source 'files.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    # Validate all paths and hashes before writing any installed file.
    foreach ($item in $manifest) {
        $from = [IO.Path]::GetFullPath((Join-Path $source $item.Path))
        $to = [IO.Path]::GetFullPath((Join-Path $target $item.Path))
        if (-not $from.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not $to.StartsWith($target.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw '安装清单含有越界路径。'
        }
        if ((Get-FileHash -LiteralPath $from -Algorithm SHA256).Hash -ne $item.SHA256) {
            throw "安装文件不完整：$($item.Path)，请重新解压。"
        }
    }
    foreach ($item in $manifest) {
        $to = Join-Path $target $item.Path
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $source $item.Path) -Destination $to -Force
    }
    Copy-Item -LiteralPath (Join-Path $source 'files.json') -Destination $target -Force
    # No config is copied: first launch uses auto QQ login, and upgrades retain settings.
    if (-not $NoShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($folder in @([Environment]::GetFolderPath('DesktopDirectory'), [Environment]::GetFolderPath('Programs'))) {
            $shortcut = $shell.CreateShortcut((Join-Path $folder '欢乐斗地主无障碍助手.lnk'))
            $shortcut.TargetPath = $exe
            $shortcut.WorkingDirectory = $target
            $shortcut.Save()
        }
    }
    Write-Output "安装完成：$exe。默认使用电脑上已登录 QQ 登录大厅。"
    if (-not $SkipLaunch) { Start-Process -FilePath $exe -WorkingDirectory $target -WindowStyle Hidden }
}
catch {
    Write-Error $_
    exit 1
}
