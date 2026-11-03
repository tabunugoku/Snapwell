# トレイメニューの磨き込み（数値仕様）

出典: `docs/mockups/tray-review.html`。単位は論理 px（物理 px は拡大率を掛けて四捨五入）。座標はパネル外縁の左上が原点。
見本の HTML と食い違ったら、この表を正とする。

## 変更の表

| ID | 要素 | 現状 | 目標 | 場所 |
|---|---|---|---|---|
| L1 | パネル 幅 / 角 | 320 / 8 | 320 / 8（変更なし） | `TrayMenuLayout.cs:157` |
| L2 | 行・区切り線の横位置 | 行 x7 w306、区切り線 x7 w306（ヘッダー）と x13 w294（他） | 行 x8 w304、区切り線 3 本とも x8 w304 h1 | `TrayMenuLayout.cs:70-81` |
| L3 | 左端の列 | アイコン x18、補助行 x17、ラベル x34、社名 x49 | アイコン x16、補助行 x16、ラベル x16、社名 x44 | `TrayMenuLayout.cs:18,59 / TrayMenuRenderer.cs:43,57,62,74` |
| L4 | ヘッダーの高さ / 区切り線 / アイコン | 高さ 52、線 y51、アイコン (18,18) 20×20 | 高さ 48、線 y48（h1）、アイコン (16,14) 20×20 | `TrayMenuLayout.cs:18,80` |
| L5 | スイッチ（トラック 40×20、つまみ 12） | x263 y18 | x264 y14（右の余白 16）。つまみは x+4 から 20 動く（変更なし） | `TrayMenuLayout.cs:59,120` |
| L6 | 状態の表示 | 丸薬 高さ19、文字 10.5 / 600、左右 8、右端 = スイッチの左 − 10、縦はアイコンの中心 | 点 径6 + 間 6 + 文字 12 / 400。背景なし。右端 = スイッチの左 − 12。縦はアイコンの中心（y24） | `TrayMenuLayout.cs:20 / TrayMenuRenderer.cs:45-54` |
| L7 | 補助行「モニター」（コンパクトでは出さない） | rect(17, 58, 286, 22)、11.5 | rect(16, 52, 288, 24)、12 | `TrayMenuRenderer.cs:55-59` |
| L8 | 状態文の行（Status あり） | rect(17, 80, 286, 24) / コンパクト rect(17, 54, 286, 22)、11.5 | rect(16, 76, 288, 24) / コンパクト rect(16, 52, 288, 24)、12 | `TrayMenuRenderer.cs:60-65` |
| L9 | 項目の前の区切り線 | y = start − 5。コンパクトでは y53 になり、ヘッダーの線 y51 と 2 本並ぶ | y = 補助行の下端 + 4（通常）。コンパクトでは描かない | `TrayMenuLayout.cs:80` |
| L10 | 最初の項目の y（start） | 通常 89 / コンパクト 58（Status あり + 24） | 通常 84 / コンパクト 52（Status あり + 24） | `TrayMenuLayout.cs:63` |
| L11 | 項目の行 | x7 w306 h32、ピッチ 32、ラベル x34、13 / 400 | x8 w304 h32、ピッチ 32、ラベル x16、13 / 400 | `TrayMenuLayout.cs:70-77 / TrayMenuRenderer.cs:67-77` |
| L12 | 2 つの組のあいだ | 間 9（線は組の上端 + 4）/ コンパクト 間 2（線は + 0） | 間 8（線は + 4）/ コンパクト 間 4（線は + 2） | `TrayMenuLayout.cs:64,78` |
| L13 | 下の余白 | 8 / コンパクト 4 | 8 / コンパクト 4（変更なし） | `TrayMenuLayout.cs:67` |
| L14 | 高さの式と Max スクロール | 表の「現状」の列。MaxScrollOffset も start と groupGap から導く | 表の「目標」の列。MaxScrollOffset・ItemsViewport の式を新しい start / groupGap に合わせる | `TrayMenuLayout.cs:66-68,159-161` |
| T1 | 社名「Snapwell」 | 13 / 600 | 13 / 600（変更なし） | `TrayMenuRenderer.cs:43` |
| T2 | 項目のラベル | 13 / 400 | 13 / 400（変更なし） | `TrayMenuRenderer.cs:74` |
| T3 | 補助行・状態文 | 11.5 / 400 | 12 / 400 | `TrayMenuRenderer.cs:57,63` |
| T4 | 状態の文字 | 10.5 / 600（丸薬の中） | 12 / 400 | `TrayMenuRenderer.cs:29,53` |
| K1 | 補助文字 Muted（ライト） | rgb(120,119,116) = #787774 | rgb(107,106,103) = #6B6A67。ダークは変更なし。OverlayPalette 共通なのでランチャーなども同時に変わる | `OverlayPainter.cs:48` |
| K2 | ホバーの面（新しい Hover） | Surface2: ライト #F9F9F8 / ダーク #252524 | Hover: ライト #F0F0EE / ダーク #2D2D2C。トレイの行だけが使う | `OverlayPainter.cs:40-60 / TrayMenuRenderer.cs:120` |
| K3 | 状態タグの背景色（6 色） | Green/Yellow/RedBackground を丸薬に塗る | 背景は使わない。点の色は既存の Green/Yellow/RedText。Background の 3 プロパティは、他に使い道がなければ削除 | `OverlayPainter.cs:62-73 / TrayMenuRenderer.cs:45-52` |
| K4 | 更新の項目のラベル色 | Text | BlueText（#1F6C9F / #8CCBF2）。無効にはならない | `TrayMenuRenderer.cs:71` |
| K5 | キーボードの焦点枠（スイッチ・項目） | BlueText 1 px、角 4 | BlueText 2 px、角 4（内側） | `TrayMenuRenderer.cs:96,125` |

