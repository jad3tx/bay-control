param(
    [string]$TitleMatch = "teetime",
    [string]$OutPath = "$env:TEMP\teetime-capture.png"
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$code = @'
using System;
using System.Runtime.InteropServices;
public static class NativeWin {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@
Add-Type -TypeDefinition $code

$p = Get-Process mstsc, msrdc -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -like "*$TitleMatch*" -and $_.MainWindowHandle -ne 0 } |
    Select-Object -First 1

if (-not $p) {
    Write-Error "No RDP window matching '$TitleMatch'"
    exit 1
}

$rect = New-Object NativeWin+RECT
[void][NativeWin]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
$w = [Math]::Max(1, $rect.Right - $rect.Left)
$h = [Math]::Max(1, $rect.Bottom - $rect.Top)
[void][NativeWin]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 200

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Output $OutPath
