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
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint f, bool inherit, uint access);
[DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr d);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
'@ -Name W -Namespace U
[U.W]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null   # Per-Monitor v2
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

# 並びは mockup 側のページ ID。3 つ目は、ナビの名前と見出しが違うページの見出し。Keys という名前の項目は辞書の .Keys と衝突するので、配列の組にする
$pages = @(
    @('general', '一般'), @('grid', '分割と間隔'), @('keys', 'ホットキー'), @('feel', '操作感'),
    @('rules', 'アプリのルール', 'アプリごとのルール'), @('os', '標準機能', 'Windows の標準機能'), @('sync', '同期とバックアップ'), @('look', '外観'))
$AE = [Windows.Automation.AutomationElement]
$p = Start-Process $Exe -PassThru
try {
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 40 -and $h -eq 0; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh(); $h = $p.MainWindowHandle }
    if ($h -eq 0) { throw 'ウィンドウが出ない' }
    Start-Sleep -Seconds 3
    # ロック画面や別のデスクトップでは、画面を写しても壁紙などになる。偽の結果を出さないため、撮る前と各ページの撮影ごとに確かめる
    function Assert-Capturable {
        # 入力を受けるデスクトップが開けない (ロック画面・UAC の画面) とき
        $d = [U.W]::OpenInputDesktop(0, $false, 0x100)
        if ($d -eq [IntPtr]::Zero) { throw '入力デスクトップを開けない (ロック画面・UAC の画面の可能性)。撮影を中断する' }
        [U.W]::CloseDesktop($d) | Out-Null
        for ($k = 0; $k -lt 10 -and [U.W]::GetForegroundWindow() -ne $h; $k++) { [U.W]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 200 }
        if ([U.W]::GetForegroundWindow() -ne $h) { throw '設定画面が前面にない。撮影を中断する' }
    }
    Assert-Capturable
    $dpi = [U.W]::GetDpiForWindow($h)
    $root = $AE::FromHandle($h)
    $cr = New-Object U.W+RECT; [U.W]::GetClientRect($h, [ref]$cr) | Out-Null
    $org = New-Object U.W+POINT; [U.W]::ClientToScreen($h, [ref]$org) | Out-Null
    $cw = $cr.R; $ch = $cr.B
    $scale = $dpi / 96.0
    $result = [ordered]@{ dpi = $dpi; scale = $scale; clientPx = @($cw, $ch); pages = [ordered]@{} }
    foreach ($pg in $pages) {
        $key = $pg[0]; $name = $pg[1]; $heading = if ($pg.Count -gt 2) { $pg[2] } else { $name }
        # 左の一覧の項目を、名前と種類 (ListItem) の両方で限定して探す
        $cond = New-Object Windows.Automation.AndCondition(
            (New-Object Windows.Automation.PropertyCondition($AE::NameProperty, $name)),
            (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)))
        $el = $root.FindFirst('Descendants', $cond)
        if (-not $el) { throw "左の一覧に「$name」が見つからない。撮影を中断する" }
        $selected = $false
        try { $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $selected = $true } catch {}
        if (-not $selected) {
            try { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); $selected = $true } catch {}
        }
        if (-not $selected) { throw "「$name」を選べない (SelectionItem も Invoke も失敗)。撮影を中断する" }
        # 遷移の確認: 本文側 (左の一覧より右) にページ名の見出しが出るまで、最大 5 秒待つ
        $navRight = $org.X + 232 * $scale
        $arrived = $false
        for ($k = 0; $k -lt 25 -and -not $arrived; $k++) {
            $hits = $root.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($AE::NameProperty, $heading)))
            foreach ($t in $hits) {
                if ($t.Current.ControlType -eq [Windows.Automation.ControlType]::Text -and $t.Current.BoundingRectangle.X -ge $navRight) { $arrived = $true }
            }
            if (-not $arrived) { Start-Sleep -Milliseconds 200 }
        }
        if (-not $arrived) { throw "「$name」のページへ遷移しなかった (見出し「$heading」が出ない)。撮影を中断する" }
        Start-Sleep -Milliseconds 1000
        # マウスが載ったホバーの影響を避けるため、ウィンドウの外へ退避する
        [Windows.Forms.Cursor]::Position = New-Object Drawing.Point ($org.X - 20), ($org.Y - 20)
        Assert-Capturable
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
