#Requires -Version 5.1
<#
.SYNOPSIS
  On the sim PC: download the latest Bay Control release (or build from source) and reinstall.

.DESCRIPTION
  Default path needs only Windows PowerShell + internet — downloads the GitHub release zip.
  No Git or .NET SDK required.

  One-liner:

    irm https://raw.githubusercontent.com/jad3tx/bay-control/main/tools/Update-BayControl.bootstrap.ps1 | iex

  FromSource path (needs Git + .NET 8 SDK):

    .\tools\Update-BayControl.ps1 -FromSource

.EXAMPLE
  .\tools\Update-BayControl.ps1
  .\tools\Update-BayControl.ps1 -Tag v1.3.0
  .\tools\Update-BayControl.ps1 -FromSource -SkipPull
#>
[CmdletBinding()]
param(
    [string]$RepoUrl = "https://github.com/jad3tx/bay-control.git",
    [string]$RepoApi = "https://api.github.com/repos/jad3tx/bay-control",
    [string]$RepoDir = "",
    [string]$InstallDir = "",
    [string]$Branch = "main",
    [string]$Tag = "",
    [switch]$FromSource,
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
    Write-Host "Stopping running BayControl..." -ForegroundColor Yellow
    foreach ($p in $procs) {
        try { $p.CloseMainWindow() | Out-Null } catch { }
    }
    Start-Sleep -Seconds 1
    Get-Process -Name "BayControl" -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Install-FromFolder([string]$SourceDir, [string]$DestDir) {
    Stop-BayControl
    New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    Write-Host "Copying build -> $DestDir"
    robocopy $SourceDir $DestDir /MIR /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed with exit $LASTEXITCODE"
    }
    $global:LASTEXITCODE = 0

    $exe = Join-Path $DestDir "BayControl.exe"
    if (-not (Test-Path $exe)) {
        throw "BayControl.exe missing after copy"
    }

    $helper = Join-Path $DestDir "Install-BayControl.ps1"
    if (Test-Path $helper) {
        Write-Host "Running install helper (shortcut / firewall)..."
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helper -NoLaunch
    }

    return $exe
}

if (-not $RepoDir) {
    $RepoDir = Join-Path $env:LOCALAPPDATA "BayControl\src"
}
if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "BayControl\app"
}

Write-Host "Bay Control update" -ForegroundColor Green
Write-Host "  Install: $InstallDir"
if ($FromSource) {
    Write-Host "  Mode:    from source ($RepoDir)"
} elseif ($Tag) {
    Write-Host "  Mode:    release tag $Tag"
} else {
    Write-Host "  Mode:    latest GitHub release"
}

if ($DryRun) {
    Write-Host "[DryRun] would download/build, stop BayControl, install, launch"
    exit 0
}

$publishOut = $null

if (-not $FromSource) {
    # --- release zip (default; no git / no SDK) ---
    $headers = @{ "User-Agent" = "BayControl-Update" }
    if ($Tag) {
        $tagName = if ($Tag.StartsWith("v")) { $Tag } else { "v$Tag" }
        Write-Host "Fetching release $tagName..."
        $release = Invoke-RestMethod -Uri "$RepoApi/releases/tags/$tagName" -Headers $headers
    } else {
        Write-Host "Fetching latest release..."
        $release = Invoke-RestMethod -Uri "$RepoApi/releases/latest" -Headers $headers
    }

    $asset = @($release.assets) | Where-Object { $_.name -like "*win-x64.zip" } | Select-Object -First 1
    if (-not $asset) {
        throw "No *win-x64.zip asset on release $($release.tag_name). Wait for CI or use -FromSource."
    }

    $zip = Join-Path $env:TEMP $asset.name
    $extract = Join-Path $env:TEMP ("baycontrol-extract-" + [guid]::NewGuid().ToString("N"))
    Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers $headers

    if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
    New-Item -ItemType Directory -Force -Path $extract | Out-Null
    Expand-Archive -Path $zip -DestinationPath $extract -Force

    # Zip may contain files at root or one nested folder
    $publishOut = $extract
    $nestedExe = Get-ChildItem -Path $extract -Filter BayControl.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($nestedExe) {
        $publishOut = $nestedExe.Directory.FullName
    }
} else {
    # --- from source ---
    Assert-Command git
    Assert-Command dotnet

    $sdkOk = $false
    foreach ($line in (dotnet --list-sdks)) {
        if ($line -match '^8\.') { $sdkOk = $true; break }
    }
    if (-not $sdkOk) {
        throw ".NET 8 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8.0"
    }

    if (-not (Test-Path (Join-Path $RepoDir ".git"))) {
        if ($SkipPull) {
            throw "No git repo at $RepoDir and -SkipPull was set."
        }
        Write-Host "Cloning into $RepoDir..."
        $parent = Split-Path -Parent $RepoDir
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        if (Test-Path $RepoDir) {
            Remove-Item -Recurse -Force $RepoDir
        }
        git clone --branch $Branch $RepoUrl $RepoDir
        if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
    } elseif (-not $SkipPull) {
        Write-Host "Updating $RepoDir..."
        Push-Location $RepoDir
        try {
            git fetch --tags --force origin
            if ($LASTEXITCODE -ne 0) { throw "git fetch failed" }
            if ($Tag) {
                $tagName = if ($Tag.StartsWith("v")) { $Tag } else { "v$Tag" }
                git checkout --force "tags/$tagName"
                if ($LASTEXITCODE -ne 0) { throw "git checkout tag $tagName failed" }
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
        throw "BayControl.Host.csproj not found under $RepoDir"
    }

    Push-Location $RepoDir
    try {
        if (-not $SkipTests) {
            Write-Host "Running tests..."
            dotnet test BayControl.slnx -c Release -v q
            if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
        }

        $publishOut = Join-Path $RepoDir "artifacts\BayControl"
        if (Test-Path $publishOut) {
            Remove-Item -Recurse -Force $publishOut
        }

        Write-Host "Publishing win-x64 self-contained..."
        dotnet publish host/BayControl.Host.csproj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -o $publishOut
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

        Copy-Item (Join-Path $RepoDir "Install-BayControl.ps1") $publishOut -Force -ErrorAction SilentlyContinue
        Copy-Item (Join-Path $RepoDir "INSTALL.txt") $publishOut -Force -ErrorAction SilentlyContinue
    } finally {
        Pop-Location
    }
}

$exe = Install-FromFolder -SourceDir $publishOut -DestDir $InstallDir

Write-Host ""
Write-Host "Installed." -ForegroundColor Green
Write-Host "  $exe"

if (-not $NoLaunch) {
    Write-Host "Launching Bay Control..."
    Start-Process -FilePath $exe -WorkingDirectory $InstallDir
}

Write-Host "Done. Phone: same Wi-Fi -> scan QR -> focus GSPro/Pureplay."
