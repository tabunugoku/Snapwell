# 紹介動画 (tools/film)

ホームページの紹介動画 (30 秒、1920×1080、60 fps) を作る道具。feel の物理モデルを偽の時計で 1 コマずつ進め、画面を撮って ffmpeg でまとめる。
同じ入力なら毎回同じ映像になる (時計は `clock.js`、乱数はシード固定)。

| ファイル | 役割 |
|---|---|
| `film.html` `film.css` `film.js` | 台本。導入 (`site/js/intro.js`)、投げる、操作感のプリセット、ランチャー、Ctrl+Alt+A の整列、終わりのロゴ |
| `render.mjs` | 映像 (`snapwell.mp4` `snapwell.webm` `poster.jpg`) と、効果音のきっかけ `cues.json` を `out/` に書く |
| `brand.mjs` | ロゴとアイコンの素材を 1 つの元から作る (下記) |
| `audio/` | 効果音の台本、生成、重ね合わせ、タイミング表 |

## 映像を作る

要 Node 20 以上、ffmpeg、Chrome か Edge。

```bash
cd tools/film
npm ci
node render.mjs            # out/ に snapwell.mp4, snapwell.webm, poster.jpg, cues.json。1800 コマで数分かかる
```

できた 3 つを `site/video/` に置く。置き場所は従来どおりで、Git LFS は使わない (`.gitattributes` に LFS の指定はなく、各ファイルは 1 MB 前後)。

## ロゴとアイコン (ずれないための決まり)

元は 2 つだけ。

- アプリのアイコン: `assets/icons/app-large.svg`
- ワードマークの書体: `site/fonts/newsreader-500.woff2` (Newsreader、SIL OFL 1.1。`tools/subset-wordmark-font.py` で切り出す。`THIRD_PARTY_NOTICES.md` に記載)

`node brand.mjs` が `site/assets/icon.svg` (app-large)、`favicon.svg` (app-32。ヘッダの 32 px もこれ)、`apple-touch-icon.png`、`og.png`、`site/js/brand-mark.js` を作る。
ホームページのヘッダとヒーロー、動画の導入と終わり、favicon、OGP はこれらだけを使う。アイコンを直したら `node brand.mjs` を走らせて同じコミットに入れる。
`node brand.mjs --check` は PNG 以外が元とずれていないかを見る (ずれていれば終了コード 1)。

## 音を足す (鍵が来たら)

映像は無音で出す。効果音は ElevenLabs の Sound Effects API で作って重ねる。鍵 (API key) はリポジトリに書かず、環境変数 `ELEVENLABS_API_KEY` から読む。

```powershell
cd tools/film
# 映像を作り直したときだけ: node render.mjs のあと out/cues.json を audio/cues.json へ写す (台本の時刻。再描画なしで音を足せる)
node audio/table.mjs                         # audio/TIMING.md を作り直す (鍵は要らない)
node audio/generate-sfx.mjs --dry-run        # 何を何秒で頼むか、クレジットの目安を見る (送らない)
$env:ELEVENLABS_API_KEY = "<鍵>"
node audio/generate-sfx.mjs                  # audio/sfx/*.mp3 を作る (Git には入れない)
node audio/mix.mjs                           # 既定は site/video の無音版と audio/cues.json。out/with-sound/ に音つきの mp4 と webm
```

音つきの 2 本を `site/video/` に置く。いま載せているのは音つき版 (`<video>` に `muted` はなく、注記は `30 seconds, with sound.`)。無音に戻すときは、`render.mjs` で作り直した映像を置き、`muted` を付け、注記を `no sound` にする。mix.mjs は `site/video` の映像を入力にするので、音つきに差し替えたあとは、先に無音の映像を `render.mjs` で作り直してから重ねること。

- 効果音の指示と長さ、きっかけとの対応は `audio/sounds.json`。きっかけの時刻は台本 (`film.js`) と物理の事象 (離した時刻、止まった時刻) から `cues.json` に書かれるので、映像を変えたら `render.mjs` と `audio/table.mjs` を走らせ直せば音のずれは出ない。
- 使うエンドポイント: `POST https://api.elevenlabs.io/v1/sound-generation` (ヘッダ `xi-api-key`、本文 `text` `duration_seconds` (0.5 から 30) `prompt_influence` `model_id`)。2026-10-26 に <https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert> で確認。
- 料金: 長さを指定すると 1 秒あたり 40 クレジット (<https://elevenlabs.io/docs/overview/capabilities/sound-effects>、2026-10-26 確認)。全部で約 10 秒、400 クレジット前後。

### 生成した効果音の利用条件 (2026-10-26 に確認。契約の前にもう一度見ること)

- 利用規約 (<https://elevenlabs.io/terms-of-use>) 第 4 条: 出力 (Output) の権利は利用者が持ち、サービスの外でも使える。第 1(c) 条: **無料プランは非商用に限る。有料プランは商用に使える** (禁止事項の方針は守る)。規約に出力へのクレジット表記の義務は見当たらない。
- 一方、同社の効果音の案内ページ (<https://elevenlabs.io/sound-effects/commercial>) は「無料プランは elevenlabs.io のクレジット表記が要る、有料プランは不要」と書く。規約と案内ページの書き方が違うので、**有料プランで生成する** のが確実 (どちらの読み方でも商用に使え、表記は要らない)。
- 第 4 条(d): 出力は同社がサービスの改善に使える権利を持つ。第 10 条: 出力は他の利用者のものと似ることがある (独占はできない)。
- 生成の記録: 2026-10-26、有料プランの鍵 (ユーザーが確認)、モデル `eleven_text_to_sound_v2`、12 件・指定の長さの合計 10.4 秒 (約 416 クレジット)、`tick` は無音に近かったので指示を変えて 1 件だけ作り直し (約 20 クレジット)。
- 作った音は公開前に聞いて確かめる (第 4 条(k) は出力を確認する責任が利用者にあるとする)。生成日、プラン、使ったモデル (`eleven_text_to_sound_v2`) を PR か `audio/` に書き残す。
- プロジェクトのライセンスとの関係: 効果音の mp3 自体はリポジトリに入れず、動画に混ぜた音だけが `site/video/` に入る。

## README の画面写真とソーシャルプレビュー
`node shots.mjs` で、モックアップから `docs/images/` の設定画面 (操作感・ホットキー) とランチャーの写真を撮り直し、GitHub のソーシャルプレビュー (1280 × 640、`social.html`) を `site/assets/social-preview.png` に書き出す。ソーシャルプレビューは API で設定できないため、リポジトリの Settings → General → Social preview に手でアップロードする。サイトの共有画像 (`og.png`、1200 × 630) は `og.html` から作る。
