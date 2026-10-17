"""assets/icons の SVG から .ico を作る。

使い方: python tools/build-icons.py [--edge <msedge.exe のパス>]

- SVG の描画には Microsoft Edge (Windows 11 に標準) のヘッドレスモードを使う。
  小さいサイズでも実寸の大きさで描くため、サイズごとに別の SVG を用意してある (弧を省く、線を太くする、など)。
- PNG の切り出しと ICO の組み立てには Pillow (HPND ライセンス) を使う。ビルドや出荷物には入らない。
- 生成物 (assets/icons/*.ico) はリポジトリに入れてあり、出荷のビルドはこのスクリプトに依存しない。
- 色もサイズも SVG の側で決める。ここで足すのは、サイズと SVG の対応表だけ。
"""
import argparse
import io
import os
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

ICONS = Path(__file__).resolve().parent.parent / "assets" / "icons"

# .ico の中身: 出力ファイル -> [(サイズ, 使う SVG)]。トレイは 100/125/150/200% の 16/20/24/32 px
ICO_PLAN = {
    "snapwell.ico": [
        (256, "app-large"), (48, "app-large"), (32, "app-32"),
        (24, "app-small"), (20, "app-small"), (16, "app-16"),
    ],
}
for state in ("active", "paused"):
    for theme in ("light", "dark"):
        ICO_PLAN[f"tray-{state}-{theme}.ico"] = [(s, f"tray-{state}-{theme}") for s in (16, 20, 24, 32)]
for theme in ("light", "dark"):
    # 注意は 16・20 px だけ、丸を大きくして「!」がつぶれないようにした版を使う
    ICO_PLAN[f"tray-attention-{theme}.ico"] = [
        (16, f"tray-attention-{theme}-small"), (20, f"tray-attention-{theme}-small"),
        (24, f"tray-attention-{theme}"), (32, f"tray-attention-{theme}"),
    ]

CANVAS = 600  # ヘッドレスの Edge は小さい窓を作れないため、大きい窓の左上に描いて切り出す


def find_edge(given):
    candidates = [given] if given else []
    for base in (os.environ.get("ProgramFiles(x86)"), os.environ.get("ProgramFiles")):
        if base:
            candidates.append(str(Path(base) / "Microsoft" / "Edge" / "Application" / "msedge.exe"))
    for path in candidates:
        if path and Path(path).is_file():
            return path
    sys.exit("msedge.exe が見つからない。--edge で指定する。")


def render(edge, work, svg_name, size):
    page = work / f"{svg_name}-{size}.html"
    svg = (ICONS / f"{svg_name}.svg").resolve().as_uri()
    page.write_text(
        f'<!doctype html><body style="margin:0;background:transparent">'
        f'<img src="{svg}" width="{size}" height="{size}" style="display:block"></body>',
        encoding="utf-8")
    shot = work / f"{svg_name}-{size}.png"
    subprocess.run([
        edge, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
        "--allow-file-access-from-files", f"--user-data-dir={work / 'profile'}",
        "--default-background-color=00000000", f"--window-size={CANVAS},{CANVAS}",
        f"--screenshot={shot}", page.as_uri(),
    ], check=True, capture_output=True, timeout=120)
    image = Image.open(shot).convert("RGBA").crop((0, 0, size, size))
    buffer = io.BytesIO()
    image.save(buffer, "PNG")
    return buffer.getvalue()


def write_ico(path, frames):
    """PNG 圧縮のフレームを並べた .ico を書く (Vista 以降で読める)。"""
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries, blobs = b"", b""
    for size, png in frames:
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(png), offset)
        blobs += png
        offset += len(png)
    path.write_bytes(header + entries + blobs)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--edge")
    edge = find_edge(parser.parse_args().edge)
    with tempfile.TemporaryDirectory() as temp:
        work = Path(temp)
        cache = {}
        for name, plan in ICO_PLAN.items():
            frames = []
            for size, svg_name in plan:
                key = (svg_name, size)
                if key not in cache:
                    cache[key] = render(edge, work, svg_name, size)
                frames.append((size, cache[key]))
            write_ico(ICONS / name, frames)
            print(f"{name}: {[s for s, _ in frames]}")


if __name__ == "__main__":
    main()
