# 紹介動画 (tools/film)

ホームページの紹介動画 (37.9 秒、1920×1080、60 fps、日本語・英語・簡体字・繁体字の 4 版) を作る道具。feel の物理モデルを偽の時計で 1 コマずつ進め、画面を撮って ffmpeg でまとめる。
同じ入力なら毎回同じ映像になる (時計は `clock.js`、乱数はシード固定)。台本・時刻・物理は全言語で同じで、違うのは文字 (`film-text.js`) と、中国語の書体 (`film-tokens.css`) だけ。

| ファイル | 役割 |
|---|---|
| `film.html` `film.css` `film.js` | 台本。導入 (`site/js/intro.js` の短い版)、投げる、ランチャー、整列、隣へ寄せる、隣のモニター、ホットキーとルール、標準スナップの置き換え、持ち運べる設定、終わりのロゴ。時刻の配分は `film.js` の先頭に書いてある |
| `film-text.js` | 画面に出す文字 (en / ja / zh-hans / zh-hant)。ja は yomiyasu で推敲した文言。zh は、アプリの中国語の辞書と `docs/I18N_GLOSSARY.md` の語にそろえた。窓の名前は架空の汎用名だけにする |
| `film-tokens.css` | 動画の配色と部品。`site/css/home.css` のライトの値 (4px 格子、角 12/8/4、墨 #111、補助文字 #6B6A67 ほか) とモニター・窓の部品をそのまま写したもの。home.css を直したらここも見直す。ダークは作らない |
| `render.mjs` | `--lang en|ja|zh-hans|zh-hant` で映像 (`snapwell-<lang>.mp4` `.webm`、`poster-<lang>.jpg`) と、効果音のきっかけ `cues.json` を `out/<lang>/` に書く。`--still 6.9,12` で指定の秒の静止画だけを `out/<lang>/stills/` に書く (場面の確認用) |
| `brand.mjs` | ロゴとアイコンの素材を 1 つの元から作る (下記) |
| `audio/` | 効果音と BGM の台本、生成、重ね合わせ、タイミング表 |

## 映像を作る

要 Node 20 以上、ffmpeg、Chrome か Edge。

```bash
cd tools/film
npm ci
node render.mjs --lang en    # out/en/ に snapwell-en.mp4, snapwell-en.webm, poster-en.jpg, cues.json。2274 コマで十数分かかる
node render.mjs --lang ja
node render.mjs --lang zh-hans   # 簡体字。繁体字は --lang zh-hant (どちらも en と同じ 2274 コマ)
node render.mjs --lang en --still 6.9,12.6,24.3   # 場面の確認だけ (数十秒)
```

音をつけてから `site/video/` に置く (次の節)。置き場所は従来どおりで、Git LFS は使わない。ja・en・zh-hans・zh-hant の `cues.json` は同じになる (時刻は言語に依らない)。ずれたら文字の幅で物理の事象が動いている。

## 中国語版 (簡体字・繁体字)

`--lang zh-hans` と `--lang zh-hant` (言語コードはこの形で固定。画面の `<html lang>` は `zh-Hans` / `zh-Hant`)。en・ja と違うのは文字だけで、動き・時刻・構図・効果音・BGM・長さは同じ。次の 2 つだけ、中国語のために手を入れた。

- 見出しと説明の大きさは、ja と同じ (見出し 64 px、説明 28 px、行の高さ 1.18 と 1.6、字間 -0.01em)。en の 76 px のままだと漢字が 600 px の列に収まらない。
- 設定の面の「言語」の 2 つのチップは、1 つ目を、その言語の名前 (`简体中文` / `繁體中文`) にして最初から点ける (en と ja は 1 つ目が `日本語`)。32 秒の切り替えで English が点く動きは同じ。

### 要確認 (訳)

中国語のネイティブの確認はない。窓の名前 (笔记・照片・邮件・计算器 / 筆記・相片・郵件・小算盤。架空の汎用名)、`自己的格`、吸附先の `上中`・`下中`、繁体字の `視窗管理員`、締めの 2 文 (`甩出窗口，吸附到位。`)、Windows 標準スナップの比較の `左半部分`・`左半部` (ランチャーのプリセット名。ホットキーは `左半边`)。`甩到下一个显示器` はアプリの `移到下一个显示器` と違うが、ja・en の動画もアプリと違う形なのでそこに合わせた。「隣へ寄せる」は、アプリと README に合わせて `靠向` を使う (用語集の主訳は `贴靠`/`貼齊` だが、Windows のスナップと同じ語になるため)。

### 書体 (中国語)

画面の字は、ja が OS の游ゴシック (`Yu Gothic UI`) を使うのと同じく、**OS の書体を使い、リポジトリには書体のファイルを入れない** (`film-tokens.css` の `:root:lang(zh-Hans)` と `:root:lang(zh-Hant)`)。英字と数字は en と同じ Segoe UI。漢字は、既定の並びのままだと游ゴシックの日本の字形になるので、言語ごとに次を先に置く。

| 版 | 書体 | 備考 |
|---|---|---|
| 簡体字 | Microsoft YaHei UI (微软雅黑) | Windows 11 に標準で入っている (`msyh.ttc`) |
| 繁体字 | Microsoft JhengHei UI (微軟正黑體) | Windows 11 に標準で入っている (`msjh.ttc`) |

- 利用条件: Microsoft の「Font redistribution FAQ」(<https://learn.microsoft.com/typography/fonts/font-faq>、2026-11-07 に本文を読んだ) は、Windows に付属する書体について、動画の字幕や文字は「文字がビットマップの画像として描かれ、書体のファイルが動画に埋め込まれていなければ」画像と同じ扱い、画像は「印刷と本質的に同じ」としている。一方、同じ FAQ は、Windows の書体の再配布 (別の PC・サーバーへの複製、ビットマップを含む別の形式への変換) を認めない。この動画は、書体のファイルを含まない H.264 / VP9 の映像で、書体のファイルはリポジトリにも入れない。`THIRD_PARTY_NOTICES.md` にはリポジトリに入れる物 (Newsreader など) だけを書くので、書かない。
- 書体のプロパティ (name テーブル、この PC の Windows 11 で 2026-11-07 に確認): `msyh.ttc` は Microsoft Corporation 名義で「Portions © Beijing Founder Electronics」、デザイナーは Founder。`msjh.ttc` は Microsoft Corporation 名義、デザイナーは Monotype Imaging。どちらも「Microsoft supplied font. You may use this font to create, display and print content as permitted by the license terms … of the Microsoft product」と書く (ライセンス欄)。
- **要確認**: 上の FAQ は、提供元 (foundry) から Microsoft が許諾を受けた書体について「Microsoft can't provide guidance to fonts that we didn't supply」「Check the font properties to find the owner」とも書く。YaHei UI と JhengHei UI は FAQ に名前が出ない。台湾の記事 (inside.com.tw) には Windows の書体は表示と印刷以外を許諾しないとあるが、取得できず (403) 読んでいない。個人の紹介動画としては FAQ の範囲に収まるとみているが、広告など厳しい用途に使う前に、Microsoft か提供元に確かめる。条件を避けたいときは、OFL の Noto Sans SC / TC (切り出して同梱) に替える手もある (Newsreader と同じやり方。`THIRD_PARTY_NOTICES.md` に足す)。
- 再現性: **Windows 11 なら同じ字形・同じ映像になる** (YaHei UI と JhengHei UI は Windows に付属)。Windows 以外や、これらの書体を消した PC では、後ろの `Noto Sans SC` / `Noto Sans TC` (入っていれば)、なければ OS の既定の書体になり、字形と字幅が変わる。ja の游ゴシック、en の Segoe UI Variable も同じ事情。

### 音は英語版と同じものを使う

効果音と BGM は言語に依らない (ja と en の音声は同じバイト列)。効果音の mp3 は鍵が要るので Git に入れていない。中国語版は、**英語版の音声を、再エンコードせずにそのまま写す** (`site/video/snapwell-en.*` から)。

```bash
cd tools/film
for L in zh-hans zh-hant; do
  ffmpeg -y -i out/$L/snapwell-$L.mp4  -i ../../site/video/snapwell-en.mp4  -map 0:v -map 1:a -c copy -movflags +faststart ../../site/video/snapwell-$L.mp4
  ffmpeg -y -i out/$L/snapwell-$L.webm -i ../../site/video/snapwell-en.webm -map 0:v -map 1:a -c copy ../../site/video/snapwell-$L.webm
  cp out/$L/poster-$L.jpg ../../site/video/poster-$L.jpg
done
```

`audio/sfx/` があれば、`node audio/mix.mjs --lang zh-hans` でも同じ結果になる (cues.json が en と同じため)。

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

音つきの mp4・webm を言語ごとに 1 組 (ja・en・zh-hans・zh-hant の計 8 本) と poster 4 枚を `site/video/` に置く。`<video>` に `muted` はない。`mix.mjs` は `out/<lang>/` の無音の映像を入力にするので、映像を作り直したらそのあとで重ねる。

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
README の画像は `docs/images/` にあり、言語ごとに `-ja` / `-en` / `-zh-hans` / `-zh-hant` の 4 枚がある (動画の場面の 3 組は 4 言語、設定画面の 2 組は ja・en だけ)。作り方は 2 通り。

- 紹介動画の場面 (`film-throw`、`film-launcher`、`film-arrange`): `node render.mjs --lang ja --still 8.2,13.4,17.5` (`--lang en`・`zh-hans`・`zh-hant` も) で静止画を書き出し、JPEG に変えて置く。動画の場面なので、モックアップ相当の画面になる。
- 設定画面 (`settings-feel`、`settings-keys`): 実物を撮る。`capture-settings.ps1` が `Snapwell.Settings.exe` を起動し、サイドバーの項目を選んでウィンドウを PNG にする。サイドバー下のバージョン表示を合わせるため、`dotnet build src/Snapwell.Settings/Snapwell.Settings.csproj -c Release -r <RID> -p:Version=<版>` で作った exe を使う。英語版は `%LOCALAPPDATA%\Snapwell\device.json` の `"Language"` を一時的に `"en"` にして撮り、元のファイルに戻す。撮ったあとは幅 1200 px に縮める。個人のアカウント名やファイル名が写っていないことを確かめる。

`node shots.mjs` は、GitHub のソーシャルプレビュー (1280 × 640、`social.html`) を `tools/film/social-preview.png` に書き出す (ホームページでは使わないので `site/` には置かない)。ソーシャルプレビューは API で設定できないため、リポジトリの Settings → General → Social preview に手でアップロードする。サイトの共有画像 (`og.png`、1200 × 630) は `og.html` から作る。
