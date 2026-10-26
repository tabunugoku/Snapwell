# 効果音のタイミング表

自動生成: `node table.mjs`。元は render.mjs が書く cues.json (動画 30 秒、60 fps)。
音の指示 (prompt) と長さは `sounds.json`。時刻は動画の頭からの秒で、offset を含む。

| 秒 | きっかけ | 効果音 | 音量 (dB) |
|---|---|---|---|
| 0.50 | intro-enter | whoosh | -6 |
| 1.90 | intro-burst | burst | -4 |
| 3.02 | intro-grid | tick | -6 |
| 3.92 | intro-layoutB | tick | -9 |
| 4.82 | intro-layoutC | tick | -9 |
| 5.70 | intro-final | tick | -6 |
| 6.50 | intro-hop 0 | tap | -10 |
| 6.60 | intro-hop 1 | tap | -10 |
| 6.70 | intro-hop 2 | tap | -10 |
| 6.80 | intro-hop 3 | tap | -10 |
| 6.90 | intro-hop 4 | tap | -10 |
| 7.00 | intro-hop 5 | tap | -10 |
| 7.70 | intro-logo | logo | -5 |
| 10.78 | release | throw | -8 |
| 11.28 | land | land | -6 |
| 13.08 | release | throw | -8 |
| 13.62 | land | land | -6 |
| 14.52 | feel (crisp) | tick | -8 |
| 15.38 | release | throw | -8 |
| 15.90 | land | land | -6 |
| 16.52 | feel (smooth) | tick | -8 |
| 17.38 | release | throw | -8 |
| 17.88 | land | land | -6 |
| 18.52 | feel (bouncy) | tick | -8 |
| 19.38 | release | throw | -8 |
| 20.07 | land | land | -6 |
| 21.00 | key | key | -7 |
| 21.10 | key | key | -7 |
| 21.20 | key | key | -7 |
| 21.50 | launcher-open | overlay | -8 |
| 22.45 | cell | cell | -16 |
| 22.53 | cell | cell | -16 |
| 22.75 | cell | cell | -16 |
| 22.98 | cell | cell | -16 |
| 23.07 | cell | cell | -16 |
| 23.80 | key | key | -7 |
| 23.90 | launcher-pick | confirm | -6 |
| 24.47 | land | land | -6 |
| 25.60 | key | key | -7 |
| 25.70 | key | key | -7 |
| 25.80 | key | key | -7 |
| 26.10 | arrange | arrange | -7 |
| 27.00 | land | land | -6 |
| 27.02 | land | land | -6 |
| 27.12 | land | land | -6 |
| 28.30 | end-in | logo | -7 |
