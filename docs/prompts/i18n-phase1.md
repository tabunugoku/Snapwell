<!--
多言語対応フェーズ 1 を Codex に渡すプロンプトの下書き（.claude/skills/codex-prompt のひな形に合わせた）。
渡す前に Opus が次を埋める・確かめる:
- <コミット>: worktree を作った origin/main のコミット（この設計のブランチを main に入れたあと）
- docs/I18N.md §8 の判断（とくに 1〜4・11）がユーザーの決定と食い違っていないか。食い違えば該当の項目を直す
- 実装役: Codex（gpt-6.1-sol）。Settings・App の Win32/WinUI と Core の基盤にまたがり、項目が多いため（CLAUDE.md の目安）
- worktree: git worktree add ../Snapwell-wt/i18n-phase1 -b codex/i18n-phase1 origin/main のあと dotnet restore（-r を付けない）
-->

多言語対応のフェーズ 1（基盤と、一般ページ・ナビゲーション・トレイのパイロット）を実装する。CLAUDE.md の「守ること」に従うこと。設計は docs/I18N.md、訳語と表記は docs/I18N_GLOSSARY.md。最初に両方を読むこと。

【共通】
- いまの作業フォルダは worktree で、origin/main (<コミット>) から作ったブランチ codex/i18n-phase1 にいる。ブランチは作り直さない。
- コミットと push はしない (Codex は .git をサンドボックスで書けない)。Opus が、最後の応答のコミット計画のとおりにコミットする。
- パッケージの復元は済んでいる。ネットワークは使えないので、ビルドとテストは --no-restore を付けて実行する。新しい NuGet パッケージは足さない (必要なら notes に書く)。
- 各項目のあとで次を実行し、通ること:
  - dotnet build src/Snapwell.App/Snapwell.App.csproj -c Release -r win-x64 --no-restore
  - dotnet build src/Snapwell.App/Snapwell.App.csproj -c Release -r win-arm64 --no-restore
  - dotnet build src/Snapwell.Settings/Snapwell.Settings.csproj -c Release -r win-x64 --no-restore
  - dotnet build src/Snapwell.Settings/Snapwell.Settings.csproj -c Release -r win-arm64 --no-restore
  - dotnet test --no-restore (サンドボックスでは名前付きパイプが使えず止まることがある。止まったら not_run にして notes に書く。Opus がローカルで実行する)
  - dotnet format --verify-no-changes --no-restore (同上)
- main へのマージ、タグ、リリース、バージョン変更はしない。x86 や AnyCPU のビルド対象を足さない。DLL インジェクション・非公開 API を使わない。外部通信をしない。
- NativeAOT で動くこと: リフレクション、`JsonSerializer` のリフレクション版、`ResourceManager`、`.resx` を使わない。`Enum.ToString()` は使ってよい。
- `InvariantGlobalization=true` は変えない。数値の書式は `CultureInfo.InvariantCulture`。
- 触ってよいファイル:
  - A: src/Snapwell.Core/Snapwell.Core.csproj、src/Snapwell.Core/Localization/ 以下 (新規)、tests/Snapwell.Core.Tests/Localization/ 以下 (新規)
  - B: src/Snapwell.Core/Settings/DeviceState.cs、tests/Snapwell.Core.Tests/Settings/ の端末状態のテスト (既存のファイルに足すか、新しいファイル)
  - C: src/Snapwell.Platform/NativeMethods.txt、src/Snapwell.Platform/Globalization/ 以下 (新規)、tests/Snapwell.Platform.Tests/Globalization/ 以下 (新規)
  - D: src/Snapwell.Settings/ の App.xaml、App.xaml.cs、MainWindow.xaml、MainWindow.xaml.cs、Pages/GeneralPage.xaml、Pages/GeneralPage.xaml.cs、SettingsWindowActivator.cs、Localization/ 以下 (新規)
  - E: src/Snapwell.Core/Tray/TrayMenuModel.cs、src/Snapwell.App/Program.cs、src/Snapwell.App/Lifecycle/AppHost.cs、src/Snapwell.App/Lifecycle/AppContracts.cs、src/Snapwell.App/Tray/TrayIconPolicy.cs、src/Snapwell.App/Tray/Menu/TrayMenuController.cs、src/Snapwell.App/Tray/Menu/TrayMenuRenderer.cs、src/Snapwell.App/Overlay/GdiOverlayText.cs、tests/Snapwell.App.Tests/ 以下 (新しいテストと、言語に関するテストの追加)
  - DOC: docs/DESIGN.md (§12.1 に端末状態の版 4 と Language を書く)、docs/MANUAL_TEST.md (「多言語（フェーズ 1）」の節を足す。すべて実機未確認)
  - ほかのファイルは触らない。既存のテストの日本語の期待値は変えない (Text.Current の既定が日本語なので、そのまま通るはず)。
