# リリース手順

## 自動化されているもの
- `.github/workflows/ci.yml`: main への push と PR で、format → テスト → App・Settings の両 RID（win-x64・win-arm64）のビルドを `windows-latest` で行う
- `.github/workflows/release.yml`: `v*` のタグを push すると、両 RID を NativeAOT で publish し、`Snapwell-<版>-<rid>.zip`（と `.sha256`）を**下書き**のリリースに添付する。本体と設定画面は同じフォルダーに入る
- `.github/dependabot.yml`: GitHub Actions と NuGet の更新を月 1 回 PR にする

## 出す手順
1. `docs/MANUAL_TEST.md` の「実機未確認」を見て、出す版で確かめ終えるものを確かめる（ARM64 はこの PC、x64 は Prism または実機）
2. main が CI で緑であることを確かめる
3. `release/summary.json` の日本語・英語の要約を今回の変更点に書き換える。必要なら `minimumSupported` にサポートする最も古い版を入れる（指定しないときは `null`）。変更をコミットしてから、タグを打って push する: `git tag v0.1.0 && git push origin v0.1.0`
4. Actions の `Release` が終わると、下書きのリリースができる。zip を展開して起動を確かめ、リリースノートを直して公開する
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

確認先は `https://github.com/tabunugoku/Snapwell/releases/latest/download/latest.json`（署名は同じ場所の `latest.json.sig`）とする。公開済みの最新の正式版にだけ転送され、下書きのうちは確認先に出ない。`v1.0.0-rc.1` のようなタグも生成・署名できるが、公開時に GitHub の **pre-release** として指定すること。GitHub の latest は prerelease を除外するため、この確認先には出ない。リポジトリが非公開の場合、匿名では取得できない（`docs/DESIGN.md` §17.1）。

## 1.0.0 のブロッカー（実機で確かめる項目）
`docs/MANUAL_TEST.md` は全件（約 380）を 1.0.0 の条件にしない。次の項目を確かめ終えてから出す。確かめたら MANUAL_TEST の該当の項目を `[x]` にする。OS の設定を変える項目は、同意を得てから行い、終わったら戻す。

1. **OS の設定の復元**（OS を壊さないための最優先）: 「Platform: 標準スナップ」の元がオン・オフの両方、正常終了と強制終了後の次回起動での復元、状態ファイルが壊れたとき。「標準機能ページ」の「標準の動作に戻す」。
2. **入力フック**: 「Platform: フック」の登録・解除・別スレッドからの停止、ホットキーの握りつぶし、Win キーを含む割り当てでスタートメニューが開かないこと。「Win + 矢印の置き換え」の、オン・オフ・一時停止と連打。別の低レベルフックのアプリと同時に動くこと。
3. **初回セットアップと自動起動**: 「初回セットアップ」の全項目（新規環境、同意の反映、保存失敗で完了フラグが立たない）。サインイン時の自動起動のオン・オフと、試験後の Run とタスクの削除の確認（`docs/DESIGN.md` §11）。「管理者として起動」は、タスク スケジューラの登録・UAC のキャンセル・設定画面が非昇格で開くこと・短い名前（8.3）と `%` を含むパスの拒否を確かめる。確かめ終えられなければ、1.0.0 では管理者として起動の項目を外すか、既知の制限として明記する。
4. **基本の操作**: 投げて区画に吸い込まれる（`App: 常駐の部分`）、ホットキーでの配置、ランチャー、整列、複数モニター、スリープからの復帰（「共通」）。ホットキーの割り当て（「ホットキーの割り当て」）と、アプリごとのルール（「アプリごとのルール — Platform と App」。管理者権限・UWP のプロセス名を含む）。
5. **多言語**（日本語と英語。「多言語（フェーズ 1〜4）」）: 言語の切り替えが本体の再起動なしで反映されること、Windows の表示言語での「自動」、英語の画面の収まり（設定の最小幅 900、ウィザード 820 × 680、ランチャー）、トレイの状態文、同意の MessageBox（拒否したら Windows の設定を変えない）。トレイの状態欄では、日本語の長い文が「…」で省略されることを確かめ、重要な文が隠れるときは直す。
6. **x64**: x64 の NativeAOT の成果物で、上の 1〜5 の主な項目（起動・フック・設定画面・ウィザード・復元）を確かめる。GitHub の CI に x64 の実行テストが無いため、Prism 上か実機で行う。
7. **DPI とテーマ**: 100%・150%・200%、ライトとダーク（設定画面・ウィザード・トレイ・ランチャー）。

