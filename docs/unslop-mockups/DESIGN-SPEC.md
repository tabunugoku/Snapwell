# Snapwell UI 統一の仕様（実装役への引き継ぎ・最終版）

状態: **採用済み（25 件すべて。2026-11-01 に利用者が決定）**。除外する項目はありません。

この文書は、新しいデザイン（unslop の改修）を実装に移すための仕様です。値の正本は `tokens.css` です。ここでは、その値を WinUI 3 のリソースと Win32 のオーバーレイの値に読み替えます。単位は、特に書かない限り DIP（論理 px）です。オーバーレイでは、モニターの拡大率 `scale` を掛けて物理 px にします。

## 0. 見た目の正本

| ファイル | Git | 役割 |
|---|---|---|
| `docs/mockups/snapwell.html` | 管理下 | **実装で見比べる正本**。設定画面・トレイ・ランチャー・区画のプレビュー・初回セットアップを、新しいデザインで動かせる（ライト／ダーク、日本語／英語） |
| `artifacts/ui-components-dimensional-drawing.html` | 管理外 | 部品の寸法図（Rev B・2026-11-01）。ボタン・タグ・キー・行・ナビ・ランチャー・プリセット・区画・一覧・トレイの寸法 |
| `docs/unslop-mockups/`（`index.html`・`tokens.css`・`kit.css`・`kit.js`・`settings.html`・`tray.html`・`launcher.html`） | 管理下 | 改修案の元。`index.html` の表が下の 25 件。値の正本は `tokens.css` |

寸法図（`artifacts/`）は Git に入っていないので、新しい worktree にはありません。実装役に渡すときは、この文書と `docs/mockups/snapwell.html` を見るように指示します。`docs/unslop-mockups/` の HTML は JavaScript で組み立てるので、`file://` ではなく `python -m http.server 8765 --bind 127.0.0.1` で開きます。

`kit.css` の TextBox はフォーカスで下線を引いていますが、**この文書（下線なし・輪だけ）を正とします**。

## 1. 採用した変更（25 件）

| # | 項目 | 変更前 | 採用後 |
|---|---|---|---|
| 1 | 操作のボタンの高さ | 28（文字 13） | 32（文字 14）。WinUI 3 の標準にそろえる |
| 2 | 角丸 | ボタン 6、ナビ 6 と 7、カード 8、トレイ 8（寸法図は 12）、一覧 4、セットアップのカード 12、タグは丸 | 操作の部品 4、面（カード・メニュー・ランチャー・プレビュー・ウィンドウ）8、トグルだけ 10 |
| 3 | 状態のタグ | 大文字・字間 0.05em・太字 10.5 の色付きの丸いタグ | 字間なし・通常の太さ 12 の中立のタグ（背景 raised・文字 text-2）。意味の色は 6 px の点だけ |
| 4 | アクセントの色 | 区画の青 #1F6C9F、導入の青 #2563EB（IntroPalette）、フォーカスの青の 3 種 | 区画の青 1 色（ライト #1F6C9F、ダーク #8CCBF2）。#2563EB / #6AA8FF をやめる |
| 5 | 区画の塗り（ライト） | 不透明の #E1F3FE | 区画の青の 18% の半透明（ダークは 16%）。下のウィンドウが透ける |
| 6 | 予測の線の終点 | 半径 4 の丸 | 8 × 8・角 2 の四角 |
| 7 | トレイの項目のアイコン | なし（文字だけ） | 16 px の線のアイコン（線 1.5）。更新の項目だけアクセント |
| 8 | 設定の背景 | 内容の面が白、行の枠が線 | 内容はキャンバス #F5F5F3、行は白いカード |
| 9 | ランチャーの選択範囲 | セルごとに青で塗り、セルごとに枠（角 4） | 選んだセルをまとめた 1 つの面（角 8・zone-fill・内側に accent 1.5 の枠）、中央に大きさの札。セルは角 4 のまま |
| 10 | 入力欄と ComboBox のフォーカス | WinUI 3 の標準の下のアクセントの線 | 下の線をやめ、2 px の輪だけ |
| 11 | ナビ | 幅 232、項目の左右 10、印 3 × 18 | 幅 240、左右 8、印 3 × 16。印は select のばねで移る |
| 12 | タイトルバー | 高さ 36、左右 14 | 高さ 32、左右 16 |
| 13 | 文字の大きさ | 10.5・11.5・12.5・13・14・26 | 12/16、12/20、14/20、20/28、28/36 の 5 段。太さは 400 と 600 だけ |
| 14 | ページの見出し | 游明朝 26、字間 −0.02em | 游明朝 28/36、字間なし、太さ 400 |
| 15 | ようこその見出し | 「ウィンドウを投げよう。」 / "Throw a window." | 「ウィンドウを投げよう」 / "Throw a window"（見出しに句点を付けない） |
| 16 | フォーカスの輪 | ナビは BlueText 2 px・角 10、トレイは 1 px | どこでもアクセント 2 px。部品の外側に 2 px あける（トレイは内側に描く） |
| 17 | トレイのメニュー | 余白 7、見出し 51（太字 13）、モニター構成の行 38 | 余白 4、見出し 56（通常 14）、モニター構成の行 28 |
| 18 | ランチャー | 余白 15、1 行目 27.2、プリセット 29.2 / 間 6 / 図 18 × 12、タグ 18.8 | 余白 16、行の間 12、1 行目 32、プリセット 32 / 間 4 / 図 20 × 12、タグ 20 |
| 19 | ランチャーの幅と上の位置 | min(560, 作業領域 − 28)、上から 14 | min(560, 作業領域 − 32)、上から 16 |
| 20 | ランチャーのウィンドウの一覧 | 行 28 | 行 32。メインは accent の輪と点、スタックは text-3 の輪。整列のボタンは図つきで 4 等分 |
| 21 | セットアップのカード | 角 12、フッターの余白 20,14、段の間 8 | 角 8、フッターの余白 24,16、段の間 12 |
| 22 | 行の高さとグループの間 | 行 46.4（中身で決まる）、グループの間 22、題 12.5 | 行の最小 56、グループの間 24、題 14 / 600 |
| 23 | キーの表示 | 高さ 22、下に線なし | 高さ 24、下の線 2 px |
| 24 | メニューとランチャーの出方 | すぐ出る | 不透明度と 4 px の浮き上がりを surface のばねで。視覚効果がオフならすぐ出る |
| 25 | 既存モックアップのトレイのプロファイル行 | モックアップにだけある | 再現しない。`docs/DESIGN.md` §16 の 4 項目に従う（`docs/mockups/snapwell.html` からは削除済み） |

