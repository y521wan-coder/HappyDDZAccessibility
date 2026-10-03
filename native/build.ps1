$ErrorActionPreference = 'Stop'
$sdk = 'C:\Program Files (x86)\Windows Kits\10'
$version = '10.0.26100.0'
$vc = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC\14.50.35717'
$env:INCLUDE = @(
    "$vc\include",
    "$sdk\Include\$version\ucrt",
    "$sdk\Include\$version\um",
    "$sdk\Include\$version\shared",
    "$sdk\Include\$version\winrt",
    "$sdk\Include\$version\cppwinrt"
) -join ';'
$env:LIB = @(
    "$vc\lib\x64",
    "$sdk\Lib\$version\ucrt\x64",
    "$sdk\Lib\$version\um\x64"
) -join ';'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$compiler = "$vc\bin\Hostx64\x64\cl.exe"
& $compiler /nologo /std:c++20 /EHsc /W4 /DUNICODE /D_UNICODE /MT "/Fo$bin\" "/Fe$bin\WindowCapture.exe" (Join-Path $root 'WindowCapture.cpp') /link d3d11.lib windowsapp.lib user32.lib
if ($LASTEXITCODE -ne 0) { throw "Native build failed with code $LASTEXITCODE" }
