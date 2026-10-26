"""ワードマーク用の書体 (Newsreader, SIL OFL 1.1) を、ASCII だけの静的な woff2 に切り出す。

使い方: python tools/subset-wordmark-font.py <Newsreader[opsz,wght].ttf のパス>

- 元の書体は https://github.com/google/fonts/tree/main/ofl/newsreader (リポジトリには入れない)。
- 太さ 500・光学サイズ 60 に固定して、"Snapwell" と英文の見出しに足りる ASCII だけを残す。
- 生成物 site/fonts/newsreader-500.woff2 はリポジトリに入れてあり、サイトも動画もこれを読む (外部の配信を使わない)。
- 要 fonttools と brotli (pip install fonttools brotli。ビルドと出荷には要らない)。
"""
import sys
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

src = Path(sys.argv[1])
out = Path(__file__).resolve().parent.parent / "site" / "fonts" / "newsreader-500.woff2"
font = TTFont(src)
font = instancer.instantiateVariableFont(font, {"wght": 500, "opsz": 60})
opts = subset.Options()
opts.flavor = "woff2"
opts.layout_features = ["kern", "liga", "calt"]
opts.name_IDs = [0, 1, 2, 3, 4, 5, 6, 13, 14]
sub = subset.Subsetter(opts)
sub.populate(unicodes=list(range(0x20, 0x7F)) + [0x2019, 0x2014, 0x00B7])
sub.subset(font)
out.parent.mkdir(parents=True, exist_ok=True)
font.flavor = "woff2"
font.save(out)
print(out, out.stat().st_size, "bytes")