文言の変更は 2 つです（用語集の語は変えていません）。2 つ目は #6（終点を四角にする）に合わせて足したもので、25 件の表にはありません。

| キー | 言語 | 変更前 | 変更後 |
|---|---|---|---|
| `Setup.Welcome.Title`（`src/Snapwell.Core/Localization/Strings/setup/`） | ja | ウィンドウを投げよう。 | ウィンドウを投げよう |
| | en | Throw a window. | Throw a window |
| `Look_PredictionLineDescription.Text`（`…/Strings/look/`） | ja | 点線と丸印で示します | 点線と小さな四角で示します |
| | en | a dotted line and dot | a dotted line and a small square |
| | zh-Hans | 用虚线和圆点 | 用虚线和小方块 |
| | zh-Hant | 用虛線和圓點 | 用虛線和小方塊 |

## 2. 方針

| | |
|---|---|
| 製品 | Windows 11 に常駐するウィンドウマネージャー。投げたウィンドウが、ばねと磁石で区画に吸い込まれる |
| 性格 | 正確・静か・少し手触りがある |
| 世界 | 製図台と方眼紙、磁石とばね。インクの黒と、区画の青 1 色だけ |
| 見せ場 | 区画のプレビューと予測の線、操作感のばねの曲線、ランチャーの範囲選択。色と動きはこの 3 か所に集める |
| 署名 | 投げの軌跡: 4/4 の点線が、角 2 の小さな四角で止まる。ロゴ、トレイのアイコン、予測の線の終点、操作感の曲線の目標点だけに使う |

既存のブランド（インクの黒の主ボタン、温かい無彩色、游明朝のページ題、区画の青）は保ちます。新しい色や書体は足しません。

## 3. トークン

### 3.1 色

アクセントを使うのは、区画・予測の線・フォーカスの輪・ランチャーの選択（とその「対象」のタグ）・トレイの更新の項目のアイコンだけです。主ボタン、トグルのオン、チェックはインクのままにします。

| トークン | ライト | ダーク | 使う場所 | 現状のキー（App.xaml） |
|---|---|---|---|---|
| canvas | `#F5F5F3` | `#1A1A19` | ナビのペイン、内容の下地、タイトルバー、フッター | `SnapwellBackgroundBrush` |
| surface | `#FFFFFF` | `#222221` | カード、メニュー、ランチャー、ダイアログ | `SnapwellSurfaceBrush` |
| raised | `#EFEFED` | `#2C2C2B` | ホバー、入力欄、キー、選択中の行、タグ | `SnapwellSurface2Brush` |
| line | `rgba(28,28,27,.09)` | `rgba(255,255,255,.08)` | カードの枠、行の区切り | `SnapwellBorderBrush` |
| line-strong | `rgba(28,28,27,.18)` | `rgba(255,255,255,.15)` | 入力欄・ボタンの枠、メニューの枠 | `SnapwellBorderStrongBrush` |
| text | `#1C1C1B` | `#EDEDEB` | 本文、行の題 | `SnapwellTextBrush` |
| text-2 | `#52524F` | `#B4B4B0` | 行の説明、リード、タグの文字 | `SnapwellMutedBrush` |
| text-3 | `#6B6B67` | `#92928E` | 件数、メタ、無効（白で 5.3:1、ダークの surface で 5.4:1） | 新設 `SnapwellSubtleBrush` |
| ink | `#1C1C1B` | `#EDEDEB` | 主ボタン、トグルのオン、チェック | `SnapwellInkBrush` |
| ink-hover | `#3A3A38` | `#D2D2CF` | 主ボタンのホバー | `SnapwellInkHoverBrush` |
| on-ink | `#FFFFFF` | `#161616` | 主ボタンの文字、つまみ | `SnapwellInkTextBrush` |
| accent | `#1F6C9F` | `#8CCBF2` | 上のアクセントの場所だけ | `SnapwellBlueTextBrush`、`SystemAccentColor*` |
| accent-tint | `rgba(31,108,159,.12)` | `rgba(140,203,242,.14)` | ランチャーの一覧の選択の背景、アクセントのタグ | `SnapwellBlueBackgroundBrush` |
| accent-line | `rgba(31,108,159,.6)` | `rgba(140,203,242,.65)` | 区画の点線、予測の線 | `SnapwellBlueLineBrush` |
| zone-fill | `rgba(31,108,159,.18)` | `rgba(140,203,242,.16)` | 区画のプレビュー、ランチャーの選択範囲、導入の区画 | （オーバーレイと導入のみ） |
| positive | `#2F7A3E` | `#7CC48A` | タグの点、成功の InfoBar のアイコン | `SnapwellGreenTextBrush` |
| caution | `#8A5A00` | `#E0B65A` | タグの点、警告 | `SnapwellYellowTextBrush` |
| negative | `#B42318` | `#F2938C` | タグの点、エラーの文字とアイコン | `SnapwellRedTextBrush` |
| caution-tint / negative-tint | `rgba(138,90,0,.08)` / `rgba(180,35,24,.08)` | `rgba(224,182,90,.12)` / `rgba(242,147,140,.12)` | InfoBar の背景 | `SnapwellYellowBackgroundBrush` / `SnapwellRedBackgroundBrush` |

