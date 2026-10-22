# 忠実度ツール (tools/fidelity)

モックアップ (`docs/mockups/snapwell.html`) のブラウザ描画と、実機のスクリーンショットを同じ大きさで突き合わせ、差を測る補助ツール。
CSS を WinUI / GDI に移したときの解釈の違い (`border-radius: 9999px` が楕円になる、ToggleSwitch の寸法、整数矩形の隙間など) を、目視ではなく数字で見つけるために使う。本体のビルド・テストには含めない（`Snapwell.sln` にも入れない）。

現時点の対象は設定画面（8 ページ）。ランチャーなどのオーバーレイへの広げ方は「拡張」に書く。

## 必要なもの
- Windows 11、Node.js 20 以降、Microsoft Edge（ヘッドレスで撮影する。playwright の同梱ブラウザは要らない）
- 実機の exe。`dotnet publish src/Snapwell.Settings/Snapwell.Settings.csproj -c Release -r win-arm64 -o artifacts/fidelity-settings-arm64`（x64 の PC では `win-x64`。開発方針 のとおり、PowerShell から VS のインストーラーを PATH に足して実行する）
- 初回だけ: `cd tools/fidelity && npm install`（`playwright-core` と `pngjs` だけ。`node_modules` は Git に入れない）

## 使い方
一括実行:

```powershell
powershell -NoProfile -File tools/fidelity/run.ps1 -Exe artifacts/fidelity-settings-arm64/Snapwell.Settings.exe
```

撮影と比較は一時のフォルダ（`out.tmp-<乱数>`）で行い、すべて成功したときだけ `out/` を差し替える。途中で失敗したら非 0 で終わり、`out/` の前回の成果物には触れない（古い成果物を今回の結果として読まないため）。

出力は `tools/fidelity/out/`（Git に入れない）:

| 場所 | 中身 |
|---|---|
| `out/native/<配色>-<ページ>.png`、`<配色>-elements.json` | 実機の撮影と、UI Automation の要素の矩形 |
| `out/mock/<配色>-<ページ>.png`、`elements.json` | モックアップの撮影と、要素の矩形・角丸・背景色・文字色・文字の大きさ |
| `out/report/report.md`、`report.json` | 比較の結果（閾値を超えた箇所の一覧） |
| `out/report/<配色>-<ページ>-heat.png` | 差のある画素を赤く塗ったヒートマップ（実機の画像の上） |

個別に動かすこともできる:

```powershell
# 実機: 前面に出して撮るので、撮影中は他のウィンドウを重ねない。配色は OS の設定に従い、このツールは OS の設定を変えない
powershell -NoProfile -File tools/fidelity/shoot-native.ps1 -Exe <Snapwell.Settings.exe> -Out tools/fidelity/out/native
# モックアップ: 実機の clientPx と DPI から決めた大きさ・DPR で撮る
node tools/fidelity/shoot-mockup.mjs --out tools/fidelity/out/mock --pxwidth 1568 --pxheight 1159 --dpr 1.5 --themes dark,light --pages general,grid
# 比較
node tools/fidelity/compare.mjs --mock tools/fidelity/out/mock --native tools/fidelity/out/native --out tools/fidelity/out/report --tol 2 --pixtol 24 --hot 0.15
```

### 閾値（`compare.mjs` の引数）
- `--tol`（既定 2）: 位置・大きさの差の許容。論理 px
- `--pixtol`（既定 24）: 1 画素のチャンネル差（0〜255）がこれを超えたら「差のある画素」とする。アンチエイリアスの揺れを除くため
- `--hot`（既定 0.15）: 対応した要素の矩形の中で、差のある画素の割合がこれを超えたら一覧に載せる（寸法は合っているのに形や色が違う要素を拾う）

## 仕組み
1. 実機（`shoot-native.ps1`）: exe を起動し、UIA で左の一覧（`ListItem`）を選んで 8 ページを順に `CopyFromScreen` で撮る。クライアント領域（物理 px）と DPI を JSON に残す。全要素の `BoundingRectangle` を、クライアント左上基準・論理 px（物理 px ÷ DPI/96）にして出す
2. モックアップ（`shoot-mockup.mjs`）: `#settings` を開き、設定画面の枠を左上に固定して、実機のクライアント領域と同じ論理 px にする。`deviceScaleFactor` を実機の DPI/96 に合わせる。要素ごとに `getBoundingClientRect`（文字は `Range` の矩形）と computed style を出す
3. 比較（`compare.mjs`）:
   - 画像: 共通の大きさに切って、差のある画素の割合（全体・タイトルバー・左の一覧・本文）とヒートマップ
   - 寸法: モックアップの要素と UIA の要素を（種類, 名前）で対応づけ、Δx・Δy・Δw・Δh を出す。同名が複数あるときは位置が近いほう
   - 要素ごとの画素差: 対応した要素の矩形の中だけの差の割合

