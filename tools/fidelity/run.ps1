# 設定画面の忠実度を測る一括実行: 実機の撮影 → モックアップの撮影 (実機と同じ大きさ・DPR) → 比較
# 使い方: powershell -File tools/fidelity/run.ps1 -Exe <Snapwell.Settings.exe> [-Out tools/fidelity/out]
# 事前に: cd tools/fidelity; npm install。Exe は dotnet publish src/Snapwell.Settings -c Release -r win-arm64 (x64 なら win-x64) で作る。
param([Parameter(Mandatory)][string]$Exe, [string]$Out)
$ErrorActionPreference = 'Stop'
$S = Split-Path $MyInvocation.MyCommand.Path
if (-not $Out) { $Out = Join-Path $S 'out' }
$Out = [IO.Path]::GetFullPath($Out)
& "$S\shoot-native.ps1" -Exe $Exe -Out "$Out\native"
$files = Get-ChildItem "$Out\native\*-elements.json"
$themes = ($files | ForEach-Object { $_.Name -replace '-elements.json$', '' }) -join ','
$meta = Get-Content "$($files[0].FullName)" -Raw -Encoding utf8 | ConvertFrom-Json
node "$S\shoot-mockup.mjs" --out "$Out\mock" --pxwidth $meta.clientPx[0] --pxheight $meta.clientPx[1] --dpr $meta.scale --themes $themes
node "$S\compare.mjs" --mock "$Out\mock" --native "$Out\native" --out "$Out\report"