- `SnapwellGreenBackgroundBrush` は、タグを中立にするので使わなくなります。参照がなくなったら消します。
- `App.xaml` の `Default` の辞書は `Dark` の写しのまま保ちます（WinUI 3 の仕様）。値を変えるときは 2 か所とも変えます。
- `MainWindow.xaml` が `NavigationView*` のブラシを重ねて定義しているので、同じ値にそろえるか、`App.xaml` 側に寄せて消します。

### 3.2 文字

| 役割 | 大きさ / 行の高さ | 太さ | WinUI 3 のスタイル |
|---|---|---|---|
| caption | 12 / 16 | 400 | `SnapwellTag*TextStyle`、`SnapwellKbdTextStyle`、件数 |
| desc | 12 / 20 | 400 | `SnapwellRowDescriptionStyle`、`SnapwellHintTextStyle` |
| body | 14 / 20 | 400 | `SnapwellRowTitleStyle`、ボタン、ナビ、メニュー |
| group | 14 / 20 | 600 | `SnapwellGroupTitleStyle`（いまは 12.5 SemiBold） |
| subtitle | 20 / 28 | 600 | ダイアログの題 |
| title | 28 / 36 | 400（游明朝） | `SnapwellPageTitleStyle`、`SetupHeadingStyle` |

- 書体は `Segoe UI Variable Text` → `Yu Gothic UI`、題は `Yu Mincho`、等幅は `Cascadia Mono`。キーは既存の `SnapwellUiFontFamily`・`SnapwellDisplayFontFamily`・`SnapwellMonoFontFamily` のまま。
- `CharacterSpacing` は 0 に戻します（ページ題の -20、タグの 50 をやめる）。タグは大文字にしません（`Typography.Capitals` などを外す）。
- 太さは 400 と 600 だけです。強調は太さでなく色（text と text-2 の差）で付けます。
- **オーバーレイの文字は GDI（`GdiOverlayText`）で、通常と太字しか出せません。** トレイとランチャーの文字は通常（400）にそろえ、トレイの題「Snapwell」も太字にしません。

### 3.3 余白・角・線

- 余白は 4 の倍数だけ: 4・8・12・16・20・24・32・40・48。
- 角: 操作の部品（ボタン、入力、行の選択、タグ、キー、区画のセル）は **4**。面（カード、ウィンドウ、メニュー、ランチャー、区画のプレビュー、ランチャーの選択範囲、セットアップのカード）は **8**。トグルのトラックだけ **10**（高さの半分）。
- 線: 1 px。区画の点線と予測の線だけ 1.5 px・4/4 の点線。キーの下だけ 2 px。
- フォーカスの輪: アクセント 2 px、部品の外側に 2 px あけ、角は部品の角 + 2。WinUI 3 では `FocusVisualPrimaryBrush` をアクセント、`FocusVisualSecondaryBrush` を透明、`FocusVisualMargin="-2"`、`FocusVisualPrimaryThickness="2"`、`FocusVisualSecondaryThickness="0"` にします。トレイのキーボード選択は、項目の内側に 2 px で描きます（いまは 1 px）。

### 3.4 動き（物理モデル）

継続時間とイージング曲線は使いません。`k = m(2πf)²`、`c = 2ζ√(km)` で、240 Hz の固定刻みで積分します（既存の `SpringToggle` と同じ）。1 フレームの経過は 1/30 秒までに切ります。

| 名前 | m | f (Hz) | ζ | 使う場所 | 備考 |
|---|---|---|---|---|---|
| toggle | 3 | 2.49 | 0.82 | トグルのつまみ | `FeelPreset.StiffHeavy`。既存のまま |
| surface | 1 | 4.5 | 1.0 | トレイのメニューとランチャーの出現（不透明度 0→1 と、4 px 下から浮き上がる） | 新設。臨界減衰で行き過ぎない |
| select | 1 | 6 | 1.0 | ナビの選択の印、操作感のチップの選択 | 新設。`docs/mockups/snapwell.html` の `makeNavIndicator` が同じ式（surface は `appear`） |

- ホバーと押下の色はすぐ変えます（アニメーションしない）。
- 「視覚効果のアニメーション」がオフ（`SPI_GETCLIENTAREAANIMATION` が FALSE）のときは、すべて最終値にすぐ置きます。
- 操作感のプリセット（標準 m 0.6・f 2.4・ζ 0.7、きびきび 0.8・3.2・0.6、なめらか 1.0・1.8・0.8、弾む 0.5・2.8・0.45）と吸着の物理は変えません。

## 4. 部品の寸法と状態（設定画面・WinUI 3）

