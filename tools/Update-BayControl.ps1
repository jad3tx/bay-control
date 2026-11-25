#Requires -Version 5.1
<#
.SYNOPSIS
  On the sim PC: clone or pull Bay Control, publish a self-contained build, replace the install, restart.

.DESCRIPTION
  Do NOT pipe this script into iex — param() blocks break under Invoke-Expression.

  Typical host one-liner (after .NET 8 SDK + Git are installed):

    $f = "$env:TEMP\Update-BayControl.ps1"
    irm https://raw.githubusercontent.com/jad3tx/bay-control/main/tools/Update-BayControl.ps1 -OutFile $f
    powershell -NoProfile -ExecutionPolicy Bypass -File $f

  Or from an existing clone:

    .\tools\Update-BayControl.ps1

  Defaults:
    Source clone → %LOCALAPPDATA%\BayControl\src
    Install dir  → %LOCALAPPDATA%\BayControl\app
    Branch       → main

.EXAMPLE
  .\tools\Update-BayControl.ps1
  .\tools\Update-BayControl.ps1 -Tag v1.3.0
  .\tools\Update-BayControl.ps1 -RepoDir C:\Users\johnd\Projects\bay-control -SkipPull
  .\tools\Update-BayControl.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [string]$RepoUrl = "https://github.com/jad3tx/bay-control.git",
    [string]$RepoDir = "",
    [string]$InstallDir = "",
    [string]$Branch = "main",
    [string]$Tag = "",
    [switch]$SkipPull,
    [switch]$SkipTests,
    [switch]$NoLaunch,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Stop-BayControl {
    $procs = Get-Process -Name "BayControl" -ErrorAction SilentlyContinue
    if (-not $procs) { return }
    Write-Host "Stopping running BayControl…" -ForegroundColor Yellow
    foreach ($p in $procs) {
        try {
            $p.CloseMainWindow() | Out-Null
        } catch { /* ignore */ }
    }
    Start-Sleep -Seconds 1
    Get-Process -Name "BayControl" -ErrorAction SilentlyContinue | Stop-Process -Force
}

if (-not $RepoDir) {
    $RepoDir = Join-Path $env:LOCALAPPDATA "BayControl\src"
}
if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "BayControl\app"
}

Write-Host "Bay Control update" -ForegroundColor Green
Write-Host "  Repo:    $RepoUrl"
Write-Host "  Source:  $RepoDir"
Write-Host "  Install: $InstallDir"
if ($Tag) {
    Write-Host "  Ref:     tag $Tag"
} else {
    Write-Host "  Ref:     branch $Branch"
}

Assert-Command git
Assert-Command dotnet

$sdkOk = $false
foreach ($line in (dotnet --list-sdks)) {
    if ($line -match '^8\.') { $sdkOk = $true; break }
}
if (-not $sdkOk) {
    throw ".NET 8 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8.0"
}

if ($DryRun) {
    Write-Host "[DryRun] would clone/pull, publish, stop BayControl, copy to install, launch"
    exit 0
}

# --- clone or pull ---
if (-not (Test-Path (Join-Path $RepoDir ".git"))) {
    if ($SkipPull) {
        throw "No git repo at $RepoDir and -SkipPull was set."
    }
    Write-Host "Cloning into $RepoDir…"
    $parent = Split-Path -Parent $RepoDir
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if (Test-Path $RepoDir) {
        Remove-Item -Recurse -Force $RepoDir
    }
    git clone --branch $Branch $RepoUrl $RepoDir
    if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
} elseif (-not $SkipPull) {
    Write-Host "Updating $RepoDir…"
    Push-Location $RepoDir
    try {
        git fetch --tags --force origin
        if ($LASTEXITCODE -ne 0) { throw "git fetch failed" }
        if ($Tag) {
            git checkout --force "tags/$Tag"
            if ($LASTEXITCODE -ne 0) { throw "git checkout tag $Tag failed" }
        } else {
            git checkout --force $Branch
            if ($LASTEXITCODE -ne 0) { throw "git checkout $Branch failed" }
            git pull --ff-only origin $Branch
            if ($LASTEXITCODE -ne 0) { throw "git pull failed" }
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Host "SkipPull: using existing tree at $RepoDir"
}

$csproj = Join-Path $RepoDir "host\BayControl.Host.csproj"
if (-not (Test-Path $csproj)) {
    throw "BayControl.Host.csproj not found under $RepoDir — wrong folder?"
}

# --- test (optional) + publish ---
Push-Location $RepoDir
try {
    if (-not $SkipTests) {
        Write-Host "Running tests…"
        dotnet test BayControl.slnx -c Release -v q
        if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
    }

    $publishOut = Join-Path $RepoDir "artifacts\BayControl"
    if (Test-Path $publishOut) {
        Remove-Item -Recurse -Force $publishOut
    }

    Write-Host "Publishing win-x64 self-contained…"
    dotnet publish host/BayControl.Host.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -o $publishOut
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Copy-Item (Join-Path $RepoDir "Install-BayControl.ps1") $publishOut -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $RepoDir "INSTALL.txt") $publishOut -Force -ErrorAction SilentlyContinue
} finally {
    Pop-Location
}

# --- install ---
Stop-BayControl
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Write-Host "Copying build → $InstallDir"
robocopy $publishOut $InstallDir /MIR /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
# robocopy exit codes 0–7 are success
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed with exit $LASTEXITCODE"
}
$global:LASTEXITCODE = 0

$exe = Join-Path $InstallDir "BayControl.exe"
if (-not (Test-Path $exe)) {
    throw "BayControl.exe missing after copy"
}

# Start Menu + optional firewall via ship helper if present
$helper = Join-Path $InstallDir "Install-BayControl.ps1"
if (Test-Path $helper) {
    Write-Host "Running install helper (shortcut / firewall)…"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $helper -NoLaunch
}

Write-Host ""
Write-Host "Installed." -ForegroundColor Green
Write-Host "  $exe"

if (-not $NoLaunch) {
    Write-Host "Launching Bay Control…"
    Start-Process -FilePath $exe -WorkingDirectory $InstallDir
}

Write-Host "Done. Phone: same Wi‑Fi → scan QR → focus GSPro/Pureplay."