- 項目の間で同じファイルを触らない。カタログの JSON (4 言語) は A だけが書く。D と E が使うキーは、下の一覧のとおり A で先に入れる。
- 各項目に、変更前のコード (<コミット>) では成り立たない振る舞いを確かめるテストを足す。テストのクラス名とメソッド名は、確かめることが分かる英語の名前にする。
- 新しく書くコメントは日本語の常体。公開する型とメソッドには 1 行の /// <summary> を書く。

【A. Core: 文言のカタログと言語の判定 (設計: docs/I18N.md §2.4, §3.3, §6, §7)】
目的: Settings・App・Core が同じカタログから文言を引けるようにする。言語の判定を純粋関数にする。
作り方:
1. src/Snapwell.Core/Localization/ に次を置く (名前は docs/I18N.md §2.4 の案に従う):
   - `UiLanguage` (Japanese, English, ChineseSimplified, ChineseTraditional) と、タグとの変換 (`ja-JP`/`en-US`/`zh-Hans`/`zh-Hant`。PrimaryLanguageOverride と FrameworkElement.Language に使う) と、保存値 (`ja`/`en`/`zh-Hans`/`zh-Hant`/`auto`) との変換。
   - `UiLanguageResolver.Resolve(string? preference, string? osTag)`: docs/I18N.md §3.3 の表のとおり。
   - `TextCatalog` (1 言語分。`Load(UiLanguage)`、`Get`、`Format`、`Plural`、`Keys`) と `Text` (`Current`、`Use`)。`Text.Current` の既定は日本語。`Use` は参照を volatile で差し替える。
   - JSON は Strings/ja.json・en.json・zh-Hans.json・zh-Hant.json。平らなオブジェクト。`//` コメント可。`Utf8JsonReader` (`JsonCommentHandling.Skip`) で読む。csproj で EmbeddedResource (`LogicalName="i18n/%(Filename)%(Extension)"`)。
   - 足りないキー: 選んだ言語 → 英語 → キーそのもの。DEBUG では `⟦キー⟧`。
   - `Format` のプレースホルダーは名前つき `{name}`。`{{` と `}}` はかっこそのもの。値の数値は InvariantCulture。未知のプレースホルダーは残す。
   - `Plural(key, count, ...)`: 英語のときだけ count == 1 で `key.One`、それ以外は `key.Other`。`{count}` を自動で渡す。
2. 4 言語の JSON に、フェーズ 1 のキーを入れる。日本語の値は、いまの画面の文言をそのまま写す (下の出典の行)。英語と中国語は docs/I18N_GLOSSARY.md の訳語と §1 の表記の方針で書く。用語集で「要判断」の語は推奨の訳を使い、使った語を notes に挙げる。キー名は docs/I18N.md §7.1 に従う。
   - Window.SettingsTitle (MainWindow.xaml:8)、Window.SetupTitle (SetupWindow.xaml:5)
   - Nav_<ページ>.Content と Nav_<ページ>.AutomationName (MainWindow.xaml のナビゲーション 9 項目)、MainWindow の題の TextBlock と保存ボタン (MainWindow.xaml)
   - MainWindow.xaml.cs:147–148, 196–200 の文言 (保存の状態、閉じるときの確認)
   - General_*: GeneralPage.xaml の全文言 (Text と AutomationProperties.Name。「準備中」は除く) と GeneralPage.xaml.cs:45, 73, 79, 117, 122, 196, 206, 208, 211, 268, 275–277, 284, 318 の文言
   - Language.Auto (「自動（OS に合わせる）」)、Language.Japanese/English/ChineseSimplified/ChineseTraditional (各言語の自称。4 つの JSON で同じ値: 日本語 / English / 简体中文 / 繁體中文)、Language.ChangedNotice (例「設定画面を開き直すと、この画面の言語が切り替わります。トレイのメニューは次に開いたときから切り替わります。」)
   - Tray.Arrange・Tray.Launcher・Tray.Settings・Tray.Exit (TrayMenuModel.cs:19–22)、Tray.Status.Error・Paused・Active (TrayMenuController.cs:17–19)、Tray.Tip.Paused (TrayIconPolicy.cs:35)、Tray.Tip.Attention (「Snapwell: {status}」。TrayIconPolicy.cs:33)、Tray.Monitors.One / .Other (「モニター構成: {count} 画面」。TrayMenuRenderer.cs:53)