| 部品 | 寸法 | 状態 | 変える場所 |
|---|---|---|---|
| ボタン | 高さ 32、左右 12、最小幅 80、角 4、文字 body | 通常: surface + line-strong / ホバー: raised / 押下: raised + 文字 text-2 / 無効: raised + 文字 text-3 / フォーカス: 3.3 の輪 | `SnapwellButtonStyle`、`SnapwellActionButtonStyle`（いま 28・14・72・角 6・13） |
| 主ボタン | 同上 | ink / ink-hover / 無効は raised + text-3 | `SnapwellPrimaryButtonStyle`、`AccentButton*` のブラシ |
| 小ボタン（チップ） | 高さ 24、左右 8、角 4、文字 caption | 選択中: raised + 枠 line-strong + 文字 text・600 | `FeelPage` の `ChipButtonStyle`・`SelectedChipButtonStyle` |
| 入力欄・ComboBox | 高さ 32、角 4、枠 line-strong、下の線なし | フォーカス: 3.3 の輪だけ | `TextControlBorderBrushFocused`、`ComboBox*` |
| トグル | 40 × 20、つまみ 12（内側 4）、角 10 | オフ: 枠 text-2・つまみ text-2 / オン: ink 塗り・つまみ on-ink / 無効: 40% | `SnapwellToggleSwitchStyle`（寸法は既存のまま。色と輪だけ） |
| チェック | 20 × 20、角 4 | オン: ink 塗り + on-ink のチェック | `CheckBox*` |
| タグ | 高さ 20、左右 6、角 4、文字 caption 400、背景 raised、文字 text-2 | 意味があるときは先頭に 6 px の点（positive / caution / negative）、点と文字の間 6 | `SnapwellTagBorderStyle`・`SnapwellTag*TextStyle`（いま丸・10.5・SemiBold・字間 50・色付きの背景） |
| アクセントのタグ | 同上 | 背景 accent-tint、文字 accent、点なし（ランチャーの「対象」だけ） | オーバーレイ側（6 章） |
| キー | 高さ 24、最小幅 24、左右 8、角 4、背景 raised、枠 line-strong、下の線 2 px、文字 caption の等幅 | — | `SnapwellKbdBorderStyle`・`SnapwellKbdTextStyle`（いま 22・6。文字の −2 の補正は測り直す） |
| 行 | 最小の高さ 56、上下 12、左右 16、列の間 16 | — | `SnapwellRowGridStyle`、`SnapwellRowDividerStyle` |
| カード（行の束） | 角 8、背景 surface、枠 line | — | `SnapwellRowsBorderStyle` |
| グループの題 | group、上 24、下 8 | — | `SnapwellGroupTitleStyle`（いま 12.5・上 22） |
| ページの題 | title、字間 0、下 4。リードは body / text-2、下 24 | — | `SnapwellPageTitleStyle`、`SnapwellLeadTextStyle` |
| ナビ | ペイン幅 240、項目 36、間 2、左右 8、角 4、アイコン 16（線 1.5）とラベルの間 12、ラベルの左 16 | ホバー: raised / 選択: raised + 左に 3 × 16 の ink の印（select のばねで移る） / フォーカス: 3.3 の輪 | `MainWindow.xaml` の `NavigationView`（`OpenPaneLength` 232→240、項目の `Margin` 10→8、角 6/7→4、印 18→16） |
| タイトルバー | 高さ 32、左右 16、背景 canvas | — | `MainWindow.xaml`（いま 36・14） |
| フッター | 高さ 64、左右 32、背景 canvas、状態の文字は desc / text-2 | 変更あり: caution の点 / 失敗: negative | `MainWindow.xaml`（いま `Padding 32,17.67`） |
| InfoBar | 角 8、背景 *-tint、アイコンだけ意味の色 | — | `InfoBar*` のブラシ（App.xaml に追加） |
| ContentDialog | 角 8、余白 24、題 subtitle、ボタン 32 | 3 つのボタンは同じ幅 | `Dialogs/DialogResources.xaml` |
| セットアップのカード | 角 8、フッターの余白 左右 24・上下 16、段の間 12 | 段: 済み text-2 + チェック、いま ink 600、先 text-3 | `Setup/SetupWindow.xaml` |

設定画面のレイアウトは「キャンバスの上に白いカード」にします。`MainWindow.xaml` の内容の `Grid` の背景を canvas にし、左の境界線を消します。

## 5. トレイのメニュー（Win32 のレイヤードウィンドウ）

| 項目 | 現状 | 採用後 |
|---|---|---|
| パネルの幅・角 | 320・8 | 320・8（変えない） |
| 内側の余白 | 7 | 4 |
| 見出し | 高さ 51、アイコン (18,18,20,20)、題「Snapwell」太字 13 | 高さ 56、アイコン 20 を左 16・縦中央、アイコンと題の間 12、題 14 通常。状態のタグとトグル（40 × 20）を右 16 |
| モニター構成の行 | 38、11.5、Muted | 28、caption、text-3、見出しに 8 重ねる（上 −8）、左は題と同じ x |
| エラーの行 | 赤の文字 | negative のアイコン 16 + 文字 desc（2 行まで） |
| 項目 | Rect(7, y, 306, 32)、文字 13、ラベル x+34 | 高さ 32、左右 12、アイコン 16（線 1.5、text-2）とラベルの間 12、文字 body |
| 区切り | 1 px、群の間 9 | 1 px の line、上下 4 |
| ホバー | Surface2・角 4 | raised・角 4 |
| キーボードの選択 | BlueText 1 px・角 4 | accent 2 px・角 4（内側に描く） |
| 状態のタグ | 色付きの背景（緑・黄・赤）、10.5 太字 | 背景 raised、文字 text-2、先頭に 6 px の点（有効 positive・一時停止中 caution・異常 negative） |
| 更新の項目 | 文字だけ | アイコン `update` を accent で、文字は text |
| 影 | 余白 52 | 変えない（`0 8 16` 相当に近づけるなら実機で確認） |
| 出現 | すぐ | surface のばね（不透明度と 4 px の浮き上がり） |
| プロファイルの行 | 実装にはない | 足さない（#25） |

