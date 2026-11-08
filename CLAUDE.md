# Snapwell

## 必ず守る: 返信の冒頭の呼び名（カナリア）
Claude Code の返信は、1 通の例外もなく、先頭を `tabunugoku` で始める。短い答えでも付ける。消えたら長い会話でコンテキストが劣化しているしるしなので、利用者は新しい会話に切り替える。対象は利用者との会話だけで、実装役（Codex・Gemini）の出力、コミット計画の JSON、コミットメッセージ、PR、ファイルの中身には付けない。

## 引き継ぎ（HANDOFF.md）
会話が長くなったとき、または前の作業をはっきり思い出せないと感じたときは、そう利用者に伝え、先に `HANDOFF.md`（リポジトリ直下。Git には入れない）に短い引き継ぎを書く。目的、終わったこと、残っていること、主なファイル、未解決の問い。新しい会話はこれを読んで続きから始める。

Windows 11 用のウィンドウマネージャー。画面のグリッド分割、間隔つきの整列、区画単位のリサイズに加えて、投げたウィンドウが物理モデル（ばね・磁石・壁）で区画や四隅に吸い込まれるスナップを持つ。

設計は `docs/DESIGN.md`。最初に読むこと。競合調査は `reports/competitive-analysis.md`（ローカルだけにあり、Git には入れない）。

## 現在のタスク
現在の状態、残っていること、未解決の問いは `HANDOFF.md` を見る（Git には入れない作業メモ）。公開済みの版と機能の範囲は `docs/RELEASE.md` とタグを見る。