3. テスト (tests/Snapwell.Core.Tests/Localization/):
   - `UiLanguageResolver`: docs/I18N.md §3.3 の表の全行 ([Theory])。明示の preference では osTag を見ないこと。大文字小文字・`_` の扱い。
   - 4 言語のキーの集合が一致する。値が空でない。
   - 各キーのプレースホルダー名の集合が 4 言語で一致する。
   - `.One` と `.Other` が対になっている。
   - 参照の網羅: リポジトリのルート (Snapwell.sln のあるフォルダー。テストの出力フォルダーから上へたどる) の src/**/*.cs から `Text.Current.Get("…")`・`Text.Current.Format("…"`・`Text.Current.Plural("…"` のリテラルのキーを、src/**/*.xaml から `Loc.Uid="…"` を集め、Uid は `<Uid>.` で始まるキーが 1 つ以上、それ以外はキーそのものが ja.json にあること。呼び出しの形を変えるなら、この正規表現も合わせる。
   - `Format`・`Plural`・足りないキーの落とし先。言語を変えるテストは `TextCatalog.Load` を使い、`Text.Use` を使うなら元に戻し、並列のテストと共有しない。
受け入れ条件 (それぞれテストで確かめる):
- §3.3 の表のすべての行で期待どおりの言語になる。
- 4 言語の JSON のキーとプレースホルダーが一致し、空の値がない。
- `Text.Current` の既定が日本語で、`Text.Current.Get("Tray.Arrange")` が「現在のモニターを整列」。
- 英語で `Plural("Tray.Monitors", 1)` と `Plural("Tray.Monitors", 2)` が別の文言になる。
競合・境界の観点:
- 埋め込みリソースの名前が App と Settings の exe でも同じに引けること (Core のアセンブリから `GetManifestResourceStream`)。
- `Text.Use` をフックのスレッドやオーバーレイのスレッドから読んでも安全なこと (参照の差し替えだけで、カタログ自体は変更しない)。
- JSON の BOM、末尾のカンマ (`AllowTrailingCommas`)、重複キー (テストで検出して失敗させる)。

【B. Core: 端末状態の言語 (設計: docs/I18N.md §3.1)】
作り方:
1. `DeviceState` に `public string Language { get; set; } = "auto";` を足し、`CurrentSchemaVersion` を 4 にする。
2. `Migrate`: 版 3 以下は `Language = "auto"`。既存の移行 (SetupCompleted など) は変えない。
3. 未知の値・null・空は `auto` にそろえる (読み込みの正規化。`SettingsStores.Device` の正規化に足すか、`DeviceState` に正規化の関数を置く。触ってよいのは DeviceState.cs だけなので、`DeviceState` 側に置き、Migrate から呼ぶ)。保存値の一覧は A の変換を使う。
受け入れ条件:
- 版 3 の device.json (Language なし) を読むと Language が "auto" で、ほかの値は変わらない。
- "fr" や "" を読むと "auto"。"zh-Hant" はそのまま。
- 版 5 のファイルは既存の方針どおり読み取り専用になる。
競合・境界の観点:
- 列挙型にしない理由 (UseStringEnumConverter で未知の値が JsonException になる) をコメントに書く。
- 版を上げたことで、既存の端末状態のテスト (版 3 を前提にしたもの) が壊れないか確かめる。壊れたら、そのテストの期待値の版だけを直し、notes に書く。