項目のアイコンは 16 × 16 の座標の Path（`kit.js` の `ICONS` と同じ）を、線 1.5・端は丸で描きます。

| 名前 | 項目 | Path |
|---|---|---|
| `arrange` | 現在のモニターを整列 | `M2 2.5 H7 V7 H2 Z M9 2.5 H14 V7 H9 Z M2 9 H7 V13.5 H2 Z M9 9 H14 V13.5 H9 Z` |
| `launcher` | ランチャーを開く | `M2 3 H14 V13 H2 Z M2 6 H14 M6 6 V13` |
| `general` | 設定を開く | `M8 5.5 A2.5 2.5 0 1 1 8 10.5 A2.5 2.5 0 1 1 8 5.5 Z M8 1.5 v2 M8 12.5 v2 M1.5 8 h2 M12.5 8 h2 M3.4 3.4 l1.4 1.4 M11.2 11.2 l1.4 1.4 M3.4 12.6 l1.4 -1.4 M11.2 4.8 l1.4 -1.4` |
| `exit` | 終了（標準の動作に戻す） | `M6 2.5 H3 A0.5 0.5 0 0 0 2.5 3 V13 A0.5 0.5 0 0 0 3 13.5 H6 M10 5 L13 8 L10 11 M13 8 H6` |
| `update` | 新しいバージョンがあります | `M8 2 V10 M4.5 6.5 L8 10 L11.5 6.5 M2.5 13.5 H13.5` |

GDI では線の端が粗いので、`OverlayPainter.StrokeLine` と同じアンチエイリアスの線で描くか、大きさ（16・20・24・32）ごとに 32 bit のビットマップを作ってキャッシュします（実機未確認）。

**更新の吹き出し（`TrayUpdateNotifier` の `NIF_INFO`）は Windows が描くので、見た目は変えられません。**

## 6. ランチャーと区画のプレビュー（Win32）

| 項目 | 現状 | 採用後 |
|---|---|---|
| パネルの幅 | min(560, 作業領域 − 28) | min(560, 作業領域 − 32) |
| 上の位置 | 作業領域の上から 14 | 16 |
| パネル | — | 角 8、枠 line-strong、背景 surface |
| 内側の余白・行の間 | 15・10 | 16・12 |
| 1 行目（対象と分割） | `TopHeight` 27.2、タグ 18.8 | 32、タグ 20（アクセントのタグ） |
| プリセットのボタン | `ButtonHeight` 29.2、幅 42 + ラベル、間 6、図 18 × 12 を (+9, +8.6) | 32、幅 8 + 20 + 8 + ラベル + 12、間 4、図 20 × 12 を (+8, +10)、角 4、文字 desc。図は ink の塗り |
| 使えないプリセット | — | 文字と図を text-3、背景なし |
| キーのヒントの行 | 22 | 24、キーは高さ 20 |
| ウィンドウの一覧 | 行 28 | 行 32（番号は等幅 caption、チェック 20、題は省略記号）。選んだ行は accent-tint。メインは accent の輪 + 点、スタックは text-3 の輪 |
| 整列のボタン | — | 4 等分、高さ 64、図 44 × 28（主の区画は zone-fill + accent の枠） |
| 区画のセル | 角 4、Cell / CellBorder | 角 4（変えない） |
| 選択範囲 | セルごとに BlueBackground で塗り、セルごとに BlueLine の枠（角 4） | 選んだセルをまとめた 1 つの矩形を zone-fill で塗り、内側に accent 1.5 の枠、角 8。中央に大きさの札（等幅 caption、文字 accent、背景 surface、角 4） |
| 区画のプレビューの塗り | ライト不透明 `#E1F3FE`、ダーク `(31,108,159,71)` | ライト `(31,108,159,46)`、ダーク `(140,203,242,41)` |
| 区画の点線 | BlueLine 1.5・4/4・角 8 | accent-line 1.5・4/4・角 8（同じ） |
| 予測の線の終点 | 半径 4 の丸 | **8 × 8・角 2 の四角**（accent） |
| 出現 | すぐ | surface のばね |

選択範囲は、`PaintLauncher` で選択外のセルを今までどおり描いたあと、範囲の外接矩形に `FillRounded(…, 8, scale)` と `StrokeRounded(…, 8, 1.5, scale)`（内側）を重ねます。カーソルのセルの輪は、範囲の面と重ならないように描く順を確かめます。

予測の線の終点は、`OverlayPainter.cs` の 150 行あたり（いまは半径 `radius` の丸を `FillRounded` で描いている）を、`FillRounded(frame, new(endX − 4s, endY − 4s, 8s, 8s), accent, 2, scale)` に変えます（`s = scale`）。

`OverlayPalette` の新しい値（RGBA）:

