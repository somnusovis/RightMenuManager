param(
    [string]$Exe = "src\RightMenuManager\bin\Debug\net8.0-windows\RightMenuManager.exe",
    [string]$Out = "artifacts\main.png",
    [int]$WaitSeconds = 4,
    [switch]$KeepOpen
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class RmmCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
}
"@

[RmmCapture]::SetProcessDPIAware() | Out-Null

$root = Split-Path -Parent $PSScriptRoot
$exePath = if ([System.IO.Path]::IsPathRooted($Exe)) { $Exe } else { Join-Path $root $Exe }
$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
New-Item -ItemType Directory -Force (Split-Path -Parent $outPath) | Out-Null

$proc = Get-Process -Name RightMenuManager -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { $proc = Start-Process -FilePath $exePath -PassThru }
Start-Sleep -Seconds $WaitSeconds
$proc.Refresh()
if ($proc.HasExited) { Write-Output "PROCESS_EXITED $($proc.ExitCode)"; exit 1 }

$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { Write-Output "NO_MAIN_WINDOW"; exit 2 }

[RmmCapture]::ShowWindow($hwnd, 9) | Out-Null
[RmmCapture]::MoveWindow($hwnd, 40, 40, 1830, 1230, $true) | Out-Null
Start-Sleep -Milliseconds 900
[RmmCapture]::MoveWindow($hwnd, 40, 40, 1830, 1230, $true) | Out-Null
Start-Sleep -Milliseconds 900
[RmmCapture]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 400

$rect = New-Object RmmCapture+RECT
[RmmCapture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
Write-Output "RECT $($rect.Left),$($rect.Top) ${width}x${height}"

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
$ok = [RmmCapture]::PrintWindow($hwnd, $hdc, 2)
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()

if (-not $ok) {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
    $graphics.Dispose()
    Write-Output "PRINTWINDOW_FALLBACK"
}

$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output "SAVED $outPath"

if (-not $KeepOpen) {
    $proc.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 700
    if (-not $proc.HasExited) { $proc.Kill() }
}