## 守ること
- **x64 と ARM64 のネイティブだけ**: ビルド対象は `win-x64` と `win-arm64`。x86（`win-x86`）や AnyCPU の成果物は作らない。どの変更も両方の RID でビルドが通ること。ソリューションの `Any CPU` 構成は開発とテストのためだけに残す（x86・x64 の構成は置かない）。出荷する成果物は必ず `-r win-x64` / `-r win-arm64` で作る
- **DLL インジェクションをしない**: フックは `SetWinEventHook(WINEVENT_OUTOFCONTEXT)`、`WH_KEYBOARD_LL`、`WH_MOUSE_LL` だけ。`SetWindowsHookEx` のグローバルな in-context フック、`CreateRemoteThread`、他プロセスのメモリの読み書きは使わない（対象のアプリのアーキテクチャに左右されないため）
- **非公開 API を使わない**: 仮想デスクトップの非公開 COM（`IVirtualDesktopManagerInternal` など）、文書化されていないエクスポートや序数での呼び出しは使わない。使うしかない場合は、先にユーザーに確認する
- **OS の設定を変えたら必ず戻す**: 標準スナップの停止などで OS の設定を変えるときは、変更前の値を状態ファイルに退避し、無効化・終了・異常終了後の起動のどれでも復元する。変えてよい設定は `docs/DESIGN.md` §8 にあるものだけ
- **例外: サインイン時の自動起動**（ユーザーが認めた例外）: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` とタスク スケジューラの `\Snapwell-<SID>` への登録は、自動で起動するための設定なので、終了しても消さない。登録するのは利用者が設定画面でオンにしたときだけで、オフにしたら消す（`docs/DESIGN.md` §11）。開発中のビルドで登録するのは試験のためだけで、ユーザーの指示があったときに限る。試験が終わったら必ず登録を消し、Run の値の削除と `schtasks /Query /TN "Snapwell-<SID>"` でタスクが消えたことを確かめる。テストでは偽のレジストリと偽のタスク サービスを使い、実際のレジストリとタスク スケジューラに書かない
- **アニメーションは物理モデルから**: 移動の動きは質量・剛性・減衰から計算する。継続時間（ms）やイージング曲線を固定値で書かない（feel と同じ方針。`docs/DESIGN.md` §5）
- **フックのコールバックはすぐ返す**: 低レベルフックの中で重い処理・同期の待ち・ウィンドウの移動をしない。処理は別のスレッドに渡す（遅いと OS にフックを外されるため）
- **座標は物理ピクセル**: プロセスは Per-Monitor DPI Aware v2。ウィンドウの見た目の矩形は `DWMWA_EXTENDED_FRAME_BOUNDS` で取り、見えない枠を補正する
- **外部通信なし**: 解析・トラッキング・自動送信をしない。クラウド同期は、利用者が選んだ同期フォルダへのファイルの読み書きで行う（`docs/DESIGN.md` §12）。例外は更新の確認だけ: 利用者が「更新を自動で確認する」を有効にしたときに GitHub Releases の `latest.json` を GET する（既定は無効。「今すぐ確認」は利用者の操作で通信する。`docs/DESIGN.md` §17）。利用者が「更新して再起動」を押したときだけ、署名つきの配布ファイルを GitHub からダウンロードする（GET と User-Agent のみ。認証・Cookie なし。`docs/DESIGN.md` §17.2）
- **公開してはいけないもの**: `reports/` と `research_notes/` は Git に入れない（`.gitignore` 済み）。スクリーンショットやモックアップに、実在の個人のアカウント名やファイル名を写さない
- **サードパーティのコード**: feel（diip3sh/feel、MIT）のアルゴリズムを移植したときは、著作権表示を `THIRD_PARTY_NOTICES.md` に入れる

## フォルダの構成
- `src/Snapwell.Core/`: 物理モデル、グリッドと間隔の計算、整列、設定のモデル。Win32 に依存しない（単体テストの対象）
- `src/Snapwell.Platform/`: Win32 との接続。フック、ウィンドウの列挙と移動、モニター、DWM、標準スナップの制御、スタートアップ
- `src/Snapwell.App/`: トレイ常駐、オーバーレイ（ランチャー、区画のプレビュー）
- `src/Snapwell.Settings/`: 設定画面（WinUI 3、パッケージ化しない別の exe。Windows App SDK は自己完結で同梱）。Win32 と同じく、テストする判断は Core に置く
- `tests/`: 単体テスト（xUnit）。`Snapwell.Core.Tests`、`Snapwell.Platform.Tests`、`Snapwell.App.Tests`
- `site/`: ホームページ（GitHub Pages。`site/**` を main に push すると自動で公開する）。`release/`: 更新の要約（`summary.json`）と更新情報の公開鍵。`tools/`: `ManifestTool`（更新情報の生成と署名）、`film`（README の画像と動画）など
- `docs/`: 設計（`DESIGN.md`）、モックアップ（`mockups/`）、実機での確認（`MANUAL_TEST.md`）、リリース手順（`RELEASE.md`）、多言語（`I18N.md`）

## 技術スタック
- C# / .NET 9 以降、NativeAOT。Win32 の呼び出しは CsWin32 で生成する
- 設定画面は WinUI 3（Windows App SDK）。オーバーレイは Win32 のレイヤードウィンドウと DirectComposition
- テストは xUnit。Win32 に触る部分は、インターフェースを挟んで Core から切り離し、Core だけを単体テストする

## コードとコメントの書き方
Codex、Gemini、Claude Code のどれが書いても、同じ見た目にそろえる。

- **整形**: `.editorconfig` に従う（4 スペース、file-scoped namespace、`nullable enable`）。`dotnet format --verify-no-changes` が通ること。1 行はおよそ 120 字まで
- **名前**: 型・メソッドは PascalCase、ローカル変数は camelCase、private フィールドは `_camelCase`。物理の記号（`m`、`k`、`c`、`zeta`）は、式をそのまま写すところだけ短い名前を許す
- **コメントは日本語**: 新しく書くコメントは日本語の常体（短い体言止めまたは「〜する」）。識別子・API 名はそのまま
- **コメントの中身**: 「なぜそうするか」「前提・制約・落とし穴」を書く。Win32 や DWM の挙動への推測は「実機未確認」と書く。公開する型とメソッドには 1 行の `/// <summary>` を書く
- **無駄を足さない**: 使われないコード、コメントアウトした古いコード、「念のため」の分岐を残さない。既存のコードの整形し直しは、機能の変更と同じコミットに入れない
- **確認**: push の前に次を通す。ソリューションは RID を付けてビルドできない（NETSDK1134）ので、RID ごとのビルドは出荷するプロジェクト（`src/Snapwell.App`）に対して行う
  - `dotnet build src/Snapwell.App/Snapwell.App.csproj -c Release -r win-x64 --no-restore`
  - `dotnet build src/Snapwell.App/Snapwell.App.csproj -c Release -r win-arm64 --no-restore`
  - 設定画面 (`src/Snapwell.Settings`、WinUI 3) も同じく両 RID で: `dotnet build src/Snapwell.Settings/Snapwell.Settings.csproj -c Release -r win-x64 --no-restore`（と `win-arm64`）
  - `dotnet test --no-restore`
  - `dotnet format --verify-no-changes --no-restore`
  - **WinUI（`src/Snapwell.Settings`）の XAML を変えたら、PR にする前に実際に起動して画面が開くことを確かめる**。ビルドとテストでは、XAML の読み込みの失敗（リソースが見つからない、など）は出ない。v1.0.4 で、`Border` 自身の `ThemeResource` が自分の `Resources` を探さず、設定画面が起動しない版を公開した。起動できないときの調べ方は `HANDOFF.md` の「手順の注意」
- **NativeAOT の publish**: `dotnet build -r` では AOT のコンパイルは走らない。出荷物は `dotnet publish src/Snapwell.App/Snapwell.App.csproj -c Release -r win-x64`（と `win-arm64`）で作る。Visual Studio の C++ ツール（x64 と ARM64 の両方）が要る。Git Bash からは `ProgramFiles(x86)` が渡らず `vswhere.exe` が見つからないので、PowerShell から、`${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer` を PATH に足して実行する（このPCで両 RID とも確認済み）

## 進め方
- 1 項目 1 コミット。各コミットでビルド（両 RID）とテストが通る状態を保つ。main へのマージは、ユーザーが指示したときだけ
- **リリースは、公開の前に、下書きの zip の `Snapwell.Settings.exe`（x64・ARM64）を起動して確かめる**。確かめるまで `--draft=false` にしない。壊れた版を公開したときは、同じ版番号を使い回さず、次の版で直すほうが安全（更新の案内が出るため）
- 実機で確かめられないこと（フック、DWM、マルチモニター、スリープ復帰、ARM64 実機）は、`docs/MANUAL_TEST.md` に「実機未確認」のチェック項目として残す。このPCは ARM64 なので、ARM64 の実機確認はここでできる
- 補助ツール（`.claude/`）: スキル `codex-prompt`（実装役に渡すプロンプトの作成）、サブエージェント `constraints-reviewer`（「守ること」だけの点検）、`codex/` に実装役の起動（Codex は `run-codex-exec.sh`、Gemini は `run-agy-exec.sh`）とコミット計画の適用のスクリプト（直したら `bash .claude/codex/check-scripts.sh` で回帰を確かめる）、`tools/launch-with-dbg.ps1`（exe を起動してデバッグ出力を表示する。XAML の解析失敗の原因のキーが分かる）

## 実装役（Codex・Gemini）との分担
実装役の割り当て、`/codex-prompt`、PR を作るときは `.claude/guides/implementers.md` を読む。
