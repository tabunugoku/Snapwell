# Snapwell

Windows 11 用のウィンドウマネージャー。画面のグリッド分割、間隔つきの整列、区画単位のリサイズに加えて、投げたウィンドウが物理モデル（ばね・磁石・壁）で区画や四隅に吸い込まれるスナップを持つ。

設計は `docs/DESIGN.md`。最初に読むこと。競合調査は `reports/competitive-analysis.md`（ローカルだけにあり、Git には入れない）。

## 現在のタスク
基本設計、モックアップ（`docs/mockups/snapwell.html`。`.claude/launch.json` の `mockups` で表示する）、技術検証（`docs/SPIKES.md`）、Core（物理モデル・グリッド・吸着先の選択）、Platform（モニター・ウィンドウの移動・角丸・標準スナップ・フック）、App の常駐部分（トレイ、起動と終了、ドラッグの吸着、ホットキー）、オーバーレイ（ランチャー、区画のプレビュー）、設定画面（WinUI 3）、ウィンドウ整列（現在のモニター、4 方式）まで main に入り、ARM64 実機での確認も済んだ。いまは、隣接吸着、整列の続き（全モニター、トレイ、キー割り当て）、設定画面の見た目をモックアップに合わせる作業を進めている。残りの項目は `docs/DESIGN.md` の「未実装」と `docs/MANUAL_TEST.md` の「実機未確認」を見ること。