## 失敗したときの動き（偽の結果を出さない）
次の場合は撮影・比較を中断して非 0 で終わる。いずれも `out/` は更新しない。
- Node の失敗（`shoot-mockup.mjs`、`compare.mjs`）: `run.ps1` が各呼び出しの終了コードを確かめる
- 左の一覧に項目がない、または `SelectionItem` も `Invoke` も失敗した: 項目は名前と種類（`ListItem`）で探す
- 選んだあと、5 秒以内に本文側へページの見出しが出ない（遷移の確認）
- ロック画面・UAC の画面（入力デスクトップを開けない）、または設定画面が前面にない: ページごとの撮影の直前に確かめる。ロック中の画面を写すと、壁紙だけの画像で画素差 68 % の偽の結果が出たため

確認の方法: Node は `node_modules/pngjs` を一時的に外す、UIA は一覧の名前を存在しないものに変える、ロック画面は `OpenInputDesktop` が失敗するように一時的に書き換える（実際にロックはしていない）。いずれも終了コード 1 で、前回の `out/report` の更新時刻は変わらず、`out.tmp-*` も残らなかった。

## 読み方と限界
- 実機にあってモックアップにない要素（「準備中」の札、状態の文言、保存バー）は、画像の差を押し上げ、下の行の Δy を連鎖してずらす。表は上から読み、最初に外れた要素から直す
- 文字の UIA の矩形は、折り返す文では枠の幅になる。文字の Δw は括弧つきで、判定に使わない
- UIA には角丸と色が出ない。実機側の形・色の差は、要素ごとの画素差とヒートマップで見つける。モックアップ側の値（`radius`、`bg`、`color`）は `elements.json` にある
- 行の箱（`Border`）は UIA に出ないので、行の矩形の寸法比較はしない（行の中の文字・スイッチ・入力の位置で見る）
- **配色**: 実機は OS の配色に従う。このツールは OS の設定を変えない（開発方針 の「OS の設定は変えない」に合わせた）。ダークとライトの両方を測るには、OS の「アプリのモード」を利用者が切り替えて、もう一度 `run.ps1` を実行する。ファイル名の接頭辞（`dark` / `light`）は OS の現在の設定から決まる。モックアップは両方を常に撮れる（`--themes`）
- 画面の中のスクロール先は撮らない（見えている範囲だけ）。実機のほうが縦に長いページは、下の部分が対応なしになる

## 拡張: ランチャーなどのオーバーレイ
- `shoot-mockup.mjs` の `SCREENS` に「画面名 → 撮影の準備（枠の切り出し）」を足す。`PROBES` に要素の探し方を足す
- 実機側は UIA が使えない（GDI のレイヤードウィンドウ）ので、`shoot-native.ps1` を元に、ウィンドウを出すコマンドと矩形の取り方（`GetWindowRect`、またはアプリが出すレイアウトの JSON）を足す。画像の比較（`compare.mjs` の画像の差とヒートマップ）はそのまま使える
- 寸法の比較は、実機側の要素の矩形を `{type, name, x, y, w, h}` の同じ形で出せば、`compare.mjs` の対応づけがそのまま動く（`KIND_TO_UIA` に種類を足す）

## 測定結果（2026-10-18、origin/main `f3021b6` の `win-arm64` Release を publish、ダーク、DPI 144 = 150 %、クライアント 1568 × 1159 px）
`tools/fidelity/out/report/report.md` の要約。ライトは OS の配色を変えないため未測定。

| 画面 | 差のある画素 | 寸法・画素の超過（対応した要素） |
|---|---|---|
| general | 4.7 % | 26 / 34 |
| grid | 4.9 % | 19 / 27 |
| keys | 4.8 % | 9 / 17 |
| feel | 4.5 % | 21 / 29 |
| rules | 3.6 % | 12 / 20 |
| os | 4.4 % | 22 / 30 |
| sync | 3.6 % | 22 / 30 |
| look | 3.5 % | 22 / 30 |

読み取れること（general、os ページで確認）:
- **左の一覧**: 項目の文字が実機で右に 2.7 px（論理）ずれ、行の高さは 36 px で一致。アイコンと文字の間隔の解釈の違い
- **ToggleSwitch**: 寸法は 40 × 20 で一致（Δw・Δh = 0）。位置は x が 1 px 左。ただし要素内の画素差は 90 % 近い。原因（色かつまみの位置か）は未特定で、ヒートマップと `elements.json` の色で確かめる。実機は「準備中」で無効のスイッチも多く、その分は差が出る
- **ComboBox（言語）**: 高さが実機で 3 px 低い（35 → 32）
- **見出しと行の文字**: UIA の文字の高さがモックアップの文字の矩形より 3〜9 px 大きい（行間の違い）。行の文字の x は実機が 1.7 px 右
- **Δy の連鎖**: general では 3 行目以降が約 20 px 下にずれる。実機にだけある警告の文言や「準備中」の札で行が高くなるため（モックアップ側に反映するか、実機側を合わせるかは判断が要る）
- 本文の差の割合は、どのページも 3.5〜5.3 % で、ほとんどは文字の位置のずれによる

画素差の割合の読み方は、背景が一様なためページ全体では小さく出る。差を調べるときは、要素ごとの画素差とヒートマップを見る。
