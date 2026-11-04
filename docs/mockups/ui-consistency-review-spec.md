# 全 UI の再レビュー (ダイアログ込み): Codex 向けの数値仕様

`ui-consistency-review.html` の JSON から生成。単位は論理 px (DIP)。対象は `src/Snapwell.Settings/Dialogs/` (RuleDialog・WindowPickerDialog・ImportDialog・DialogButtons) と、コードで作るダイアログ (MainWindow の保存せず閉じる確認、OsPage の停止の同意)。

## 判断してほしい点

- **D1. ダイアログの面の角は 8 のままにして、中の部品をそろえるか (W1, W3)** 推奨: そろえる (推奨)。浮かぶ面 (トレイ・ランチャー・オーバーレイ・ダイアログ) は 8 で統一済み。ダイアログだけ 12 にすると、トレイ・ランチャーも変えることになり、すでにマージしたレビューの結論を動かす。そこで、面は 8 のまま、中の入れ物 (ウィンドウ一覧・取り込みの一覧) を 8 に下げる。「内側は外側以下」が守られる。
- **D2. ダイアログのボタンと入力欄を、ページの部品 (角 8・高さ・文字 13) にそろえるか (W2, W4)** 推奨: そろえる (推奨)。ボタンは角 6 → 8。入力欄 (TextBox・ComboBox) は角 4 → 8、文字 14 → 13、高さ 32。ラベル (Header) も 14 → 13。ページとダイアログで同じ部品が同じ形になる。TextBox・ComboBox の Style を足す作りになり、フォーカス時の下線と墨の色は変えない。
- **D3. ダイアログの文字を段 (16 / 14 / 13) にそろえるか (W5)** 推奨: そろえる (推奨)。タイトルを 20 から 16 SemiBold (LineHeight 24) へ。本文 (確認の文) は 14、リードとヒントは 13 (補助文字の色)。ページのタイトル (28)・行の見出し (14)・説明 (13) と同じ序列になる。タイトルが 4 小さくなるので、Windows 標準のダイアログより控えめになる。20 のまま残す案 (段に 20 を足す) もあるが、段が増える。
- **D4. 取り込みダイアログに Snapwell の色を当てるか (W6)** 推奨: 当てる (推奨)。`DialogResources.xaml` を結合して、チェックの水色を墨にする。補助文字は `SnapwellMutedBrush`、項目名は 14 (標準の太さ)、差分の文は 13。見出しの太字 (SemiBold) は外し、行の見出しと同じにする。
- **D5. ルールを追加の欄の間隔を 16 にするか (W7)** 推奨: する (推奨)。欄 (ラベル + 入力) どうしを 8 から 16 に。ラベルと入力の間は 4 のまま。ダイアログが 24 ほど高くなる (800 の既定の高さには収まる)。
- **D6. Win32 の MessageBox (標準スナップの停止の同意など) を Snapwell の見た目にするか (X7)** 推奨: 今回は変えない (推奨)。同意は『Snapwell.exe 本体から、設定画面を開かずに出る』ため、設定画面のダイアログ (WinUI) へ移すと、本体の起動方法と同意の流れの設計変更になる。見た目だけの理由では割に合わない。別の課題として記録する。代案: 設定画面から出る同意 (OsPage のダイアログ) と本体の MessageBox の文言をそろえるだけにする。
- **D7. InfoBar の色を Snapwell の控えめな色にするか (X8)** 推奨: 今回は変えない (推奨)。実機で出して確かめられていない (操作のあとにだけ出る)。色を変える Style は WinUI の InfoBar の内部テンプレートに触れるため、Expander のときと同じ手間がかかる。次の版で、実機で見てから決める。

## 全 UI の値の比較 (as is)

