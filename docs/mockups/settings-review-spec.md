# 設定画面 レビュー: Codex 向けの数値仕様

`settings-review.html` の JSON から生成。単位は論理 px (DIP)。対象は `src/Snapwell.Settings/` の `App.xaml`・`MainWindow.xaml`・`Pages/*.xaml` (と、コードで作るボタン)、文言は `src/Snapwell.Core/Localization/Strings/` の ja / en / zh-Hans / zh-Hant。

## 判断してほしい点

- **D1. 文字の段を 11 / 12 / 13 / 14 / 28 にそろえるか (W1〜W5, W8, W9)** 推奨: そろえる (推奨)。説明・ヒント・グループ見出し・保存バーの文を 12.5 から 13 へ、タグを 10.5 から 11 へ、ComboBox とチップを 14 から 13 へ。行の見出しとリード・ナビは 14 のまま。説明が 0.5 大きくなるので、一部の長い説明が 1 行増える。導入の画面 (同じスタイルを使う) にも及ぶ。
- **D2. ページの題名をナビの名前にそろえるか (W13)** 推奨: そろえる (推奨)。題名を短いほうに寄せる: 「アプリのルール」「標準機能」(英語は「App rules」「Windows features」)。リードの 1 文目に「Windows の」が入っているので、意味は落ちない。ナビを長いほうに寄せると 232 の幅に収まらない (英語は特に)。
- **D3. 入れ物 12 / 操作部品 8 にそろえるか (W6, W7)** 推奨: そろえる (推奨)。行の入れ物とプリセットのカード・外観の見本は 12、ボタン・ComboBox・チップ・ナビの項目は 8、キーは 4 のまま。導入・トレイ・ランチャーと同じ。ボタンの角が 6 から 8 に丸くなる程度の変化。
- **D4. ナビの項目に 4 の間隔を入れるか (W11)** 推奨: 入れる (推奨)。項目 36・間隔 4 (送り 40) で、9 項目が 36 から 76 高くなる (ウィンドウの既定の高さ 780 には収まる)。選択の面が隣に接しなくなる。キーボードの枠は項目の外へ描く作りなので、隣と重ならないことを実機で確かめる。
- **D5. Expander を入れ物の見た目にそろえるか (W10)** 推奨: そろえる (推奨)。「試す」と「最初から対象外にしているもの」を、行の入れ物と同じ枠つきの面にする。開閉の動きと中身は変えない。WinUI の Expander のテンプレートを一部置き換えるので、起動して開閉を確かめる。
- **D6. プリセットの注記を 2 行に分けるか (W14)** 推奨: 分ける (推奨)。「落ち着くまで 0.40 秒」と「行き過ぎ 4.6%」を改行で分け、区切りの「 · 」をなくす。翻訳 (4 言語) の差し替えを伴う。

## 監査

