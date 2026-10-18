# 設定画面の忠実度を測る一括実行: 実機の撮影 → モックアップの撮影 (実機と同じ大きさ・DPR) → 比較
# 使い方: powershell -File tools/fidelity/run.ps1 -Exe <Snapwell.Settings.exe> [-Out tools/fidelity/out]
# 事前に: cd tools/fidelity; npm install。Exe は dotnet publish src/Snapwell.Settings -c Release -r win-arm64 (x64 なら win-x64) で作る。
# 撮影・比較は一時のフォルダで行い、すべて成功したあとで Out に公開する。途中で失敗したら Out の前回の成果物には触れず、非 0 で終わる。
param([Parameter(Mandatory)][string]$Exe, [string]$Out)
$ErrorActionPreference = 'Stop'
$S = Split-Path $MyInvocation.MyCommand.Path
if (-not $Out) { $Out = Join-Path $S 'out' }
$Out = [IO.Path]::GetFullPath($Out)
$Tmp = "$Out.tmp-$([guid]::NewGuid().ToString('N').Substring(0, 8))"

function Invoke-Node {
    node @args
    if ($LASTEXITCODE -ne 0) { throw "node が失敗した (終了コード $LASTEXITCODE): $($args -join ' ')" }
}

try {
    New-Item -ItemType Directory -Force $Tmp | Out-Null
    & "$S\shoot-native.ps1" -Exe $Exe -Out "$Tmp\native"
    $files = @(Get-ChildItem "$Tmp\native\*-elements.json")
    if ($files.Count -eq 0) { throw '実機の撮影結果 (elements.json) がない' }
    $themes = ($files | ForEach-Object { $_.Name -replace '-elements.json$', '' }) -join ','
    $meta = Get-Content $files[0].FullName -Raw -Encoding utf8 | ConvertFrom-Json
    Invoke-Node "$S\shoot-mockup.mjs" --out "$Tmp\mock" --pxwidth $meta.clientPx[0] --pxheight $meta.clientPx[1] --dpr $meta.scale --themes $themes
    Invoke-Node "$S\compare.mjs" --mock "$Tmp\mock" --native "$Tmp\native" --out "$Tmp\report"
    foreach ($d in 'native', 'mock', 'report') {
        if (-not (Test-Path "$Tmp\$d")) { throw "$d の出力がない" }
    }
    # ここまで全部成功したときだけ、前回の成果物を差し替える
    New-Item -ItemType Directory -Force $Out | Out-Null
    foreach ($d in 'native', 'mock', 'report') {
        if (Test-Path "$Out\$d") { Remove-Item -Recurse -Force "$Out\$d" }
        Move-Item "$Tmp\$d" "$Out\$d"
    }
    "完了: $Out\report\report.md"
}
finally {
    if (Test-Path $Tmp) { Remove-Item -Recurse -Force $Tmp -ErrorAction SilentlyContinue }
}
