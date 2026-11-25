$ErrorActionPreference = "Stop"
Write-Host "Bay Control install/update" -ForegroundColor Green
$installDir = Join-Path $env:LOCALAPPDATA "BayControl\app"
$headers = @{ "User-Agent" = "BayControl-Install" }
Write-Host "Fetching latest release..."
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/jad3tx/bay-control/releases/latest" -Headers $headers
$asset = @($release.assets) | Where-Object { $_.name -like "*win-x64.zip" } | Select-Object -First 1
if (-not $asset) { throw "No win-x64.zip on $($release.tag_name) yet - wait for CI." }
Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size/1MB,1)) MB)..."
$zip = Join-Path $env:TEMP $asset.name
$extract = Join-Path $env:TEMP ("bay-" + [guid]::NewGuid().ToString("N"))
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers $headers
if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
New-Item -ItemType Directory -Force -Path $extract | Out-Null
Expand-Archive -Path $zip -DestinationPath $extract -Force
$exeFile = Get-ChildItem -Path $extract -Filter BayControl.exe -Recurse | Select-Object -First 1
if (-not $exeFile) { throw "BayControl.exe not found in zip" }
$src = $exeFile.Directory.FullName
Write-Host "Stopping BayControl if running..."
Get-Process -Name BayControl -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Write-Host "Installing to $installDir"
robocopy $src $installDir /MIR /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $LASTEXITCODE" }
$global:LASTEXITCODE = 0
$helper = Join-Path $installDir "Install-BayControl.ps1"
if (Test-Path $helper) {
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helper -NoLaunch
}
$exe = Join-Path $installDir "BayControl.exe"
Write-Host "Launching..." -ForegroundColor Green
Start-Process -FilePath $exe -WorkingDirectory $installDir
Write-Host "Done. Same Wi-Fi -> scan QR -> focus GSPro/Pureplay."