| 項目 | トレイ | ランチャー | オーバーレイ・プレビュー | ようこそ・導入 | 設定画面 | ダイアログ |
|---|---|---|---|---|---|---|
| 浮かぶ面の角 | 8 | 8 (パネル) | 8 (セル・プレビュー) | — | — | 8 (CornerRadius) |
| 入れ物の角 | — | — | — | 12 (カード) | 12 | 12 (ウィンドウ一覧)  ←ずれ: 面は 8 なのに中が 12 |
| 操作部品の角 | — | 8 (行) | 8 (選択ラベル) | 8 | 8 (ボタン・ComboBox) | ボタン 6、入力欄 4  ←ずれ |
| 補助文字の色 | #6B6A67 / #9B9A97 | 同じ | 同じ | 同じ | 同じ | 取り込み: システム色 (#605E5C / #C5C5C5)  ←ずれ。ほかは同じ |
| 強調色 (チェック・フォーカス) | BlueText の 2 px 枠 | 同じ | 同じ | 墨 | 墨 | 取り込み: Windows の水色  ←ずれ (DialogResources を結合していない) |
| タイトル / 本文の文字 | — | 12・13 | 12 | 28 / 14・13・12 | 28 / 14・13・12・11 | 20 SemiBold / 14・13  ←ずれ (20 は段にない) |
| ボタンの高さ | — | — | — | 28 | 28 (操作部品) | 28 (同じ) |
| 4 の格子 | 〇 | 〇 | 〇 | 〇 | 〇 | 一部 (項目の間 8 と 4、入力欄の間隔) |

## 監査

- **X1 (P1) ダイアログのボタンの角が 6** 設定画面のボタン (Action / Primary) は PR 154 で角 8 になったが、ダイアログのボタンは `DialogButtons.Compact` が 6 を直接入れている。同じ「ボタン」が画面で角が違う。 (DialogButtons.cs:36)
- **X2 (P1) 入力欄・ComboBox の角が 4 で、ページの部品と違う** ページの ComboBox・数値ボックスは角 8・高さ 32・文字 13 だが、ダイアログの `TextBox` と `ComboBox` は WinUI の既定 (角 4・文字 14)。同じ ComboBox が、ページでは丸く、ダイアログでは角ばって見える。 (RuleDialog.xaml)
- **X3 (P1) ダイアログの中の入れ物のほうが、面より丸い** ダイアログの面は角 8、中の「ウィンドウ一覧」は設定画面の入れ物の型 (角 12) のまま。内側の角が外側より大きく、角が合わない。 (WindowPickerDialog.xaml)
- **X4 (P1) 取り込みダイアログだけ水色と別の補助文字** `ImportDialog` は `DialogResources.xaml` を結合しておらず、チェックボックスが Windows の水色になる。補助文字も Snapwell の色ではなくシステム色 (`TextFillColorSecondaryBrush`)。ほかのダイアログは墨色。 (ImportDialog.xaml)
- **X5 (P1) ダイアログの文字に、段にない大きさがある** タイトルは WinUI 既定の 20 SemiBold。段は 11 / 12 / 13 / 14 / 28 にそろえたので、20 だけ外れる。入力欄のラベルと本文は 14、リードは 13 と混在。 (各ダイアログ (既定の ContentDialog))
- **X6 (P2) ルールを追加の入力欄が詰まっている** 3 つの入力欄 (ラベル + 入力) が 8 の間隔で縦に並び、欄の区切りが分かりにくい (ラベルと入力は 4 で近いほうがよい)。欄どうしを 16 にして、ラベルとの距離で組を示す。 (RuleDialog.xaml (Spacing 8))
- **X7 (P2) 標準スナップの停止の確認が、OS の MessageBox** Snapwell.exe 側から出る「Windows 標準のスナップを停止しますか」(同意) と、設定画面を起動できなかったときの通知は、Win32 の `MessageBox`。テーマ・書体・ボタンの形が Snapwell と別の見た目になる。ただし、同意の取り方と復元の約束は変えられないので、見た目の統一は今回の対象にしない (判断 D6)。 (WindowsServices.cs:89、SettingsLauncher.cs:87)
- **X8 (P2) InfoBar が既定の色** 保存結果・取り込み結果・エラーを出す `InfoBar` は、WinUI の既定の色 (情報は水色)。タグの緑・赤・黄 (Snapwell の控えめな色) と別の系統。画面に出るのは操作のあとだけなので、実機では撮れていない。 (MainWindow.xaml:333、BackupPage.xaml:14、GeneralPage.xaml:29、OsPage.xaml:12、SetupWindow.xaml:91)

## 数値仕様 (as is → to be)

