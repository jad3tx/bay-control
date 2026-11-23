#Requires -Version 5.1
<#
.SYNOPSIS
  Optional setup helper for Bay Control release zip.
#>
[CmdletBinding()]
param(
    [switch]$NoAutostart,
    [switch]$NoFirewall,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root "BayControl.exe"

if (-not (Test-Path $exe)) {
    Write-Error "BayControl.exe not found next to this script. Extract the full release zip first."
}

Write-Host "Bay Control setup" -ForegroundColor Green
Write-Host "Install folder: $root"

# Start Menu shortcut
$programs = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $programs "Bay Control.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $root
$shortcut.Description = "Bay Control — GSPro / Pureplay remote"
$shortcut.Save()
Write-Host "Start Menu shortcut: $shortcutPath"

# Autostart
if (-not $NoAutostart) {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $runKey -Name "BayControl" -Value "`"$exe`""
    Write-Host "Start with Windows: enabled (uncheck in the app to disable)"
} else {
    Write-Host "Start with Windows: skipped"
}

# Firewall (private/domain)
if (-not $NoFirewall) {
    $ruleName = "Bay Control 8787"
    $arg = "advfirewall firewall add rule name=`"$ruleName`" dir=in action=allow protocol=TCP localport=8787 profile=private,domain"
    try {
        Start-Process -FileName "netsh" -ArgumentList $arg -Verb RunAs -Wait -WindowStyle Hidden
        Write-Host "Firewall: attempted rule '$ruleName' (approve UAC if prompted)"
    } catch {
        Write-Warning "Firewall rule not added. Use 'Add firewall rule' in the Bay Control dashboard."
    }
} else {
    Write-Host "Firewall: skipped"
}

Write-Host ""
Write-Host "Next: phone on SAME Wi-Fi → scan QR → focus GSPro/Pureplay"
Write-Host "Docs: https://github.com/jad3tx/bay-control/blob/main/docs/install.md"

if (-not $NoLaunch) {
    Start-Process -FilePath $exe -WorkingDirectory $root
}
