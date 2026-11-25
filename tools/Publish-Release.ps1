#Requires -Version 5.1
<#
.SYNOPSIS
  Commit pending work (optional), tag the csproj version, push to GitHub so CI cuts a release.

.DESCRIPTION
  Reads <Version> from host/BayControl.Host.csproj (e.g. 1.1.6 → tag v1.1.6).
  Pushing the tag triggers .github/workflows/release.yml, which builds the zip and
  publishes a GitHub Release.

.EXAMPLE
  # From repo root — commit current UI work, tag, push:
  .\tools\Publish-Release.ps1 -Message "Play-first layout: Play / More / Trackpad."

.EXAMPLE
  .\tools\Publish-Release.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [string]$Message = "",
    [switch]$SkipTests,
    [switch]$DryRun,
    [switch]$NoCommit
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    return (Resolve-Path (Join-Path $here "..")).Path
}

function Get-CsprojVersion([string]$csproj) {
    [xml]$xml = Get-Content -Raw $csproj
    $ver = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $ver) { throw "No <Version> found in $csproj" }
    return $ver.Trim()
}

$root = Get-RepoRoot
Set-Location $root

$csproj = Join-Path $root "host\BayControl.Host.csproj"
$version = Get-CsprojVersion $csproj
$tag = "v$version"

Write-Host "Bay Control release" -ForegroundColor Green
Write-Host "  Version: $version  →  tag $tag"
Write-Host "  Repo:    $root"

# Sanity: on a branch, not detached
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -eq "HEAD") { throw "Detached HEAD — check out main (or your release branch) first." }
Write-Host "  Branch:  $branch"

$status = git status --porcelain
$dirty = -not [string]::IsNullOrWhiteSpace($status)

if ($dirty) {
    Write-Host ""
    Write-Host "Uncommitted changes:" -ForegroundColor Yellow
    git status --short
    if ($NoCommit) {
        throw "Working tree is dirty. Commit first, or omit -NoCommit so this script can commit."
    }
    if (-not $Message) {
        $Message = "Release $tag"
    }
    if ($DryRun) {
        Write-Host "[DryRun] would: git add -A; git commit -m `"$Message`""
    } else {
        git add -A
        git commit -m $Message
        if ($LASTEXITCODE -ne 0) { throw "git commit failed (exit $LASTEXITCODE)" }
        Write-Host "Committed." -ForegroundColor Green
    }
} else {
    Write-Host "Working tree clean."
}

if (-not $SkipTests) {
    Write-Host ""
    Write-Host "Running tests…"
    if ($DryRun) {
        Write-Host "[DryRun] would: dotnet test BayControl.slnx -c Release -v q"
    } else {
        dotnet test BayControl.slnx -c Release -v q
        if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
        Write-Host "Tests passed." -ForegroundColor Green
    }
}

$existing = git tag -l $tag
if ($existing) {
    throw "Tag $tag already exists. Bump <Version> in host/BayControl.Host.csproj first."
}

Write-Host ""
if ($DryRun) {
    Write-Host "[DryRun] would:"
    Write-Host "  git tag -a $tag -m `"Bay Control $tag`""
    Write-Host "  git push origin $branch"
    Write-Host "  git push origin $tag"
    Write-Host ""
    Write-Host "CI would then publish BayControl-$tag-win-x64.zip on GitHub Releases."
    exit 0
}

git tag -a $tag -m "Bay Control $tag"
if ($LASTEXITCODE -ne 0) { throw "git tag failed." }

Write-Host "Pushing $branch and $tag…"
git push origin $branch
if ($LASTEXITCODE -ne 0) { throw "git push branch failed." }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push tag failed." }

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Actions:  https://github.com/jad3tx/bay-control/actions"
Write-Host "  Release:  https://github.com/jad3tx/bay-control/releases/tag/$tag"
Write-Host ""
Write-Host "When the workflow finishes, download BayControl-$tag-win-x64.zip on the sim PC,"
Write-Host "extract over your install (or run Install-BayControl.ps1), and restart BayControl."
