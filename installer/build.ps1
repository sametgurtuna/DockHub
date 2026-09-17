# Publishes DockHub (self-contained, win-x64) and compiles the Inno Setup installer.
# Usage: pwsh installer/build.ps1
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\CustomDock\CustomDock.csproj'

[xml]$proj = Get-Content $project
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Version not found in CustomDock.csproj' }

$publish = Join-Path $root "artifacts\publish\$Runtime"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish $project -c $Configuration -r $Runtime --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc "/DMyAppVersion=$version" "/DSourceDir=$publish" (Join-Path $PSScriptRoot 'DockHub.iss')
if ($LASTEXITCODE) { throw 'ISCC failed' }

Write-Host "Installer: $(Join-Path $PSScriptRoot "Output\DockHub-Setup-$version-x64.exe")"