| ID | 項目 | as is | to be | 指摘 |
|---|---|---|---|---|
| W1 | ダイアログの面 | CornerRadius 8 (RuleDialog・WindowPickerDialog。ImportDialog・確認は Compact が 8) | 8 のまま。DialogButtons.Compact に残す。値は変えない | X3 |
| W2 | ダイアログのボタン (DialogButtons.Compact) | CornerRadius 6、高さ 28、最小幅 72、文字 13、Padding 14,0,14,2 | CornerRadius 8。高さ 28・最小幅 72・文字 13・Padding は変えない (SnapwellActionButtonStyle の W7 と同じ) | X1 |
| W3 | ダイアログの中の入れ物 (ウィンドウ一覧、取り込みの一覧) | ウィンドウ一覧: SnapwellRowsBorderStyle (12)。取り込み: ListView の既定 | 8。ウィンドウ一覧は、そのダイアログの中だけ CornerRadius 8 にする (SnapwellRowsBorderStyle は変えない)。行の Padding 16,12 は同じ | X3 |
| W4 | 入力欄 (TextBox・ComboBox) | TextBox・ComboBox は WinUI の既定: 角 4、文字 14、Header 14。数値は等幅 (プロセス名など) | 角 8、FontSize 13、MinHeight 32、Header の FontSize 13 (ink)。TextBox の下線 (2 px の補助文字色) とフォーカスの墨は変えない。RuleDialog.xaml に Style を置く (App.xaml には足さない: 設定画面の ComboBox は DemoSelectStyle で足りている) | X2 |
| W5 | ダイアログの文字 | タイトル 20 SemiBold (LineHeight 28)、本文 14、リード 13 (SnapwellHintTextStyle)、取り込みのリード 14 | タイトル 16 SemiBold / LineHeight 24 (ContentDialog の TitleTemplate か Title の TextBlock で指定)。確認の文 (Content) 14 / LineHeight 20 (ink)。リード・ヒント 13 / LineHeight 20 (SnapwellMutedBrush)。取り込みのリードも 13 の補助文字 | X5 |
| W6 | 取り込みダイアログ (ImportDialog) | DialogResources 未結合 (チェックが水色)。補助文字はシステム色。項目名 14 SemiBold、差分 14 | ImportDialog.xaml に DialogResources.xaml を結合 (RuleDialog と同じ書き方)。差分の文は 13・SnapwellMutedBrush。項目名は 14・標準の太さ (SnapwellRowTitleStyle)。チェックは墨。保存のヒントも 13・SnapwellMutedBrush | X4 |
| W7 | ルールを追加の欄の間隔 (RuleDialog) | StackPanel Spacing 8 (ラベル + 入力の 3 組が 8 おき) | 欄 (ラベル + 入力) を StackPanel Spacing 4 で組にし、組どうしは Spacing 16。リード → 最初の欄は 16。エラーの文の MinHeight 40 は変えない | X6 |
| W8 | 変えないもの | — | ダイアログの構成・ボタンの数と並び・既定のボタン・押したときの動作、墨の主ボタン、副ボタンの枠 (BorderStrong)、DialogResources.xaml の色、Win32 の MessageBox (D6)、InfoBar (D7)、タイトルの文言、各ダイアログの幅 (440) | — |

表にない値の扱い: 4 の倍数 (2 刻みの微調整は可、中心をそろえるための奇数は残してよい) に最も近い値へ。ただし W8 の「変えないもの」は触らない。

## 受け入れ条件

- DialogButtons.Compact のボタンの角が 8 (高さ 28・最小幅 72・文字 13・Padding は変更なし)。
- RuleDialog の TextBox・ComboBox が角 8・FontSize 13・MinHeight 32、Header が 13。フォーカスの墨と TextBox の下線は変わらない。
- WindowPickerDialog のウィンドウ一覧の角が 8 (SnapwellRowsBorderStyle 自体は 12 のまま、設定画面の入れ物は変わらない)。
- ImportDialog が DialogResources.xaml を結合し、チェックボックスが墨 (水色でない)。差分の文・ヒントが SnapwellMutedBrush の 13、項目名が 14 の標準の太さ。
- ダイアログのタイトルが 16 SemiBold / LineHeight 24。確認の文 14、リード・ヒント 13。ja / en / zh-Hans / zh-Hant の文字列は変えない (見た目だけ)。
- RuleDialog の欄の間隔が 16、ラベルと入力の間が 4。
- XAML を変えたため、Snapwell.Settings.exe を実際に起動し、ルールを追加・ウィンドウを選んで追加・取り込み (書き出した JSON を 1 度読む)・保存せず閉じる確認の 4 つのダイアログを開いて、文字が読めること・ボタンが押せることを確かめる。ライトとダークの両方。確認中は保存する値を変えない (保存ボタンは押さない)。
- ビルドは win-x64 / win-arm64 の両方 (Snapwell.Settings も)。dotnet test と dotnet format が通る。表にない off-grid の値を直した場合は、その一覧と理由を PR に書く。