| フィールド | ライト | ダーク |
|---|---|---|
| Dim | (245,245,243,140) | (26,26,25,128) |
| Cell | (255,255,255,128) | (255,255,255,13) |
| CellBorder | (28,28,27,26) | (255,255,255,26) |
| BlueBackground（zone-fill） | (31,108,159,46) | (140,203,242,41) |
| BlueLine（accent-line） | (31,108,159,153) | (140,203,242,166) |
| BlueText（accent） | (31,108,159,255) | (140,203,242,255) |
| Surface | (255,255,255,255) | (34,34,33,255) |
| Surface2（raised） | (239,239,237,255) | (44,44,43,255) |
| Border（line） | (28,28,27,23) | (255,255,255,20) |
| BorderStrong（line-strong） | (28,28,27,46) | (255,255,255,38) |
| Text | (28,28,27,255) | (237,237,235,255) |
| Muted（text-2） | (82,82,79,255) | (180,180,176,255) |
| Subtle（text-3、新設） | (107,107,103,255) | (146,146,142,255) |
| Ink | (28,28,27,255) | (237,237,235,255) |
| Green/Yellow/RedText（点の色） | (47,122,62) / (138,90,0) / (180,35,24) | (124,196,138) / (224,182,90) / (242,147,140) |
| Green/Yellow/RedBackground | タグでは使わない（raised にする） | 同左 |

## 7. 初回セットアップ（ようこそ）

- `Setup/IntroPalette.cs` のアクセント `#2563EB` / `#6AA8FF` をやめ、accent（`#1F6C9F` / `#8CCBF2`）にします。導入の舞台の区画の塗りと点線も 6 章の値（zone-fill・accent-line）にそろえます。
- ようこその見出しの句点を取ります（1 章の文言の表）。見出しは title（28/36・400）で、ようこそだけ大きくする上書きはしません。
- カードの角 12→8、フッターの余白 20,14→24,16、段の表示の間 8→12、同意の文字は body。

## 8. 実装の順（1 段 1 コミット。各段でビルド・テスト・見た目の確認を通す）

各段の確認は共通で、CLAUDE.md の「確認」（両 RID のビルド、`dotnet test --no-restore`、`dotnet format --verify-no-changes --no-restore`）を通します。見た目は、`docs/mockups/snapwell.html` をブラウザーで開き（`python -m http.server --directory docs/mockups` などで配信）、同じ画面を実機のアプリと並べて見ます。ライト／ダーク、日本語／英語、100% と 150% の拡大率で見ます。

### 段 1. トークンの色（#4・#5・#8 の色の部分）

- 変えるファイル: `src/Snapwell.Settings/App.xaml`（Light・Dark・Default）、`src/Snapwell.Settings/MainWindow.xaml` の `NavigationView*` のブラシ、`src/Snapwell.Core/Overlay/OverlayPainter.cs` の `OverlayPalette`（`Subtle` を足す）、`src/Snapwell.Settings/Setup/IntroPalette.cs`
- 一緒に直すテスト: `tests/Snapwell.Core.Tests/Overlay/OverlayPainterTests.cs`（`PaletteMatchesMockupTokens`、`PreviewUsesThemedFillAndCenteredDashedBorder`）、`tests/Snapwell.Core.Tests/Overlay/LauncherPanelLayoutTests.cs`（`PanelPaletteMatchesMockupTokens`、`PanelPaintsSurfaceBorderAndDisabledOpacity`）、`tests/Snapwell.App.Tests/Tray/TrayMenuHeaderTests.cs`（`IconsAndTagsUseStateColors`）
- 見た目の確認: 投げたときの区画のプレビューの下のウィンドウが透けること。設定画面の内容の下地が canvas になり、カードが白いこと。セットアップの舞台に #2563EB の青が残っていないこと

### 段 2. 文字（#13・#14）

- 変えるファイル: `App.xaml` の文字のスタイル（3.2 の表）、`CharacterSpacing` と `Typography.Capitals` の削除、キーの文字の −2 とナビのアイコンの 2 の補正の測り直し
- 一緒に直すテスト: `tests/Snapwell.Core.Tests/Localization/MainWindowSourceTests.cs`、`tests/Snapwell.App.Tests/Localization/SettingsLocalizationSourceTests.cs`（XAML の文字列を読んでいるもの）
- 見た目の確認: ページの題が游明朝 28 で太くないこと。タグの文字が大文字でないこと。日本語と英語で行の題と説明が切れないこと

### 段 3. 部品の角と寸法（#1・#2・#3・#10・#16・#22・#23）

- 変えるファイル: `App.xaml` の部品のスタイル（4 章の表。ボタン、主ボタン、入力欄、ComboBox、チェック、タグ、キー、行、カード、グループの題、フォーカスの輪、InfoBar）
- 一緒に直すテスト: `MainWindowSourceTests.cs`、`SettingsLocalizationSourceTests.cs`（スタイルの値を読んでいれば）
- 見た目の確認: Tab で回したとき、どの部品にも 2 px の輪が外側 2 px に出て、入力欄に下線が出ないこと。行が 56 より低くならないこと。タグが角 4 の灰色で、意味のあるタグだけ点に色があること

### 段 4. 設定画面の骨組み（#8・#11・#12）

- 変えるファイル: `src/Snapwell.Settings/MainWindow.xaml`（タイトルバー 32、ナビ 240・項目・印 3 × 16、内容の背景 canvas、左の境界線の削除、フッター 64）
- 一緒に直すテスト: `MainWindowSourceTests.cs`
- 見た目の確認: ナビの幅と印の位置（印の縦位置は項目の上から 10）を `docs/mockups/snapwell.html` と並べて見ること。ウィンドウを狭くしたときにナビとフッターが重ならないこと

### 段 5. 各ページ・ダイアログ・セットアップ（#15・#21 と、ページのローカルのスタイル）

