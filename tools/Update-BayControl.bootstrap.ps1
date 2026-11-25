#Requires -Version 5.1
# Safe for: irm .../Update-BayControl.bootstrap.ps1 | iex
# Downloads the real updater (which has param()) and runs it as a file.
$ErrorActionPreference = "Stop"
$url = "https://raw.githubusercontent.com/jad3tx/bay-control/main/tools/Update-BayControl.ps1"
$f = Join-Path $env:TEMP "Update-BayControl.ps1"
Write-Host "Downloading updater…" -ForegroundColor Green
Invoke-WebRequest -Uri $url -OutFile $f -UseBasicParsing
& powershell -NoProfile -ExecutionPolicy Bypass -File $f @args
