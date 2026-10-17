# 実機の設定画面を 8 ページ撮影し、UI Automation で要素の矩形を JSON で出す。
# 使い方: powershell -File shoot-native.ps1 -Exe <Snapwell.Settings.exe> -Out out/native [-Prefix dark]
# 撮影は画面を CopyFromScreen で写す。設定画面は前面に出し、他のウィンドウを重ねないこと。
# 配色は OS の設定 (アプリのモード) に従う。このツールは OS の設定を変えない。
# ファイル名の接頭辞は、いまの OS の配色 (dark / light) を読んで決める。もう一方の配色は、利用者が OS の設定を切り替えて、もう一度実行する。
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Out, [string]$Prefix)
$ErrorActionPreference = 'Stop'
if (-not $Prefix) {
    $light = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue).AppsUseLightTheme
    $Prefix = if ($light -eq 0) { 'dark' } else { 'light' }
}
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
[DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
[DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
'@ -Name W -Namespace U
[U.W]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null   # Per-Monitor v2
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

# 並びは mockup 側のページ ID。Keys という名前の項目は辞書の .Keys と衝突するので、配列の組にする
$pages = @(
    @('general', '一般'), @('grid', '分割と間隔'), @('keys', 'ホットキー'), @('feel', '操作感'),
    @('rules', 'アプリのルール'), @('os', '標準機能'), @('sync', '同期とバックアップ'), @('look', '外観'))
$AE = [Windows.Automation.AutomationElement]
$p = Start-Process $Exe -PassThru
try {
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 40 -and $h -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh(); $h = $p.MainWindowHandle }
    if ($h -eq 0) { throw 'ウィンドウが出ない' }
    Start-Sleep -Seconds 3
    [U.W]::SetForegroundWindow($h) | Out-Null
    $dpi = [U.W]::GetDpiForWindow($h)
    $root = $AE::FromHandle($h)
    $cr = New-Object U.W+RECT; [U.W]::GetClientRect($h, [ref]$cr) | Out-Null
    $org = New-Object U.W+POINT; [U.W]::ClientToScreen($h, [ref]$org) | Out-Null
    $cw = $cr.R; $ch = $cr.B
    $scale = $dpi / 96.0
    $result = [ordered]@{ dpi = $dpi; scale = $scale; clientPx = @($cw, $ch); pages = [ordered]@{} }
    foreach ($pg in $pages) {
        $key = $pg[0]; $name = $pg[1]
        $el = $root.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
        if ($el) {
            try { $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() }
            catch { try { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() } catch {} }
        }
        Start-Sleep -Milliseconds 1000
        # マウスが載ったホバーの影響を避けるため、ウィンドウの外へ退避する
        [Windows.Forms.Cursor]::Position = New-Object Drawing.Point ($org.X - 20), ($org.Y - 20)
        $bmp = New-Object Drawing.Bitmap $cw, $ch
        [Drawing.Graphics]::FromImage($bmp).CopyFromScreen($org.X, $org.Y, 0, 0, $bmp.Size)
        $bmp.Save("$Out\$Prefix-$key.png", [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        # UIA の矩形 (物理 px) をクライアント左上基準・論理 px (÷scale) にして出す
        $items = @()
        foreach ($e in $root.FindAll('Descendants', [Windows.Automation.Condition]::TrueCondition)) {
            $c = $e.Current; $r = $c.BoundingRectangle
            if ($c.IsOffscreen -or $r.IsEmpty -or $r.Width -le 0) { continue }
            $items += [ordered]@{
                type = $c.ControlType.ProgrammaticName -replace '^ControlType\.', ''
                name = $c.Name; autoId = $c.AutomationId
                x = [math]::Round(($r.X - $org.X) / $scale, 2); y = [math]::Round(($r.Y - $org.Y) / $scale, 2)
                w = [math]::Round($r.Width / $scale, 2); h = [math]::Round($r.Height / $scale, 2)
            }
        }
        $result.pages["$Prefix-$key"] = $items
        "$name -> $Prefix-$key ($($items.Count) 要素)"
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$Out\$Prefix-elements.json"
}
finally { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