【C. Platform: OS の表示言語 (設計: docs/I18N.md §3.2)】
作り方:
1. NativeMethods.txt に GetUserDefaultUILanguage と LCIDToLocaleName を足す (CsWin32)。
2. src/Snapwell.Platform/Globalization/OsLanguageSource.cs: `static string? GetDisplayLanguageTag()`。LANGID → LCIDToLocaleName。失敗は null。GetUserPreferredUILanguages は使わない (理由: docs/I18N.md §2.2)。
受け入れ条件:
- LCID 0x0411 を渡す内部の変換関数が "ja-JP"、0x0404 が "zh-TW"、0x0804 が "zh-CN" を返す (テストは変換関数で確かめる。OS の現在の言語には依存させない)。
- `GetDisplayLanguageTag()` が null でない値を返す (この PC の環境に依存しない形で: 空でないことだけを確かめる)。
競合・境界の観点:
- 0x0C04 (zh-HK)、0x1404 (zh-MO) も A の判定で繁体字になること (A のテストの表と矛盾しないこと)。

【D. Settings: 言語の適用・添付プロパティ・一般ページとナビゲーション (設計: docs/I18N.md §2.4, §3.4, §5.3)】
作り方:
1. App.xaml.cs のコンストラクターで、InitializeComponent の前に: 端末状態を読み (`SettingsStores.Device().Load()` の値。読めなければ "auto")、`UiLanguageResolver.Resolve(state.Language, OsLanguageSource.GetDisplayLanguageTag())` → `Text.Use` → `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = タグ` (自動のときも必ず設定する)。`Windows.Globalization.ApplicationLanguages` (OS 版) は使わない (パッケージなしで例外になる)。
2. InitializeComponent のあと、窓を作る前に、`Resources["SnapwellUiFontFamily"]` と `Resources["ContentControlThemeFontFamily"]` を docs/I18N.md §5.3 の表の書体に差し替える。各窓のルート要素の `Language` にタグを入れる。
3. src/Snapwell.Settings/Localization/Loc.cs: 添付プロパティ `Loc.Uid`。値が入ったら、カタログの `<Uid>.Text`・`.Content`・`.Header`・`.PlaceholderText`・`.ToolTip`・`.AutomationName` を探し、要素の型に合わせて設定する (switch で書く。リフレクションを使わない)。フェーズ 1 で要る型 (TextBlock、Button、ComboBoxItem、NavigationViewItem、ToggleSwitch、ComboBox、InfoBar の Title/Message) を必ず扱う。対応していない組み合わせは DEBUG で例外にする。
4. MainWindow.xaml・GeneralPage.xaml の日本語の属性を `l:Loc.Uid` に置き換え、日本語の Text/Content/AutomationProperties.Name を消す。Window の Title はコードで `Text.Current.Get("Window.SettingsTitle")` を設定する。MainWindow.xaml.cs・GeneralPage.xaml.cs の文言も Text.Current に置き換える。
5. 一般ページの「言語」: 「準備中」のタグと IsEnabled="False" を外す。項目は 自動／日本語／English／简体中文／繁體中文 の順。幅は 200 (モックアップに合わせた)。選んだら、有効・無効のトグルと同じ流れ (`DeviceStateChanges.Update`、新しい版・読み取り専用・失敗の InfoBar) で `Language` をすぐ保存し、成功したら Language.ChangedNotice を Informational の InfoBar で出す。ページを開いたときは、保存されている値を選んだ状態にする (このとき保存しない)。
6. SettingsWindowActivator: 4 言語すべての Window.SettingsTitle と Window.SetupTitle を順に探す (`TextCatalog.Load` で各言語のタイトルを取る)。
受け入れ条件:
- 参照の網羅のテスト (A) が、D で使ったキーと Uid をすべて見つけて通る。
- Settings が両 RID でビルドできる (publish は Opus が確かめる)。
- 移行した 2 つの XAML に、日本語の属性値が残っていない (A の参照のテストとは別に、notes で確認結果を書く)。
競合・境界の観点:
- 端末状態の読み込みが失敗・新しい版でも、設定画面は起動する (言語は自動として扱う)。
- `StaticResource` で書体を引くスタイルに、差し替えが効く順序か (効かない見込みなら notes に書く。実機確認は MANUAL_TEST に入れる)。
- 言語の ComboBox の初期化で SelectionChanged が走り、保存してしまわないこと (既存の `_refreshing` と同じ守りをする)。
- 2 つ目の設定画面の起動 (SingleInstance) が、タイトルの言語が違っても既存の窓を前面に出すこと。

