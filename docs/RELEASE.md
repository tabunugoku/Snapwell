# リリース手順

## 自動化されているもの
- `.github/workflows/ci.yml`: main への push と PR で、format → テスト → App・Settings の両 RID（win-x64・win-arm64）のビルドを `windows-latest` で行う
- `.github/workflows/release.yml`: `v*` のタグを push すると、両 RID を NativeAOT で publish し、`Snapwell-<版>-<rid>.zip`（と `.sha256`）を**下書き**のリリースに添付する。本体と設定画面は同じフォルダーに入る
- `.github/dependabot.yml`: GitHub Actions と NuGet の更新を月 1 回 PR にする

## 出す手順
1. `docs/MANUAL_TEST.md` の「実機未確認」を見て、出す版で確かめ終えるものを確かめる（ARM64 はこの PC、x64 は Prism または実機）
2. main が CI で緑であることを確かめる
3. `release/summary.json` の日本語・英語の要約を今回の変更点に書き換える。必要なら `minimumSupported` にサポートする最も古い版を入れる（指定しないときは `null`）。変更をコミットしてから、タグを打って push する: `git tag v<版> && git push origin v<版>`（版はタグから決まる。版を書いたファイルは無い）
4. Actions の `Release` が終わると、下書きのリリースができる。zip を展開し、`Snapwell.Settings.exe` を x64（ARM64 PC ではエミュレーション）・ARM64 とも起動して確かめる（壊れた版を公開すると更新の案内が出るため、確かめるまで `--draft=false` にしない）。リリースノート（英語を表に、日本語を `<details>` で畳む）は `gh release edit <tag> --title "Snapwell <版>" --notes-file <ファイル>` で先に入れてから、`gh release edit <tag> --draft=false --latest` で公開する（順序を逆にすると自動生成の本文のまま公開される）。公開すると `pages.yml` が main で再実行され、ホームページの版が追従する
5. 試すだけのときは、Actions の `Release` を `workflow_dispatch`（version を入力）で実行する。リリースは作らず、アーティファクトに zip が出る

## 更新の確認用ファイル

タグによるリリースでは、両 RID の zip と `.sha256` に加えて `latest.json` と `latest.json.sig` を下書きに添付する。`latest.json` は版、UTC の生成時刻、日英の要約、リリースノートの URL、両 RID のダウンロード URL・SHA256・バイト数、任意の `minimumSupported` を含む。`release/summary.json` の文言は雛形なので、リリースのたびに書き換えてからタグを打つ。

署名は JSON のバイト列そのものに対する ECDSA P-256・SHA-256 で、`latest.json.sig` は IEEE P1363 の 64 バイトを Base64 の 1 行にしたものとする。署名後に JSON の空白や改行も変更しない。この署名は更新情報用であり、exe のコード署名は別途必要になる。

初回だけ、リポジトリの外の一時フォルダーで鍵を作る。以下は PowerShell の例で、既存の鍵ファイルは上書きしない。

```powershell
dotnet run --project tools/ManifestTool -- keygen --private "$env:TEMP/update-private.txt" --public "$env:TEMP/update-public.txt"
```

秘密鍵ファイルの内容（PKCS#8 DER の Base64）を、GitHub の Settings → Environments → `release` の environment secret `UPDATE_SIGNING_KEY` に登録し、手元の秘密鍵ファイルを消す。秘密鍵をリポジトリやログ、成果物に入れない。environment `release` には、Required reviewers（承認者。メンテナー自身）と、Deployment branches and tags を `v*` のタグだけに制限する設定を付ける。タグを打っただけでは release ジョブが始まらず、承認するまで鍵が渡らない。公開鍵（SPKI DER の Base64）は `release/update-public-key.txt` にコミットする（公開してよい。アプリがこのファイルを埋め込む）。実際の秘密鍵ファイルはコミットしない。

秘密鍵を失うと、同じ公開鍵で検証できる署名を作れなくなる。新しい鍵に切り替えても古いアプリは新しい公開鍵を持たないため、更新の案内が止まる。利用者には新しい公開鍵を含む版を手動で再インストールしてもらう必要がある。鍵を変更する場合も同様になる。

Actions では秘密鍵を署名の手順だけに渡す。未登録なら処理を失敗させ、署名なしの `latest.json` は添付しない。署名の手順は、秘密鍵から導いた公開鍵が `release/update-public-key.txt` と一致しなければ失敗し、添付前にその公開鍵ファイルでもう一度検証する（別の有効な鍵への差し替えや誤設定を、配布の前に止める）。手元でも `sign --key-env <環境変数名> --in <latest.json> --out <latest.json.sig>` と `verify --public <公開鍵ファイルかBase64> --in <latest.json> --sig <latest.json.sig>` を使える。`sign` の標準出力は導いた公開鍵の Base64 だけになる。生成時刻を固定して試すときは `generate` に `--published-at 2026-10-23T00:00:00Z` を渡す。

確認先は `https://github.com/tabunugoku/Snapwell/releases/latest/download/latest.json`（署名は同じ場所の `latest.json.sig`）とする。公開済みの最新の正式版にだけ転送され、下書きのうちは確認先に出ない。`v1.0.0-rc.1` のようなタグも生成・署名できるが、公開時に GitHub の **pre-release** として指定すること。GitHub の latest は prerelease を除外するため、この確認先には出ない。

