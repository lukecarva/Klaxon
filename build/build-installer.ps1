<#
.SYNOPSIS
  Builds the Klaxon installer from scratch.

.DESCRIPTION
  Publishes framework-dependent (a single lean .exe, ~5 MB, that uses the already
  installed .NET 8 Desktop Runtime) and compiles it into a setup.exe with Inno
  Setup. Run it from the repository root or anywhere — paths are resolved from
  this script's own location.

  Requires Inno Setup 6 (winget install JRSoftware.InnoSetup). The script looks
  for ISCC.exe in the per-user and per-machine install locations.

.EXAMPLE
  pwsh build\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$buildDir = $PSScriptRoot
$repoRoot = Split-Path $buildDir -Parent
$project = Join-Path $repoRoot 'src\Klaxon.App\Klaxon.App.csproj'
$publishDir = Join-Path $buildDir 'publish'
$iss = Join-Path $buildDir 'installer.iss'

Write-Host "==> Cleaning previous publish" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "==> Publishing framework-dependent ($Runtime, single-file)" -ForegroundColor Cyan
& dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Write-Host "==> Locating Inno Setup (ISCC.exe)" -ForegroundColor Cyan
$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe not found. Install it with: winget install JRSoftware.InnoSetup"
}

Write-Host "==> Compiling the installer" -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }

$output = Get-ChildItem (Join-Path $buildDir 'dist') -Filter '*.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ""
Write-Host ("Done: {0} ({1:N1} MB)" -f $output.FullName, ($output.Length / 1MB)) -ForegroundColor Green
