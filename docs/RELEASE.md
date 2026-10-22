# リリース手順

## 自動化されているもの
- `.github/workflows/ci.yml`: main への push と PR で、format → テスト → App・Settings の両 RID（win-x64・win-arm64）のビルドを `windows-latest` で行う
- `.github/workflows/release.yml`: `v*` のタグを push すると、両 RID を NativeAOT で publish し、`Snapwell-<版>-<rid>.zip`（と `.sha256`）を**下書き**のリリースに添付する。本体と設定画面は同じフォルダーに入る
- `.github/dependabot.yml`: GitHub Actions と NuGet の更新を月 1 回 PR にする

## 出す手順
1. `docs/MANUAL_TEST.md` の「実機未確認」を見て、出す版で確かめ終えるものを確かめる（ARM64 はこの PC、x64 は Prism または実機）
2. main が CI で緑であることを確かめる
3. タグを打って push する: `git tag v0.1.0 && git push origin v0.1.0`
4. Actions の `Release` が終わると、下書きのリリースができる。zip を展開して起動を確かめ、リリースノートを直して公開する
5. 試すだけのときは、Actions の `Release` を `workflow_dispatch`（version を入力）で実行する。リリースは作らず、アーティファクトに zip が出る

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
