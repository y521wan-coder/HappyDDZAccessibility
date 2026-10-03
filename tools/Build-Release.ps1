$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
& (Join-Path $root 'native\build.ps1')
$sdk = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $sdk)) { throw 'The .NET 10 SDK was not found at the expected path.' }
$project = Join-Path $root 'src\HappyDDZ.Assistant\HappyDDZ.Assistant.csproj'
$output = Join-Path $root 'release\app'
& $sdk publish $project -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed with code $LASTEXITCODE" }
Write-Output (Join-Path $output 'HappyDDZ.Assistant.exe')
