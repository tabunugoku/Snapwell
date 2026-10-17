# Snapwell

Windows 11 用のウィンドウマネージャー。画面のグリッド分割、間隔つきの整列、区画単位のリサイズに加えて、投げたウィンドウが物理モデル（ばね・磁石・壁）で区画や四隅に吸い込まれるスナップを持つ。

設計は `docs/DESIGN.md`。最初に読むこと。競合調査は `reports/competitive-analysis.md`（ローカルだけにあり、Git には入れない）。

## 現在のタスク
基本設計とモックアップ（`docs/mockups/snapwell.html`。`.claude/launch.json` の `mockups` で表示する）まで完了。コードはまだ無い。次は技術検証（`docs/DESIGN.md` §15）と、Core の物理モデル・グリッド計算の実装。

## 守ること
- **x64 と ARM64 のネイティブだけ**: ビルド対象は `win-x64` と `win-arm64`。x86（`win-x86`）や AnyCPU の成果物は作らない。どの変更も両方の RID でビルドが通ること。ソリューションの `Any CPU` 構成は開発とテストのためだけに残す（x86・x64 の構成は置かない）。出荷する成果物は必ず `-r win-x64` / `-r win-arm64` で作る
- **DLL インジェクションをしない**: フックは `SetWinEventHook(WINEVENT_OUTOFCONTEXT)`、`WH_KEYBOARD_LL`、`WH_MOUSE_LL` だけ。`SetWindowsHookEx` のグローバルな in-context フック、`CreateRemoteThread`、他プロセスのメモリの読み書きは使わない（対象のアプリのアーキテクチャに左右されないため）
- **非公開 API を使わない**: 仮想デスクトップの非公開 COM（`IVirtualDesktopManagerInternal` など）、文書化されていないエクスポートや序数での呼び出しは使わない。使うしかない場合は、先にユーザーに確認する
- **OS の設定を変えたら必ず戻す**: 標準スナップの停止などで OS の設定を変えるときは、変更前の値を状態ファイルに退避し、無効化・終了・異常終了後の起動のどれでも復元する。変えてよい設定は `docs/DESIGN.md` §8 にあるものだけ
- **アニメーションは物理モデルから**: 移動の動きは質量・剛性・減衰から計算する。継続時間（ms）やイージング曲線を固定値で書かない（feel と同じ方針。`docs/DESIGN.md` §5）
- **フックのコールバックはすぐ返す**: 低レベルフックの中で重い処理・同期の待ち・ウィンドウの移動をしない。処理は別のスレッドに渡す（遅いと OS にフックを外されるため）
- **座標は物理ピクセル**: プロセスは Per-Monitor DPI Aware v2。ウィンドウの見た目の矩形は `DWMWA_EXTENDED_FRAME_BOUNDS` で取り、見えない枠を補正する
- **外部通信なし**: 解析・トラッキング・自動送信をしない。クラウド同期は、利用者が選んだ同期フォルダへのファイルの読み書きで行う（`docs/DESIGN.md` §12）
- **公開してはいけないもの**: `reports/` と `research_notes/` は Git に入れない（`.gitignore` 済み）。スクリーンショットやモックアップに、実在の個人のアカウント名やファイル名を写さない
- **サードパーティのコード**: feel（diip3sh/feel、MIT）のアルゴリズムを移植したときは、著作権表示を `THIRD_PARTY_NOTICES.md` に入れる

## フォルダの構成（予定）
- `src/Snapwell.Core/`: 物理モデル、グリッドと間隔の計算、整列、設定のモデル。Win32 に依存しない（単体テストの対象）
- `src/Snapwell.Platform/`: Win32 との接続。フック、ウィンドウの列挙と移動、モニター、DWM、標準スナップの制御、スタートアップ
- `src/Snapwell.App/`: トレイ常駐、オーバーレイ（ランチャー、区画のプレビュー）、設定画面（WinUI 3）
- `tests/Snapwell.Core.Tests/`: Core の単体テスト（xUnit）
- `docs/`: 設計（`DESIGN.md`）、モックアップ（`mockups/`）、実機での確認（`MANUAL_TEST.md`）

## 技術スタック
- C# / .NET 9 以降、NativeAOT。Win32 の呼び出しは CsWin32 で生成する
- 設定画面は WinUI 3（Windows App SDK）。オーバーレイは Win32 のレイヤードウィンドウと DirectComposition
- テストは xUnit。Win32 に触る部分は、インターフェースを挟んで Core から切り離し、Core だけを単体テストする

## コードとコメントの書き方
Codex と Claude Code のどちらが書いても、同じ見た目にそろえる。

- **整形**: `.editorconfig` に従う（4 スペース、file-scoped namespace、`nullable enable`）。`dotnet format --verify-no-changes` が通ること。1 行はおよそ 120 字まで
- **名前**: 型・メソッドは PascalCase、ローカル変数は camelCase、private フィールドは `_camelCase`。物理の記号（`m`、`k`、`c`、`zeta`）は、式をそのまま写すところだけ短い名前を許す
- **コメントは日本語**: 新しく書くコメントは日本語の常体（短い体言止めまたは「〜する」）。識別子・API 名はそのまま
- **コメントの中身**: 「なぜそうするか」「前提・制約・落とし穴」を書く。Win32 や DWM の挙動への推測は「実機未確認」と書く。公開する型とメソッドには 1 行の `/// <summary>` を書く
- **無駄を足さない**: 使われないコード、コメントアウトした古いコード、「念のため」の分岐を残さない。既存のコードの整形し直しは、機能の変更と同じコミットに入れない
- **確認**: push の前に次を通す。ソリューションは RID を付けてビルドできない（NETSDK1134）ので、RID ごとのビルドは出荷するプロジェクト（いまは `src/Snapwell.Core`、App ができたら `src/Snapwell.App`）に対して行う
  - `dotnet build src/Snapwell.Core/Snapwell.Core.csproj -c Release -r win-x64 --no-restore`
  - `dotnet build src/Snapwell.Core/Snapwell.Core.csproj -c Release -r win-arm64 --no-restore`
  - `dotnet test --no-restore`
  - `dotnet format --verify-no-changes --no-restore`

