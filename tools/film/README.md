# 紹介動画 (tools/film)

ホームページの紹介動画 (37.9 秒、1920×1080、60 fps、日本語版と英語版) を作る道具。feel の物理モデルを偽の時計で 1 コマずつ進め、画面を撮って ffmpeg でまとめる。
同じ入力なら毎回同じ映像になる (時計は `clock.js`、乱数はシード固定)。台本・時刻・物理は両言語で同じで、違うのは文字 (`film-text.js`) だけ。

| ファイル | 役割 |
|---|---|
| `film.html` `film.css` `film.js` | 台本。導入 (`site/js/intro.js` の短い版)、投げる、ランチャー、整列、隣へ寄せる、隣のモニター、ホットキーとルール、標準スナップの置き換え、持ち運べる設定、終わりのロゴ。時刻の配分は `film.js` の先頭に書いてある |
| `film-text.js` | 画面に出す文字 (en / ja)。ja は yomiyasu で推敲した文言。窓の名前は架空の汎用名だけにする |
| `film-tokens.css` | 動画の配色と部品。`site/css/home.css` のライトの値 (4px 格子、角 12/8/4、墨 #111、補助文字 #6B6A67 ほか) とモニター・窓の部品をそのまま写したもの。home.css を直したらここも見直す。ダークは作らない |
| `render.mjs` | `--lang en|ja` で映像 (`snapwell-<lang>.mp4` `.webm`、`poster-<lang>.jpg`) と、効果音のきっかけ `cues.json` を `out/<lang>/` に書く。`--still 6.9,12` で指定の秒の静止画だけを `out/<lang>/stills/` に書く (場面の確認用) |
| `brand.mjs` | ロゴとアイコンの素材を 1 つの元から作る (下記) |
| `audio/` | 効果音と BGM の台本、生成、重ね合わせ、タイミング表 |

## 映像を作る

要 Node 20 以上、ffmpeg、Chrome か Edge。

```bash
cd tools/film
npm ci
node render.mjs --lang en    # out/en/ に snapwell-en.mp4, snapwell-en.webm, poster-en.jpg, cues.json。2160 コマで十数分かかる
node render.mjs --lang ja
node render.mjs --lang en --still 6.9,12.6,24.3   # 場面の確認だけ (数十秒)
```

音をつけてから `site/video/` に置く (次の節)。置き場所は従来どおりで、Git LFS は使わない。ja と en の `cues.json` は同じになる (時刻は言語に依らない)。ずれたら文字の幅で物理の事象が動いている。

## ロゴとアイコン (ずれないための決まり)

元は 2 つだけ。

- アプリのアイコン: `assets/icons/app-large.svg`
- ワードマークの書体: `site/fonts/newsreader-500.woff2` (Newsreader、SIL OFL 1.1。`tools/subset-wordmark-font.py` で切り出す。`THIRD_PARTY_NOTICES.md` に記載)

`node brand.mjs` が `site/assets/favicon.svg` (app-32。ヘッダの 32 px もこれ)、`apple-touch-icon.png`、`og.png`、`site/js/brand-mark.js` を作る。
ホームページのヘッダとヒーロー、動画の導入と終わり、favicon、OGP はこれらだけを使う。アイコンを直したら `node brand.mjs` を走らせて同じコミットに入れる。
`node brand.mjs --check` は PNG 以外が元とずれていないかを見る (ずれていれば終了コード 1)。

## 音を足す (効果音と BGM)

映像は無音で出す。効果音は ElevenLabs の Sound Effects API、BGM は Music API で作って重ねる。鍵 (API key) はリポジトリに書かず、環境変数 `ELEVENLABS_API_KEY` から読む (Windows ではユーザー環境変数も見る)。音は言語に依らないので、同じ 1 セットを ja と en の両方に使う。

```powershell
cd tools/film
node audio/table.mjs out/en/cues.json        # audio/TIMING.md を作り直す (鍵は要らない)
node audio/generate-sfx.mjs --dry-run        # 何を何秒で頼むか、クレジットの目安を見る (送らない)
$env:ELEVENLABS_API_KEY = "<鍵>"
node audio/generate-sfx.mjs                  # audio/sfx/*.mp3 を作る (Git には入れない)
node audio/generate-bgm.mjs                  # BGM の候補 3 本を audio/bgm/candidates/ に作る (Git には入れない)。鍵に music_generation の権限が要る
# 聞いて 1 本選び、audio/bgm/bgm.mp3 に置く (これは Git に入れる。生成は再現できないため)
node audio/mix.mjs --lang en                 # out/with-sound/en/ に音つきの mp4 と webm (BGM は bgm.mp3 があるときだけ)
node audio/mix.mjs --lang ja
```

音つきの 4 本と poster 2 枚を `site/video/` に置く。`<video>` に `muted` はない。`mix.mjs` は `out/<lang>/` の無音の映像を入力にするので、映像を作り直したらそのあとで重ねる。

- 効果音は、ここぞという所 (バースト、ロゴ、最初の投げ・着地、ランチャーの開閉と確定、最初の整列、隣へ寄せる、モニター間、置き換えのスイッチ、元に戻す、終わりのロゴ) だけにつける。キー、区画の移動、導入の細かい音などは付けない (`sounds.json` の `cue_map` に無いきっかけは無音)。
- 導入のロゴは `film.js` の `D` (1.9 秒) だけ長く見せている。導入より後の台本の時刻は、`D` を足して動く。
- 効果音の指示と長さ、きっかけとの対応は `audio/sounds.json`。きっかけの時刻は台本 (`film.js`) と物理の事象 (離した時刻、止まった時刻) から `cues.json` に書かれる。
- 音量: 効果音は生成した音の大きさがまちまち (鳴っている部分の RMS が -9 から -40 dB) なので、`mix.mjs` が各音の RMS を `sounds.json` の `target` にそろえる (ピークは -3 dB まで)。cue_map の `gain` はそのきっかけだけの上げ下げ。BGM は `bgm.json` の `mix.lufs` (-32) にそろえ、効果音が鳴る間はさらに下がる。
- BGM の指示は `audio/bgm.json` (候補 3 本の prompt、ミックスの設定)。BGM は効果音より約 12〜15 dB 小さく、効果音が鳴る間は `sidechaincompress` でさらに下がる。頭に 1.5 秒のフェードイン、末尾に 2.5 秒のフェードアウト。
- 使うエンドポイント: `POST https://api.elevenlabs.io/v1/sound-generation` (効果音)、`POST https://api.elevenlabs.io/v1/music` (BGM。`prompt`、`music_length_ms`、`model_id`、`force_instrumental`)。
- 料金: 効果音は 1 秒あたり 40 クレジット (17 件、約 13 秒、約 520 クレジット)。Music API は米ドル課金で 1 分 0.15 ドル (<https://elevenlabs.io/pricing/api>、2026-11-04 確認)。36 秒の候補 3 本で約 0.27 ドル。

### 生成した効果音の利用条件 (2026-10-26 に確認。契約の前にもう一度見ること)

- 利用規約 (<https://elevenlabs.io/terms-of-use>) 第 4 条: 出力 (Output) の権利は利用者が持ち、サービスの外でも使える。第 1(c) 条: **無料プランは非商用に限る。有料プランは商用に使える** (禁止事項の方針は守る)。規約に出力へのクレジット表記の義務は見当たらない。
- 一方、同社の効果音の案内ページ (<https://elevenlabs.io/sound-effects/commercial>) は「無料プランは elevenlabs.io のクレジット表記が要る、有料プランは不要」と書く。規約と案内ページの書き方が違うので、**有料プランで生成する** のが確実 (どちらの読み方でも商用に使え、表記は要らない)。
- 第 4 条(d): 出力は同社がサービスの改善に使える権利を持つ。第 10 条: 出力は他の利用者のものと似ることがある (独占はできない)。
- 生成の記録: 2026-10-26、有料プランの鍵 (ユーザーが確認)、モデル `eleven_text_to_sound_v2`、12 件・指定の長さの合計 10.4 秒 (約 416 クレジット)、`tick` は無音に近かったので指示を変えて 1 件だけ作り直し (約 20 クレジット)。
- 作った音は公開前に聞いて確かめる (第 4 条(k) は出力を確認する責任が利用者にあるとする)。生成日、プラン、使ったモデル (`eleven_text_to_sound_v2`) を PR か `audio/` に書き残す。
- 生成の記録 (2026-11-04 の作り直し): 鍵は有料プラン (ユーザーが確認)、モデル `eleven_text_to_sound_v2`、17 件・指定の長さの合計 13.0 秒 (約 520 クレジット)。`cell` は無音に近かったので指示を変えて 1 件だけ作り直した (約 20 クレジット)。
- BGM の利用条件 (<https://elevenlabs.io/music-terms>、2026-11-04 確認): 有料プランは表記不要 (無料プランは「Created in collaboration with ElevenLabs」の表記が要る)。Free から Pro は個人の利用に限り、Scale 以上は小さな組織まで。映画・テレビ・ラジオを除く商用 (オンラインとオフライン) に使える。出力の権利は利用者が持ち、公開前に出力を確かめる責任も利用者にある。プロンプトに実在のアーティスト名・曲名・歌詞を書かない。有料プランの鍵で生成すること。BGM の生成の記録: 2026-11-04、有料プランの鍵 (ユーザーが確認)、モデル `music_v2_5`、`force_instrumental`、36 秒の候補 3 本 (約 0.27 ドル)。採用: 候補 b (ピアノの 4 音の反復にパッド、`audio/bgm/bgm.mp3`)。
- プロジェクトのライセンスとの関係: 効果音の mp3 自体はリポジトリに入れず、動画に混ぜた音だけが `site/video/` に入る。

## README の画面写真とソーシャルプレビュー
`node shots.mjs` で、モックアップから `docs/images/` の設定画面 (操作感・ホットキー) とランチャーの写真を撮り直し、GitHub のソーシャルプレビュー (1280 × 640、`social.html`) を `tools/film/social-preview.png` に書き出す (ホームページでは使わないので `site/` には置かない)。ソーシャルプレビューは API で設定できないため、リポジトリの Settings → General → Social preview に手でアップロードする。サイトの共有画像 (`og.png`、1200 × 630) は `og.html` から作る。