- **U1 (P1) ページの名前がナビとずれる** 日本語: ナビ「アプリのルール」に対し題名「アプリごとのルール」、ナビ「標準機能」に対し題名「Windows の標準機能」。英語: ナビ「Windows features」に対し題名「Built-in Windows features」。同じ場所を二つの名前で呼んでいる。中国語 2 言語も同じ対応を確かめる。 (settings/*.json Nav_*・*_Title)
- **U2 (P1) 文字の大きさが半端** 本文は 12.5 (説明・ヒント・グループ見出し・保存バーの文) と 14 (行の見出し・リード・ナビ)、タグは 10.5 (11 未満) で字間 +0.05em、キー表示は 11.5、操作部品は 13 と 14 が混在。12 / 12.5 / 13 / 14 が近い値で並び、段の差が読み取れない。導入のレビューで「設定画面で決める」とした項目。 (App.xaml 各 Style、ComboBox / チップ)
- **U3 (P1) 角の半径が 5 種類** 行の入れ物 8、プリセットのカード・外観の見本 10、ボタン・ComboBox 6、ナビ 6 (選択の枠 7)、キー 4。トレイ・ランチャー・導入で決めた「入れ物 12 / 操作部品 8」と、設定画面だけがずれている。 (App.xaml、MainWindow.xaml、Pages/*.xaml)
- **U4 (P1) 寸法が 4 の格子に乗っていない** 行の見出しの行高 22.4 (行の高さ 66.4)、ページの上余白 28、題名の行高 41.6、リードの行高 22.4 / 下 20、グループ見出しの上 22、保存バーの縦の余白 17.67、ナビの上余白 10、フッターの余白 14。 (App.xaml、MainWindow.xaml)
- **U5 (P1) 同じ「まとまり」が二通りに描かれる** 行を束ねる入れ物は「Surface + 枠 1 + 角 8」なのに、操作感の「試す」とルールの「最初から対象外にしているもの」(Expander) は「Surface2 の塗り・枠なし・太字」で、入れ物の隣に別の部品として出る。 (Pages/FeelPage.xaml:156、RulesPage.xaml:58)
- **U6 (P2) ナビの項目に間隔がない** 項目 36 が隙間なしで 9 個並ぶ。選択の面と枠が上下の項目に接して見える (推奨は 項目 32〜36、間隔 2〜4)。 (MainWindow.xaml:64)
- **U7 (P2) 操作感のプリセットの注記が半端に折れる** 「落ち着くまで 0.40 秒 · 行き過ぎ 4.6%」が、カード幅で「行き過ぎ / 4.6%」と区切りの悪いところで折り返す (実機 4 枚とも)。 (Feel の preset 注記の文言 / FeelPage.xaml:70)
- **U8 (P2) 操作部品の高さがばらつく** チップ (区画案・ランチャー) 約 29、ボタン 28、ComboBox 約 32、数値ボックス 約 32 が同じ行に並ぶ。 (SnapwellButtonStyle ほか)

## 数値仕様 (as is → to be)

| ID | 項目 | as is | to be | 指摘 |
|---|---|---|---|---|
| W1 | ページの題名 (SnapwellPageTitleStyle) | FontSize 26、LineHeight 41.6、Margin 0,0,0,4 | FontSize 28、LineHeight 32、Margin 0,0,0,8 (導入の SetupHeadingStyle と同じ)。書体 Yu Mincho・Medium・CharacterSpacing -20・色は変えない | U2, U4 |
| W2 | リード (SnapwellLeadTextStyle) | FontSize 14、LineHeight 22.4、Margin 0,0,0,20 | FontSize 14、LineHeight 24、Margin 0,0,0,24 | U4 |
| W3 | ページの余白 (各ページの最上位 StackPanel / Grid) | Padding 32,28,32,40 (9 ページ) | Padding 32,32,32,40 | U4 |
| W4 | 行の文字と余白 | SnapwellRowTitleStyle: 14 / LineHeight 22.4。SnapwellRowDescriptionStyle・SnapwellHintTextStyle: 12.5 / 20。行 Padding 16,12 (GeneralPage の更新の 3 行だけ ColumnSpacing 20) | 見出し 14 / LineHeight 20。説明・ヒント 13 / LineHeight 20。行 Padding 16,12、ColumnSpacing 16 (20 の行も 16 に)。行の高さは 1 行 44、見出し + 説明 1 行で 64 | U2, U4 |
| W5 | グループ見出し (SnapwellGroupTitleStyle) | FontSize 12.5、SemiBold、LineHeight 20、Margin 0,22,0,8 | FontSize 13、SemiBold のまま、LineHeight 20、Margin 0,24,0,8。操作感の「詳細 + プリセット名」の行 (FeelPage.xaml:261 の Margin 0,22,0,8) も同じ | U2, U4 |
| W6 | 入れ物の角 | SnapwellRowsBorderStyle 8。プリセットのカード (操作感 4 枚、外観 4 枚)・外観の見本 (400×190) 10 | すべて 12 (枠 1 は同じ)。ほかの入れ物 (ダイアログの面など 8〜10 のもの) もここに合わせる | U3 |
| W7 | 操作部品の角 | SnapwellActionButtonStyle・SnapwellPrimaryButtonStyle・SnapwellButtonStyle・ComboBox (各ページの DemoSelectStyle 相当) 6。コードで作るボタン (HotkeysPage.xaml.cs:229、RulesPage.xaml.cs:161、高さ 28) も 6 | すべて 8。キー表示 (SnapwellKbdBorderStyle) 4、タグ (Pill) カプセル、ばねのトグル 10 は変えない | U3 |
| W8 | ComboBox・チップ・数値ボックスの文字と高さ | ComboBox 14 (高さ 約 32)。SnapwellButtonStyle (チップ・区画案) 14、Padding 14,7 (高さ 約 29) | ComboBox・数値ボックス FontSize 13、MinHeight 32。チップ FontSize 13、Padding 14,0、MinHeight 32 (選択は 2 px の墨の枠のまま、内側の余白で寸法を保つ) | U2, U8 |
| W9 | タグ (SnapwellTagTextStyle・SnapwellTagStyle) | FontSize 10.5、SemiBold、CharacterSpacing 50、LineHeight 16.8。Padding 8,1 | FontSize 11、SemiBold、CharacterSpacing 0、LineHeight 16。Padding 8,2 (高さ 20)。色と Pill (カプセル) は変えない。SnapwellTagConflictTextStyle (編集中の測り方: LineHeight 0・NoWrap) は変えない | U2 |
| W10 | Expander (操作感「試す」、ルール「最初から対象外にしているもの」) | Surface2 の塗り、枠なし、角 8 (または標準)、見出しは太字 13、高さ 約 45 | Surface の塗り + 枠 1 (SnapwellBorderBrush) + 角 12。見出しは SnapwellRowTitleStyle (14 / LineHeight 20・標準の太さ・墨)、Header の Padding 16,12 で高さ 44。開いた本文との間に 1 px の区切り線。シェブロンは 16、補助文字の色。開閉の動作と中身は変えない | U5 |
| W11 | ナビ (MainWindow.xaml) | 項目の LayoutRoot Margin 10,0、CornerRadius 6、SelectedOutline の角 7。PaneHeader 10。PaneFooter の Padding 10,14,10,4、StackPanel Spacing 6 | LayoutRoot Margin 10,2 (項目 36・間隔 4)、CornerRadius 8、SelectedOutline の角 9 (Margin -1 のまま)。PaneHeader 12。PaneFooter の Padding 10,16,10,8、Spacing 8。項目の文字 14・アイコン 16・アイコンと文字の間 10・選択の 3 px の墨の帯は変えない。キーボードの枠 (KeyboardFocusOutline) の寸法は変えず、隣と重ならないことを実機で確かめる | U3, U4, U6 |
| W12 | 保存バー (MainWindow.xaml 下) | Padding 32,17.67、文は SnapwellHintTextStyle (12.5) | Padding 32,16 (ボタン 28 で高さ 60 + 枠 1)。文は 13 (W4 に従う) | U4 |
| W13 | ページの題名の文言 | ja: Rules_Title「アプリごとのルール」、Os_Title「Windows の標準機能」。en: Os_Title「Built-in Windows features」 | ja: Rules_Title「アプリのルール」、Os_Title「標準機能」。en: Os_Title「Windows features」(Rules は「App rules」で一致済み)。zh-Hans・zh-Hant も、ナビと題名が一致するかを確かめ、ずれていれば短いほうにそろえる。ナビの文言は変えない | U1 |
| W14 | 操作感のプリセットの注記 (Feel_PresetSummary 相当) | 「落ち着くまで 0.40 秒 · 行き過ぎ 4.6%」(1 つの文で折り返し任せ) | 「落ち着くまで 0.40 秒」と「行き過ぎ 4.6%」を 2 行 (改行) に分ける。区切りの「 · 」をなくす。12 / LineHeight 16・補助文字のまま。4 言語とも差し替え、プレースホルダーの一致を保つ | U7 |
| W15 | 変えないもの | — | ウィンドウの大きさと最小・固定サイズの扱い、ページの構成と順序、色のトークン (ライト・ダーク)、ばねのトグル (40×20)、キー表示、操作感の曲線の絵、区画のプレビュー、フォーカスの見た目、保存の挙動と設定の値、ダイアログの構成 | — |

表にない値の扱い: 4 の倍数 (2 刻みの微調整は可、中心をそろえるための奇数は残してよい) に最も近い値へ。ただし W15 の「変えないもの」と、実機で中心をそろえて決めた補正 (キー表示・アイコンの Margin など) は触らない。

## 受け入れ条件

- SnapwellPageTitleStyle が 28 / LineHeight 32 / Margin 0,0,0,8、SnapwellLeadTextStyle が LineHeight 24 / Margin 0,0,0,24、9 ページの Padding が 32,32,32,40。
- SnapwellRowTitleStyle の LineHeight 20、SnapwellRowDescriptionStyle・SnapwellHintTextStyle・SnapwellGroupTitleStyle の FontSize 13 (グループ見出しは Margin 0,24,0,8)。GeneralPage の ColumnSpacing 20 が 16 になっている。
- 行の入れ物・プリセットのカード・外観の見本の角が 12、ボタン (Action / Primary / Button)・ComboBox・チップ・コードで作るボタンの角が 8。キー表示 4・ばねのトグル 10 は変わらない。
- ComboBox・数値ボックス・チップが FontSize 13・MinHeight 32。選択中のチップの枠が 2 px のまま、寸法が変わらない。
- タグが FontSize 11・CharacterSpacing 0・LineHeight 16・Padding 8,2 (高さ 20)。標準機能・ホットキー・導入の「使えます」「重複」「停止中」などの表示が崩れない。
- Expander の 2 か所が、入れ物と同じ枠つきの面 (角 12) で、見出しが行の見出しと同じ文字。開閉と中身は変わらない。
- ナビの項目が間隔 4 (送り 40)・角 8、選択の枠の角 9。キーボードで Tab / 矢印を操作したとき、フォーカスの枠が隣の項目と重ならない (実機)。
- 保存バーの Padding が 32,16。ページの題名が ja・en・zh-Hans・zh-Hant ともナビと同じ名前 (W13)。ja / en / zh の文字列の対応 (キーの過不足なし) を保つ。
- 操作感のプリセットの注記が 2 行 (落ち着くまで / 行き過ぎ) で、折り返しの途中で切れない。4 言語。
- XAML を変えたため、Snapwell.Settings.exe を実際に起動し、9 ページ (一般・分割と間隔・ホットキー・操作感・アプリのルール・標準機能・同期とバックアップ・ランチャー・外観) すべてが開くこと、ライトとダークの両方で文字が読めること、「試す」「最初から対象外にしているもの」が開閉できることを確かめる。確認中は保存する値を変えない (保存ボタンは押さない)。ビルドとテストでは XAML の読み込みの失敗が出ない。
- ビルドは win-x64 / win-arm64 の両方 (Snapwell.Settings も)。dotnet test と dotnet format が通る。上の表にない off-grid の値を 4 の倍数に直した場合は、その一覧と理由を PR に書く。