- 変えるファイル: `src/Snapwell.Settings/Pages/*.xaml`（`FeelPage` のチップとカード、`HotkeysPage` の行とキャプチャ、`LauncherPage` のプリセットの選択肢、`LookPage` の角のカード、`GridPage` のプレビューと分割の候補、`RulesPage` の行）、`Dialogs/DialogResources.xaml`、`Dialogs/WindowPickerDialog.xaml`、`Controls/OsHowDiagram.xaml`、`Setup/SetupWindow.xaml`、`Setup/SetupWindow.Pages.cs`、文言（1 章の表の `setup/ja.json`・`setup/en.json`、`look/ja.json`・`en.json`・`zh-Hans.json`・`zh-Hant.json`）
- 一緒に直すテスト: `tests/Snapwell.Core.Tests/Localization/WelcomeTextTests.cs`（40〜41 行目が「ウィンドウを投げよう。」と "Throw a window." を固定している）、`tests/Snapwell.Core.Tests/Localization/SetupWindowSourceTests.cs`、`SettingsLocalizationSourceTests.cs`
- 見た目の確認: 閉じるときのダイアログの 3 つのボタンが同じ幅であること。セットアップの 4 段で、カードの角 8・フッターの余白・段の表示がモックアップと同じこと。ようこその見出しに句点がないこと

### 段 6. トレイ（#7・#16 のトレイの部分・#17・#25）

- 変えるファイル: `src/Snapwell.Core/Tray/TrayMenuLayout.cs`、`src/Snapwell.App/Tray/TrayMenuRenderer.cs`（5 章の寸法、アイコン、タグ、キーボードの選択の輪、更新の項目のアイコン）
- 一緒に直すテスト: `tests/Snapwell.Core.Tests/Tray/TrayMenuLayoutTests.cs`、`tests/Snapwell.Core.Tests/Tray/TrayMenuIconDrawingTests.cs`、`tests/Snapwell.App.Tests/Tray/TrayMenuHeaderTests.cs`（`MeasuredTagsFit`、`IconsAndTagsUseStateColors`）、`tests/Snapwell.App.Tests/Tray/TrayMenuToggleAnimationTests.cs`、`tests/Snapwell.App.Tests/Localization/TrayStatusRenderingTests.cs`、`tests/Snapwell.App.Tests/Overlay/GdiOverlayTextTests.cs`
- 見た目の確認: 有効・一時停止中・異常の 3 状態 × 更新の有無で、メニューの高さ（更新なしで、枠の内側が 230。寸法図の H26 と同じ。モックアップの DOM では上下の 1 px の枠を含めて約 231 に見える）、見出し 56、項目 32、項目が 4 つ（更新ありで 5 つ）であること。矢印キーで選んだ項目に 2 px の輪が内側に出ること。アイコンの線が 100%・150%・200% で欠けないこと

### 段 7. ランチャーと区画のプレビュー（#6・#9・#18・#19・#20）

- 変えるファイル: `src/Snapwell.Core/Overlay/LauncherPanelLayout.cs`、`src/Snapwell.Core/Overlay/LauncherWindowListLayout.cs`、`src/Snapwell.Core/Overlay/OverlayPainter.cs`（`PaintLauncher` の選択範囲、予測の線の終点）
- 一緒に直すテスト: `tests/Snapwell.Core.Tests/Overlay/LauncherPanelLayoutTests.cs`（`PanelMatchesCssDimensions`、`HitTests`）、`tests/Snapwell.Core.Tests/Overlay/LauncherWindowListLayoutTests.cs`（`NormalAreasPreserveOriginalCoordinates`、`ArrangementIconsUseMockupCoordinates`、行の役割の矩形）、`tests/Snapwell.Core.Tests/Overlay/LauncherMeasuredLayoutTests.cs`、`tests/Snapwell.Core.Tests/Overlay/LauncherPresetIconTests.cs`、`tests/Snapwell.Core.Tests/Overlay/OverlayPainterTests.cs`（`LauncherPaintsIndividualCellsAndOnlyCursorHasInsetRing` は「範囲を 1 つの面で塗る」に書き直す）、`tests/Snapwell.App.Tests/Overlay/LauncherControllerTests.cs`（`DisabledPresetAndPanelPadding`）、`tests/Snapwell.App.Tests/Overlay/ZonePreviewTests.cs`
- 見た目の確認: ランチャーで 2 × 2 などを選んだとき、範囲が 1 つの角 8 の面になり、中央に大きさの札が出ること。投げたときの予測の線の終点が 8 px の四角であること。作業領域が狭いモニターでパネルが左右 16 ずつあくこと

### 段 8. 動き（#11 の印のばね・#24）

- 変えるファイル: `src/Snapwell.Settings/MainWindow.xaml.cs`（ナビの印を select のばねで `TranslateTransform.Y` を動かす。`SpringToggle` と同じ積分を共通化する）、`src/Snapwell.App/Overlay/*` と `src/Snapwell.App/Tray/*` のランチャーとトレイの窓（surface のばねで `UpdateLayeredWindow` の `SourceConstantAlpha` と y を毎フレーム更新）
- 一緒に直すテスト: `tests/Snapwell.App.Tests/Tray/TrayMenuToggleAnimationTests.cs`（同じ積分の共通化で影響があれば）。ばねの式は Core に置き、単体テストを足す
- 見た目の確認: ナビの印が行き過ぎずに止まること。メニューとランチャーが 4 px 下から浮いて出ること。「視覚効果のアニメーション」をオフにすると、どちらもすぐ出ること

### 段 9. ドキュメントの更新

