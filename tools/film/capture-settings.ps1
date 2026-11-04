# 設定画面の実物を起動し、サイドバーの項目を選びながらページごとの画像を撮る。
# 例: pwsh capture-settings.ps1 -Exe <Snapwell.Settings.exe> -Out C:\temp\settings -Pages feel=操作感,keys=ホットキー
# -Pages は「出力名=サイドバーの表示名」の並び。表示名は -Probe で一覧できる。
# 出力は <Out>-<出力名>.png (ウィンドウの見た目の矩形。影は含めない)。
# 言語は設定画面の表示言語に従う。英語版を撮るときは %LOCALAPPDATA%\Snapwell\device.json の "Language" を
# 一時的に "en" にし、撮り終えたら元のファイルに戻す (バイト単位で同じことを確かめる)。
param(
    [string]$Exe,
    [string]$Out,
    [string[]]$Pages,
    [switch]$Probe
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
$p = Start-Process -FilePath $Exe -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { Write-Output 'no window'; exit 1 }
# 初回の描画が終わるまで待つ
Start-Sleep -Seconds 3
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
function Snap($name) {
    [W]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 800
    # 見た目の矩形 (DWMWA_EXTENDED_FRAME_BOUNDS = 9) と、見えない枠を含む矩形の差で切り抜く
    $r = New-Object W+RECT
    [W]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) | Out-Null
    $wr = New-Object W+RECT
    [W]::GetWindowRect($h, [ref]$wr) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($wr.R - $wr.L), ($wr.B - $wr.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    # PW_RENDERFULLCONTENT (2): WinUI の描画も取る
    [W]::PrintWindow($h, $dc, 2) | Out-Null
    $g.ReleaseHdc($dc); $g.Dispose()
    $cx = $r.L - $wr.L; $cy = $r.T - $wr.T
    $crop = $bmp.Clone((New-Object System.Drawing.Rectangle $cx, $cy, ($r.R - $r.L), ($r.B - $r.T)), $bmp.PixelFormat)
    $crop.Save("$Out-$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output ("saved $Out-$name.png " + ($r.R - $r.L) + 'x' + ($r.B - $r.T))
}
if ($Probe) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { $n = $e.Current.Name; if ($n) { Write-Output ($e.Current.ControlType.ProgrammaticName + ' | ' + $n) } }
} else {
    foreach ($pg in $Pages) {
        $parts = $pg.Split('=')
        $key = $parts[0]; $label = $parts[1]
        $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $label
        $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el) {
            try { $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
            catch { try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { Write-Output "cannot select $label" } }
            Start-Sleep -Seconds 2
        } else { Write-Output "not found $label" }
        Snap $key
    }
}
Stop-Process -Id $p.Id -Force