## 高さ

| 場合 | 現状 | 目標 |
|---|---|---|
| 通常 | 234 | 228 |
| 通常 + 状態文 | 258 | 252 |
| コンパクト | 192 | 188 |
| コンパクト + 状態文 | 216 | 212 |
| 更新の項目が 1 つ増えるごと | + 32 | + 32 |

## 受け入れ条件

1. 上の表の数値どおりに TrayMenuLayout（Rows / Separators / Toggle / HeaderIcon / ItemsViewport / MaxScrollOffset と高さ）を直す。StatusTag は StatusIndicator のような名前に直してよい
2. TrayMenuRenderer: 丸薬を点＋文字に置き換える。補助行 12、更新の項目は BlueText、焦点枠 2 px、ホバーは新しい OverlayPalette.Hover
3. OverlayPalette: Hover を足す。ライトの Muted を #6B6A67 にする。使われなくなった Background を削除する（他で使っていれば残す）
4. tests/Snapwell.Core.Tests/Tray（TrayMenuLayoutTests・TrayMenuCenteringTests・TrayMenuIconDrawingTests・TrayUpdateMenuTests）と tests/Snapwell.App.Tests/Tray・Localization（TrayMenuHeaderTests・TrayMenuTextCenteringTests・TrayStatusRenderingTests）の期待値を、この表に合わせて更新する。新しい値の根拠をテスト名に書く
5. コントラストのテストを足す: ライトの Muted / Surface ≥ 4.5、ダークも ≥ 4.5。Hover は Surface との比 ≥ 1.1
6. 4 倍数の検査を足す: 拡大率 1.0 のとき、行・区切り線・補助行の x / y / 幅 / 高さが、ヘッダーの中央合わせ（y14）以外はすべて 4 の倍数（または 1）
7. 変えない: スイッチの物理モーション（TrayMenuToggleAnimation）、アイコン、文言（ja / en / zh）、トレイのコマンド番号、フックとポーリング
8. 拡大率 1.0 / 1.25 / 1.5 / 2.0 で、行とスイッチの丸め誤差が 1 px を超えないこと（既存のテストの方式に合わせる）
9. dotnet build（App を win-x64 / win-arm64）、dotnet test --no-restore、dotnet format --verify-no-changes --no-restore が通ること
10. docs/MANUAL_TEST.md のトレイの節に「実機未確認」の項目を足す（明暗 × 100% / 150% DPI、4 状態、キーボード、ホバー）。docs/I18N.md の 298 行の根拠は幅 320 のまま変わらない
11. Muted の変更でランチャーの補助文字が変わる。ランチャーの見た目が崩れていないことを、同じ PR の説明に書く