## 進め方
- 1 項目 1 コミット。各コミットでビルド（両 RID）とテストが通る状態を保つ。main へのマージは、ユーザーが指示したときだけ
- 実機で確かめられないこと（フック、DWM、マルチモニター、スリープ復帰、ARM64 実機）は、`docs/MANUAL_TEST.md` に「実機未確認」のチェック項目として残す。このPCは ARM64 なので、ARM64 の実機確認はここでできる
- 補助ツール（`.claude/`）: スキル `codex-prompt`（Codex に渡すプロンプトの作成）、サブエージェント `constraints-reviewer`（「守ること」だけの点検）、`codex/` に Codex の起動とコミット計画の適用のスクリプト

## Codex との分担
Codex もこのファイルを読む（`run-codex-exec.sh` が `project_doc_fallback_filenames` で渡す）。

- **役割**: Claude Code（Opus 5.5）が統括する。設計、モックアップ、Codex へのプロンプト、レビュー、品質の確認、コミット、リリースを受け持つ。Codex（`gpt-6.1-sol`）は実装を受け持ち、渡されたプロンプトの範囲だけを変える
- **人の手を介さない**: Codex への指示は Codex CLI（`codex exec`）で渡し、承認を求めない設定（`approval_policy="never"`、サンドボックスは `workspace-write`）で最後まで動かす。ユーザーへの確認が要るのは、main へのマージ、リリース、「守ること」の例外だけ
- **料金**: Claude と ChatGPT のサブスクの範囲で動かす。`ANTHROPIC_API_KEY`・`OPENAI_API_KEY` は使わない。Codex は ChatGPT のログイン（`codex login`）で動かす
- **Codex が使えないとき**: ChatGPT の利用制限などで Codex が動かないときは、Opus が同じプロンプトの範囲で実装してよい。そのときはコミットのメッセージに書かず、PR の説明に「Codex の制限のため Opus が実装」と書く
- **流れ**:
  1. 要件の確認（`docs/DESIGN.md`、必要ならモックアップ）
  2. `git worktree add ../Snapwell-wt/<名前> -b codex/<名前> origin/main` で作業のフォルダを作り、そこで `dotnet restore` をしておく（`Directory.Build.props` の `RuntimeIdentifiers` で x64 と ARM64 の両方を復元する。`-r` を付けると片方だけになるので付けない。Codex のサンドボックスはネットワークが止められ、NuGet から取れないため）
  3. `/codex-prompt` でプロンプトを書く。項目ごとに触ってよいファイルを決め、項目の間でファイルを重ねない。各項目に「受け入れ条件（テストで確かめる）」と「競合・境界の観点」を書き、最後の応答をコミット計画の JSON にさせる
  4. `.claude/codex/run-codex-exec.sh <worktree> <プロンプト> <出力フォルダ>` を、Bash の `run_in_background` で起動する。終わると通知が 1 回届くので、待つ間に状態を見に行かない（ポーリングしない）
  5. 出力フォルダの `last.json`（コミット計画・チェックの結果・notes・manual_test）と差分を読み、ローカルでビルド（両 RID）・テスト・format を通す
  6. `node .claude/codex/apply-commit-plan.mjs <worktree> <出力フォルダ>/last.json` で、項目ごとにコミットする（先に `--dry-run` で確かめる）。複数の項目が触ったファイルや、計画に無い変更があれば止まるので、そのときは手で分ける。push して PR を作る
  7. `/codex:adversarial-review` とサブエージェント `constraints-reviewer` で見る。フォーカスの文に「指摘ごとに、マージを止めるものか後回しでよいものかを書く」を入れる
  8. マージを止める指摘だけを直す（やり取りは 2 往復まで）。後回しの指摘は `docs/MANUAL_TEST.md` か、次の作業の候補に回す。採るかどうかの最後の判断は Opus が行う
  9. main へのマージ（ユーザーが指示したときだけ）→ worktree とブランチを消す
- **コミットと push は Opus が行う**: Codex のサンドボックスでは `.git` が書けず、ネットワークも止められている。プロンプトには「コミットと push はしない」と書く。サンドボックスは緩めない
- **並行**: 互いに触るファイルが重ならない作業だけを並べる。最大 2 つから始める。作業ごとに別の worktree とブランチにする。完了の通知を受けてから次へ進む
- **後片付け**: `run-codex-exec.sh` は、終わったあとに worktree を参照するプロセスの数（`leftover=`）を出す。0 でなければ、その worktree を参照するプロセスだけを止める（dotnet のビルドサーバー `VBCSCompiler` や `MSBuild` が残りやすい）
- **サンドボックスでの dotnet**（core-foundation で確認）: MXC サンドボックスの中では `dotnet build` は通るが、`dotnet test` と `dotnet format` は止まる（名前付きパイプへの接続が拒否される）。Codex にはビルドまでを確かめさせ、テストと format は Opus がローカルで実行する。プロンプトでは test と format を `not_run` にしてよいと書く
- **Windows の注意**: Git Bash から `cmd.exe /d /c` などを渡すと、MSYS が `/d` をパスに変える。`codex sandbox` で試すときは PowerShell から呼ぶ