## 実機で確かめる項目（優先順）
v1.0.0〜v1.0.6 は未署名のまま公開した。このうち ARM64 は、導入・ようこそ・四隅・スリープとロックからの復帰を確認済みで、4 言語の画面の収まりと `LowLevelHooksTimeout` まわりも手元のビルドで問題なしと確認した（2026-11-08）。x64 実機・DPI・複数モニターなどは薄い。`docs/MANUAL_TEST.md` は全件を公開の条件にしない。次の項目を、確かめ終えるほど優先して消し、確かめたら MANUAL_TEST の該当の項目を `[x]` にして日付と環境を書く。OS の設定を変える項目は、同意を得てから行い、終わったら戻す。

1. **OS の設定の復元**（最優先）: 「Platform: 標準スナップ」の元がオン・オフの両方、正常終了と強制終了後の次回起動での復元、状態ファイルが壊れたとき。「標準機能ページ」の「標準の動作に戻す」。
2. **入力フック**: 「Platform: フック」の登録・解除・別スレッドからの停止、ホットキーの握りつぶし、Win キーを含む割り当てでスタートメニューが開かないこと、「Win + 矢印の置き換え」の連打、別の低レベルフックのアプリとの同時動作、ペン入力での誤検知。
3. **自動起動**: サインイン時の自動起動のオン・オフと、試験後の Run とタスクの削除の確認（`docs/DESIGN.md` §11）。「管理者として起動」は、タスク スケジューラの登録・UAC のキャンセル・設定画面が非昇格で開くこと・短い名前（8.3）と `%` を含むパスの拒否。
4. **基本の操作**: 複数モニター、リモート接続・切断、アプリごとのルール（管理者権限・UWP のプロセス名を含む）。
5. **x64**: x64 の NativeAOT の成果物で、上の主な項目（起動・フック・設定画面・ウィザード・復元）。CI に x64 の実行テストが無いため、Prism 上か実機で行う。
6. **DPI とテーマ**: 100%・150%・200%、ライト・ダーク・高コントラスト（設定画面・ウィザード・トレイ・ランチャー）。

後回しにしてよいもの: 代理描画とブラウザ・Office・Electron のちらつき、120 Hz 以上での `DwmFlush`、設定の同期の細かい失敗パターン、見た目のモックアップ比較の細部。

## まだ実装していない機能
- モニター構成ごとの配置の保存と復元（`docs/DESIGN.md` §10.3）
- トレイメニューのプロファイルの切り替え（同 §16 のトレイメニュー）
- ピクチャー イン ピクチャーのクラス・タイトルによる自動判定（利用者がアプリごとのルールの「位置だけ」で指定する形は実装済み。同 §10.4・§15 の項目 8）

## 配布の方針
- **署名**: 未署名のため、ダウンロードすると SmartScreen の警告が出る。SignPath Foundation は 2026-11-06 に「公開の知名度が低い」として却下された。利用者が増えてから再申請し、承認後に `Package` の前へ exe だけに署名する手順を足す（zip と更新の仕組みは変えない）。
- **MSIX・winget は作らない**。ポータブル版の zip だけを出す。
- **ARM64 の実行テスト**: CI は ARM64 をビルドまでしか確かめない（実行は実機で確かめる）。
- **ライセンス**: `LICENSE` を置いてある（`THIRD_PARTY_NOTICES.md` は zip に入る）。

## ホームページ（GitHub Pages）
- `site/` が公開元。main の `site/**` の変更と、リリースの公開で `.github/workflows/pages.yml` が自動で公開する (手動の実行もできる)。リリースのイベントはタグの ref で動き、github-pages 環境がタグを許さず失敗するため、`dispatch` ジョブが `workflow_dispatch` で main から再実行を頼む。URL は `https://tabunugoku.github.io/Snapwell/`
- リリースで対応言語が変わるときは、`site/index.html` の機能 08 の文言を 4 言語（`data-l` が en・ja・zh-hans・zh-hant）とも直す
- 初回だけ、リポジトリの Settings → Pages の Source を「GitHub Actions」にする
- リポジトリは公開済みなので、プランの制約はない
- `site/index.html` の版は `{{VERSION}}` と書き、公開の直前に、最新の正式版（下書きと pre-release を除く）の版へ置き換える。ダウンロードのボタンは `releases/download/v<版>/Snapwell-<版>-win-<rid>.zip` を直接指す。リリースを下書きから公開に切り替えると、ページも自動で追従する
- 動きは feel のランタイムで動かしている（`site/js/feel.js`、`THIRD_PARTY_NOTICES.md` に記載）。`transition` や `cubic-bezier` を足さない
- 紹介動画（`site/video/`、日本語・英語・簡体字・繁体字の 4 版）は `tools/film/` で作る。feel のモデルを偽の時計で 1 コマずつ進めて撮る（実時間に依存しないので、毎回同じ映像になる）。作り直すときは `cd tools/film && npm install && node render.mjs --lang en`（と `--lang ja`。ffmpeg と Chrome か Edge が要る）。効果音と BGM を `audio/mix.mjs` で重ね、できた `snapwell-en.mp4`・`snapwell-en.webm`・`poster-en.jpg` と `-ja` の 3 本を `site/video/` に置く。手順は `tools/film/README.md`。実アプリの録画ではなく、モックアップ相当の画面なので、ページにもそう書いてある
