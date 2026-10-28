# Third-party notices

Snapwell のスナップの物理モデル（語彙から質量・振動数・減衰比・磁石の強さを導く式と語の数値、二段のばねの磁石場、壁、RK4 による積分、手を離したときの予測、静止の判定の数値）は、次のプロジェクトの考え方と数値をもとにしている。

## feel

- https://github.com/diip3sh/feel
- ホームページ（`site/`）は、feel のランタイム（`runtime/feel.js`、v0.2）をそのまま `site/js/feel.js` に同梱して、デモと画面の動きを動かしている。

```
MIT License

Copyright (c) 2026 diip3sh

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Newsreader

- https://github.com/productiontype/Newsreader (Production Type)
- ホームページ（`site/`）と紹介動画のワードマーク「Snapwell」と、ホームページの英語の見出しに使う。太さ 500 と ASCII（ほかに ’ — · の 3 字）だけに切り出した `site/fonts/newsreader-500.woff2` を同梱する（切り出しは `tools/subset-wordmark-font.py`）。日本語の見出しはこの書体に字がないので、OS の明朝体で表示する。
- SIL Open Font License 1.1。全文は `site/fonts/OFL-Newsreader.txt`。

Copyright 2020 The Newsreader Project Authors (http://github.com/productiontype/Newsreader)