【E. App: 言語の適用とトレイ (設計: docs/I18N.md §3.4, §5.3)】
作り方:
1. Program.cs: 起動時に OS の表示言語のタグを 1 回だけ取り、AppHost に渡す (AppContracts.cs に必要なら引数や小さなインターフェースを足す)。
2. AppHost: 起動時の端末状態の読み込みと `ApplyDevice` で、`UiLanguageResolver.Resolve(device.Language, osTag)` → `Text.Use`。言語が変わったら、既存の `_tray.Update(State())` の流れでホバーの文言が作り直されることを確かめる。状態の文言 (`_status` など) はこのフェーズでは変えない。
3. TrayMenuModel.cs の 4 項目、TrayMenuController.cs の状態タグ、TrayIconPolicy.Tip、TrayMenuRenderer.cs の「モニター構成」を `Text.Current` から引く。
4. GdiOverlayText: ASCII 以外の文字を描く書体を、`Text.Current` の言語で選ぶ (ja・en: Yu Gothic UI、zh-Hans: Microsoft YaHei UI、zh-Hant: Microsoft JhengHei UI)。書体がなければ、いまと同じく既定の書体へ落とす。フォントのキャッシュのキーに書体の名前を含め、言語を変えたあとに古い書体を使わない。`japanese` という名前は `cjk` に改める (このファイルの中だけ)。
受け入れ条件 (それぞれテストで確かめる):
- 既存のトレイのテスト (日本語の期待値) がそのまま通る。
- 英語のカタログを使ったとき、トレイの 4 項目・状態タグ・ホバー・モニター構成が英語になる (`Text.Use` を使うテストは元に戻し、並列で共有しない)。
- `ApplyDevice` に Language を変えた端末状態を渡すと、`Text.Current` の言語が変わり、トレイの更新が 1 回行われる (既存の AppHost のテストの偽物を使う)。
- GdiOverlayText の書体の選択が言語ごとに期待どおり (Win32 に触れない判断の関数を切り出してテストする)。
競合・境界の観点:
- `Text.Use` はトレイの STA から呼ぶ。オーバーレイのスレッドが描画中に言語が変わっても、1 回の描画の中で言語が混ざらないこと (描画の最初に `Text.Current` を 1 回だけ取る)。
- NOTIFYICONDATA の szTip は 128 文字まで。英語・中国語のホバーで超えないこと (既存の切り詰めがあれば、それに従う)。
- 端末状態が読み取り専用・新しい版のときも、言語は自動として扱い、起動を止めない。

【DOC. 文書】
- docs/DESIGN.md §12.1: 端末状態の版 4 で `Language` を足したこと、既定は "auto"、未知の値は "auto"、版 3 以下は "auto" に移行すること。§14 の多言語の行は変えない (変更済み)。
- docs/MANUAL_TEST.md に「多言語（フェーズ 1）」の節を足す。すべて「実機未確認」:
  - 一般ページで 5 つの言語を選び、開き直すと、ナビゲーション・窓のタイトル・一般ページがその言語になる。日本語の文字が残らない。
  - 言語を変えると、本体を再起動せずに、次に開いたトレイメニューとホバーがその言語になる。
  - 中国語の画面 (設定画面とトレイ) で、漢字が中国語の字形になる (例: 「设」「設」の形。日本語の字形にならない)。
  - 「自動」で、Windows の表示言語を英語・中国語 (簡体・繁体) に変えたとき、期待どおりになる。
  - 2 つ目の設定画面を起動すると、どの言語でも既存の窓が前面に出る。
  - 「言語」のドロップダウン (幅 200) で、どの言語の「自動」も切れない。
  - ARM64 と x64 の両方で publish した exe で確かめる。

【最後に】
- 自分の差分を、上の受け入れ条件と競合・境界の観点で点検し、満たさない点があれば notes に書く。
- 最後の応答は、指定された JSON の形で返す:
  - items: 項目ごとに id ("A"・"B"・"C"・"D"・"E"・"DOC")、コミットのメッセージ (日本語。「Core: …」「Platform: …」「設定画面: …」「App: …」「文書: …」の形)、その項目で変えたファイルの一覧 (新しいファイルとテストを含む)、修正前のコードでテストが失敗したか。
  - shared_files: 2 つ以上の項目が触ったファイル (無ければ空の配列)。
  - checks: build_x64・build_arm64・test・format の結果と、テストの件数の要約。
  - notes: 判断した点 (使った要判断の訳語を含む)、点検で見つけた点。
  - manual_test: 実機で確かめてほしい点。