## 守ること
- **x64 と ARM64 のネイティブだけ**: ビルド対象は `win-x64` と `win-arm64`。x86（`win-x86`）や AnyCPU の成果物は作らない。どの変更も両方の RID でビルドが通ること。ソリューションの `Any CPU` 構成は開発とテストのためだけに残す（x86・x64 の構成は置かない）。出荷する成果物は必ず `-r win-x64` / `-r win-arm64` で作る
- **DLL インジェクションをしない**: フックは `SetWinEventHook(WINEVENT_OUTOFCONTEXT)`、`WH_KEYBOARD_LL`、`WH_MOUSE_LL` だけ。`SetWindowsHookEx` のグローバルな in-context フック、`CreateRemoteThread`、他プロセスのメモリの読み書きは使わない（対象のアプリのアーキテクチャに左右されないため）
- **非公開 API を使わない**: 仮想デスクトップの非公開 COM（`IVirtualDesktopManagerInternal` など）、文書化されていないエクスポートや序数での呼び出しは使わない。使うしかない場合は、先にユーザーに確認する
- **OS の設定を変えたら必ず戻す**: 標準スナップの停止などで OS の設定を変えるときは、変更前の値を状態ファイルに退避し、無効化・終了・異常終了後の起動のどれでも復元する。変えてよい設定は `docs/DESIGN.md` §8 にあるものだけ
- **例外: サインイン時の自動起動**（ユーザーが認めた例外）: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` とタスク スケジューラの `\Snapwell` への登録は、自動で起動するための設定なので、終了しても消さない。登録するのは利用者が設定画面でオンにしたときだけで、オフにしたら消す（`docs/DESIGN.md` §11）。開発中のビルドで登録するのは試験のためだけで、ユーザーの指示があったときに限る。試験が終わったら必ず登録を消し、Run の値の削除と `schtasks /Query /TN Snapwell` でタスクが消えたことを確かめる。テストでは偽のレジストリと偽のタスク サービスを使い、実際のレジストリとタスク スケジューラに書かない
- **アニメーションは物理モデルから**: 移動の動きは質量・剛性・減衰から計算する。継続時間（ms）やイージング曲線を固定値で書かない（feel と同じ方針。`docs/DESIGN.md` §5）
- **フックのコールバックはすぐ返す**: 低レベルフックの中で重い処理・同期の待ち・ウィンドウの移動をしない。処理は別のスレッドに渡す（遅いと OS にフックを外されるため）
- **座標は物理ピクセル**: プロセスは Per-Monitor DPI Aware v2。ウィンドウの見た目の矩形は `DWMWA_EXTENDED_FRAME_BOUNDS` で取り、見えない枠を補正する
- **外部通信なし**: 解析・トラッキング・自動送信をしない。クラウド同期は、利用者が選んだ同期フォルダへのファイルの読み書きで行う（`docs/DESIGN.md` §12）
- **公開してはいけないもの**: `reports/` と `research_notes/` は Git に入れない（`.gitignore` 済み）。スクリーンショットやモックアップに、実在の個人のアカウント名やファイル名を写さない
- **サードパーティのコード**: feel（diip3sh/feel、MIT）のアルゴリズムを移植したときは、著作権表示を `THIRD_PARTY_NOTICES.md` に入れる

## フォルダの構成（予定）
- `src/Snapwell.Core/`: 物理モデル、グリッドと間隔の計算、整列、設定のモデル。Win32 に依存しない（単体テストの対象）
- `src/Snapwell.Platform/`: Win32 との接続。フック、ウィンドウの列挙と移動、モニター、DWM、標準スナップの制御、スタートアップ
- `src/Snapwell.App/`: トレイ常駐、オーバーレイ（ランチャー、区画のプレビュー）
- `src/Snapwell.Settings/`: 設定画面（WinUI 3、パッケージ化しない別の exe。Windows App SDK は自己完結で同梱）。Win32 と同じく、テストする判断は Core に置く
- `tests/Snapwell.Core.Tests/`: Core の単体テスト（xUnit）
- `docs/`: 設計（`DESIGN.md`）、モックアップ（`mockups/`）、実機での確認（`MANUAL_TEST.md`）

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
- **NativeAOT の publish**: `dotnet build -r` では AOT のコンパイルは走らない。出荷物は `dotnet publish src/Snapwell.App/Snapwell.App.csproj -c Release -r win-x64`（と `win-arm64`）で作る。Visual Studio の C++ ツール（x64 と ARM64 の両方）が要る。Git Bash からは `ProgramFiles(x86)` が渡らず `vswhere.exe` が見つからないので、PowerShell から、`${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer` を PATH に足して実行する（このPCで両 RID とも確認済み）

## 進め方
- 1 項目 1 コミット。各コミットでビルド（両 RID）とテストが通る状態を保つ。main へのマージは、ユーザーが指示したときだけ
- 実機で確かめられないこと（フック、DWM、マルチモニター、スリープ復帰、ARM64 実機）は、`docs/MANUAL_TEST.md` に「実機未確認」のチェック項目として残す。このPCは ARM64 なので、ARM64 の実機確認はここでできる
- 補助ツール（`.claude/`）: スキル `codex-prompt`（実装役に渡すプロンプトの作成）、サブエージェント `constraints-reviewer`（「守ること」だけの点検）、`codex/` に実装役の起動（Codex は `run-codex-exec.sh`、Gemini は `run-agy-exec.sh`）とコミット計画の適用のスクリプト

## 実装役（Codex・Gemini）との分担
実装役もこのファイルを読む（`run-codex-exec.sh` は `project_doc_fallback_filenames` で、`run-agy-exec.sh` はプロンプトの前に付ける `agy-preamble.md` で読ませる）。

- **役割**: Claude Code（Opus 5.5）が統括する。設計、モックアップ、実装役へのプロンプト、実装役の割り当て、レビュー、品質の確認、コミット、リリースを受け持つ。通常の実装は Codex（Codex CLI、`gpt-6.1-sol`）が行う。Gemini（Antigravity CLI の `agy`、`gemini-3.8-flash-high`）は、単純な変更・文字列の変更・文書・機械的な小さい作業だけを受け持つ。どちらも、渡されたプロンプトの範囲だけを変える
- **実装役の選び方**: 作業ごとに Opus が決める。決めた実装役とモデル、選んだ理由を PR の説明に 1 行で書く。目安:
  - まず、使えるかを見る（利用制限、直前の失敗）。使えないほうには割り当てない
  - Codex: 既定。通常の実装はすべて Codex（機能の追加、設計に関わる変更、項目が多い作業、Platform・App の Win32 の相互運用、描画・性能の作業、長くかかる作業）。サンドボックスの中で `dotnet build` まで確かめた実績が多い
  - Gemini 3.8 Flash（`gemini-3.8-flash-high`）: 単純な変更・文字列（文言、ラベル、リソース）の変更・名前の変更・文書・テストの追加などの、機械的な小さい作業だけ。判断や設計を要する作業、複数の層にまたがる作業、描画・性能の作業は割り当てない。agy はシェルで `dotnet build` しか実行できない
  - Gemini 4 Argon（`gemini-4-argon-high`）は使わない（利用枠を使い切り、実装では途中で止まったため）。ユーザーが指示したときだけ使う
  - 前の作業の結果（ビルドの失敗、レビューでマージを止めた指摘の数、かかった時間）を見て、次の割り当てを変えてよい（Gemini を通常の実装に広げるのは、ユーザーが認めたときだけ）
- **人の手を介さない**: Codex には Codex CLI（`codex exec`）で、承認を求めない設定（`approval_policy="never"`、サンドボックスは `workspace-write`）で渡す。Gemini には `agy -p` で、`--mode accept-edits`（ファイルの編集だけを自動で許可）と `--sandbox` で渡す。agy のコマンドの許可は、`~/.gemini/antigravity-cli/settings.json` の `permissions.allow` の `command(regex:dotnet build .*)` だけ（`command(dotnet build)` の形は Windows では効かなかった）。許可の外のコマンドを出すと、agy はその時点で何も返さずに終わるので、`agy-preamble.md` でシェルのコマンドを `dotnet build` だけに限らせる。許可を広げない。`--dangerously-skip-permissions` は使わない。ユーザーへの確認が要るのは、main へのマージ、リリース、「守ること」の例外、実装役が両方とも使えないときの進め方だけ
- **料金**: Claude、ChatGPT、Google のサブスクの範囲で動かす。`ANTHROPIC_API_KEY`・`OPENAI_API_KEY`・`GEMINI_API_KEY` は使わない。Codex は ChatGPT のログイン（`codex login`）、agy は Google アカウントのログイン（`oauth-personal`。ユーザーが対話モードで済ませたもの）で動かす
- **実装役が使えないとき**: Codex が利用制限などで動かないときは、通常の実装を Gemini に回さない。制限の解除を待つ（制限が解ける時刻は出力にある）。待つ間は、Gemini に単純な作業だけを割り当てるか、Opus が設計・レビューなどを進める。Codex の制限中に通常の実装を進めたいときは、自動で切り替えず、ユーザーに、制限のリセットを待つか、Gemini Flash または Opus で実装を続けるかを確認する。Gemini が使えないときは、単純な作業を Codex に回してよい（確認は要らない）。両方とも使えないときも、自動で Opus の実装に切り替えない。ユーザーに、いったん開発を止めて制限のリセットを待つか、Opus で実装を続けるかを確認する（制限のリセットを待つほうがよい場合があるため）。確認するときは、止まった作業、制限が解ける時刻（出力にあれば）、残りの項目を示す。Opus で続けると決まったときは、同じプロンプトの範囲で実装し、コミットのメッセージには書かず、PR の説明に「実装役の制限のため Opus が実装」と書く
- **流れ**:
  1. 要件の確認（`docs/DESIGN.md`、必要ならモックアップ）
  2. 実装役を決め、`git worktree add ../Snapwell-wt/<名前> -b <codex か gemini>/<名前> origin/main` で作業のフォルダを作り、そこで `dotnet restore` をしておく（`Directory.Build.props` の `RuntimeIdentifiers` で x64 と ARM64 の両方を復元する。`-r` を付けると片方だけになるので付けない。実装役は `dotnet restore` を実行しない・できないため）
  3. `/codex-prompt` でプロンプトを書く。項目ごとに触ってよいファイルを決め、項目の間でファイルを重ねない。各項目に「受け入れ条件（テストで確かめる）」と「競合・境界の観点」を書き、最後の応答をコミット計画の JSON にさせる
  4. Codex なら `.claude/codex/run-codex-exec.sh <worktree> <プロンプト> <出力フォルダ>`、Gemini なら `.claude/codex/run-agy-exec.sh <worktree> <プロンプト> <出力フォルダ> [モデル]` を、Bash の `run_in_background` で起動する。終わると通知が 1 回届くので、待つ間に状態を見に行かない（ポーリングしない）。`run-agy-exec.sh` は、拒否されたコマンド（`denied=`）があったときや結果の JSON が無いときに 0 以外で終わる
  5. 出力フォルダの `last.json`（コミット計画・チェックの結果・notes・manual_test）と差分を読み、ローカルでビルド（両 RID）・テスト・format を通す
  6. `node .claude/codex/apply-commit-plan.mjs <worktree> <出力フォルダ>/last.json` で、項目ごとにコミットする（先に `--dry-run` で確かめる）。複数の項目が触ったファイルや、計画に無い変更があれば止まるので、そのときは手で分ける。push して PR を作る
  7. `/codex:adversarial-review` とサブエージェント `constraints-reviewer` で見る。フォーカスの文に「指摘ごとに、マージを止めるものか後回しでよいものかを書く」を入れる。Gemini が実装した作業も、レビューは同じ手順で行う（実装したのと別のモデルに見せるため）。敵対的レビューは Codex の枠を使うので、Codex の制限中はレビューを後ろにずらし、制限の解除後にまとめて行う
  8. マージを止める指摘だけを直す（やり取りは 2 往復まで。直すのは、原則としてその作業を実装した実装役）。後回しの指摘は `docs/MANUAL_TEST.md` か、次の作業の候補に回す。採るかどうかの最後の判断は Opus が行う
  9. main へのマージ（ユーザーが指示したときだけ）→ worktree とブランチを消す
- **コミットと push は Opus が行う**: Codex のサンドボックスでは `.git` が書けず、ネットワークも止められている。agy は `git` のコマンドを許されていない。プロンプトには「コミットと push はしない」と書く。サンドボックスと許可は緩めない
- **並行**: 互いに触るファイルが重ならない作業だけを並べる。最大 2 つから始める（Codex と Gemini に 1 つずつ割り当ててもよい）。作業ごとに別の worktree とブランチにする。完了の通知を受けてから次へ進む
- **後片付け**: 起動のスクリプトは、終わったあとに worktree を参照するプロセスの数（`leftover=`）を出す。0 でなければ、その worktree を参照するプロセスだけを止める（dotnet のビルドサーバー `VBCSCompiler` や `MSBuild` が残りやすい）
- **サンドボックスでの dotnet**（core-foundation で確認）: Codex の MXC サンドボックスの中では `dotnet build` は通るが、`dotnet test` と `dotnet format` は止まる（名前付きパイプへの接続が拒否される）。agy は `dotnet build` だけが許されている。どちらの実装役にもビルドまでを確かめさせ、テストと format は Opus がローカルで実行する。プロンプトでは test と format を `not_run` にしてよいと書く
- **Windows の注意**: Git Bash から `cmd.exe /d /c` などを渡すと、MSYS が `/d` をパスに変える。`codex sandbox` で試すときは PowerShell から呼ぶ。agy は Claude Code の Bash から動かす（PowerShell のツールはサンドボックスの中で動き、`%LOCALAPPDATA%` への書き込みが外に出ないことがあった）。agy のプロンプトはコマンドラインの引数で渡すので、30000 字を超えるプロンプトは分ける