ブロッカーの外（1.0.0 の後でもよい）: 代理描画とブラウザ・Office・Electron のちらつき、120 Hz 以上での `DwmFlush`、設定の同期の細かい失敗パターン、見た目のモックアップ比較の細部。
## 1.0.0 に含めないもの（将来のリリースで実装する）
- モニター構成ごとの配置の保存と復元（`docs/DESIGN.md` §10.3）
- トレイメニューのプロファイルの切り替え（同 §16 のトレイメニュー）
- ピクチャー イン ピクチャーのクラス・タイトルによる自動判定（利用者がアプリごとのルールの「位置だけ」で指定する形は、1.0.0 に含める。同 §10.4・§15 の項目 8）
- 中国語（簡体字・繁体字）の画面。辞書は残してあり、`UiLanguageResolver.ChineseAvailable` を `true` にして、足りない訳を埋めると再開できる（`docs/I18N.md`）
## まだ無いもの（決めることがある）
- **署名**: 未署名のため、ダウンロードすると SmartScreen の警告が出る。コード署名の証明書（または Trusted Signing）が決まったら、`Package` の前に署名の手順を足す
- **MSIX・winget**（`docs/DESIGN.md` の配布方針）: 署名が前提。MSIX にすると `StartupTask` で自動起動を扱える。それまではポータブル版の zip だけを出す
- **LICENSE**: リポジトリは非公開で、ライセンスを置いていない。公開する前に決める（`THIRD_PARTY_NOTICES.md` は zip に入る）
- **ARM64 の実行テスト**: GitHub の ARM64 ランナーは非公開リポジトリでは使えない。CI は ARM64 をビルドまでしか確かめない
- **ランナーの前提**: NativeAOT のリンクには Visual Studio の C++ ツール（x64・ARM64）が要る。`windows-latest` のイメージに入っている前提で、初回の実行で確かめること（入っていなければ手順を足す）

## ホームページ（GitHub Pages）
- `site/` が公開元。`.github/workflows/pages.yml` を手動で実行すると公開する (v1.0.0 のリリースを公開する日まで、自動の公開は止めてある。リリース後に push の節を足せば、main の `site/**` の変更で自動になる)。URL は `https://tabunugoku.github.io/Snapwell/`
- 初回だけ、リポジトリの Settings → Pages の Source を「GitHub Actions」にする
- **非公開のリポジトリから公開するには、GitHub Pro 以上が要る**。無料のプランでは、リポジトリを公開にする必要がある（その場合は、先に LICENSE を決める）
- ダウンロードのボタンは `releases/download/v1.0.0/Snapwell-1.0.0-win-<rid>.zip` を直接指す。v1.0.0 のリリースを公開するまでは 404 になる。公開は、ページと同じ日にそろえる
- 動きは feel のランタイムで動かしている（`site/js/feel.js`、`THIRD_PARTY_NOTICES.md` に記載）。`transition` や `cubic-bezier` を足さない
- 紹介動画（`site/video/`）は `tools/film/` で作る。feel のモデルを偽の時計で 1 コマずつ進めて撮る（実時間に依存しないので、毎回同じ映像になる）。作り直すときは `cd tools/film && npm install && node render.mjs`（ffmpeg と Chrome か Edge が要る）。できた `out/snapwell.mp4`・`snapwell.webm`・`poster.jpg` を `site/video/` に置く。実アプリの録画ではなく、モックアップ相当の画面なので、ページにもそう書いてある