- 変えるファイル: `docs/DESIGN.md`、`docs/MANUAL_TEST.md`（下の表）。`docs/mockups/snapwell.html` は更新済み
- 見た目の確認: なし

| ファイル | 場所 | 直すこと |
|---|---|---|
| `docs/DESIGN.md` | §9 ランチャーの一覧（403 行あたり） | 「青の輪と点」「灰色の輪」は意味はそのまま。色の名前を accent / text-3 に合わせるかを決める |
| `docs/DESIGN.md` | §12 見た目の設定（594〜596 行あたり） | 予測の線の終点の形（8 px・角 2 の四角）を書く |
| `docs/DESIGN.md` | §17（697 行あたり） | 「新しい版 x.y.z があります」を、UI の文言（バージョン）に合わせる |
| `docs/MANUAL_TEST.md` | 195〜196 行あたり | 設定画面の色とナビの値を新しい値に |
| `docs/MANUAL_TEST.md` | 198・246・308・500・520〜521 行あたり | 「赤い／緑の／黄色のタグ」を「中立のタグに赤・緑・黄の点」に |
| `docs/MANUAL_TEST.md` | 202 行あたり | ランチャーの選択範囲を「1 つの面（角 8）と大きさの札」に |
| `docs/MANUAL_TEST.md` | 205 行あたり | 「対象」のタグをアクセントのタグに |
| `docs/MANUAL_TEST.md` | 236 行あたり | キーの表示を高さ 24・下の線 2 px に |
| `docs/MANUAL_TEST.md` | 310 行あたり | 間の 10 を 12 に |
| `docs/MANUAL_TEST.md` | 475 行あたり（3 か所） | 「終点の丸」を「終点の四角」に |
| `docs/MANUAL_TEST.md` | 末尾 | 実機未確認の項目を足す: DPI 100/150/200% の線とアイコン、明暗の切り替え、ハイコントラスト、視覚効果オフ、トレイの 2 px の輪 |

## 9. 実装で気を付けること

### WinUI 3

- **NavigationView**: 選択の印の高さ・角・ばねでの移動は、`MainWindow.xaml` が持っている `NavigationViewItemPresenter` のテンプレートで変えます。標準の印の移動のアニメーションは使わず、自前のばねで `TranslateTransform.Y` を動かします（止め方は実機未確認）。
- **フォーカスの輪**: `UseSystemFocusVisuals` を使い、`FocusVisualMargin` などの共通の値をスタイルで入れます。ナビの項目の輪は、テンプレートの `KeyboardFocusOutline` を同じ値にします。
- **ComboBox・TextBox の下線**: `TextControlBorderThemeThicknessFocused` を `1` に、`TextControlBorderBrushFocused` を line-strong にして、輪だけにします。
- **ContentDialog のボタン**: 同じ幅にするには、テンプレートの `CommandSpace` の列を `*` にそろえます。
- **Slider**: トラックとつまみの色だけ変えます（ink と line-strong）。寸法は標準のまま。
- **InfoBar**: 背景とアイコンの色は `InfoBarErrorSeverityBackgroundBrush` などのキーで上書きします。
- **ハイコントラスト**: `HighContrast` の辞書は足さず、システムの色に任せます（いまと同じ）。

### Win32 のオーバーレイ

- 区画の塗りが半透明になるので、premultiplied の合成で下のデスクトップが透けます。プレビューの窓の再描画は、いまと同じく位置が変わったときだけで足ります。
- 出現のばねは、`UpdateLayeredWindow` の `BLENDFUNCTION.SourceConstantAlpha` と窓の y を毎フレーム更新します。ビットマップを作り直す必要はありません。描くのは UI のスレッドのタイマーで、フックのコールバックの中では描きません。
- 拡大率が変わったら（`WM_DPICHANGED`）、すべての寸法を `scale` から作り直します。1.5 px の線は `Math.Max(1, 1.5 * scale)` のまま。

## 10. 確かめたこと・確かめていないこと

- 確かめた（ブラウザー、DOM の計測。`docs/mockups/snapwell.html`）:
  - 幅 1440: ナビ 240・項目 36・印の位置、タイトルバー 32、ページの題 28/36、行 56 以上、プリセット 32、タグ 20、キーのヒント 20、一覧の行 32、トレイ 320 × 230（枠の内側。見出し 56・メタ 28・項目 32 が 4 つ）、ランチャーの選択範囲が選んだセルの和と一致すること、ようこその見出し（日英）
  - 幅 390: 5 つの画面が横にはみ出さないこと（ライト・ダーク・OS の設定）
  - 導入の青 `rgb(37,99,235)`・`rgb(106,168,255)` がどの要素にも残っていないこと。コンソールのエラーがないこと
- 確かめていない: WinUI 3 と Win32 での実際の見た目、GDI の文字の太さと位置、DPI ごとの線の太さ、ハイコントラスト、視覚効果オフのときの動き。コントラスト比は計算値です
- モックアップで満たしていないもの: 整列のボタンは、キーの表示（Alt+n）を残したので高さ約 89 です（採用後の値は 64。実装では 64 にし、キーの表示を入れるなら図と並べる）。トレイの更新の項目は、モックアップにないのでアクセントのアイコンを見せていません
- 出現のばね（#24）: モックアップでは、トレイとランチャーの画面を開いたときに `appear()` で動かしています。式の値（単調に 0→1、約 0.33 秒で止まる）はフレームを手で進めて確かめました。ブラウザーのペインが裏にあり、実際のフレームでの動きは目で見ていません
