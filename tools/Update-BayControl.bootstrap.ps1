# Safe for: irm .../Update-BayControl.bootstrap.ps1 | iex
# Downloads the real updater and runs it as a file (param() cannot run under iex).
$ErrorActionPreference = "Stop"
$url = "https://raw.githubusercontent.com/jad3tx/bay-control/main/tools/Update-BayControl.ps1"
$f = Join-Path $env:TEMP "Update-BayControl.ps1"
Write-Host "Downloading updater..." -ForegroundColor Green
$raw = (Invoke-WebRequest -Uri $url -UseBasicParsing).Content
if ($raw -is [string]) {
    $text = $raw -replace "^\uFEFF", ""
    [System.IO.File]::WriteAllText($f, $text, [System.Text.UTF8Encoding]::new($false))
} else {
    $arr = [byte[]]$raw
    if ($arr.Length -ge 3 -and $arr[0] -eq 0xEF -and $arr[1] -eq 0xBB -and $arr[2] -eq 0xBF) {
        $arr = $arr[3..($arr.Length - 1)]
    }
    [System.IO.File]::WriteAllBytes($f, $arr)
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $f @args