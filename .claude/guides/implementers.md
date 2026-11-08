# 実装役（Codex・Gemini）との分担

CLAUDE.md から移した。実装役の割り当て、`/codex-prompt`、PR を作るときに読む。

実装役は CLAUDE.md の「守ること」と「コードとコメントの書き方」を読む（`run-codex-exec.sh` は `project_doc_fallback_filenames` で、`run-agy-exec.sh` はプロンプトの前に付ける `agy-preamble.md` で読ませる）。

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
