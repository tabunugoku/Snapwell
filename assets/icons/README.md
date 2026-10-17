# アイコン

アプリとトレイのアイコンの元になる SVG と、そこから作った .ico。

- `app-*.svg` → `snapwell.ico` (Snapwell.exe と Snapwell.Settings.exe。256/48/32/24/20/16 px)。暗いタイルに白い枠と青い正方形。48 px 以上だけ点線の弧を付け、小さいほど枠を太くする
- `tray-{active,paused,attention}-{light,dark}.svg` → `tray-*.ico` (16/20/24/32 px = 拡大率 100/125/150/200%)。色はモックアップの `--blue-tx` と `--red-tx`
  - 有効: 塗りの青い正方形 / 一時停止中: 全体を薄く、正方形は中空 / 注意: 「!」を抜いた赤い丸。`-small` は 16・20 px 用で、丸と「!」を大きくしてある
  - 色だけに頼らず形でも区別できる。ライト用とダーク用は、タスクバーの明暗に合わせて TrayIconSet が選ぶ

SVG を直したら `python tools/build-icons.py` で .ico を作り直し、SVG と .ico を同じコミットに入れる。
作り直しには Microsoft Edge (Windows 11 に標準) と Pillow (`pip install pillow`、HPND ライセンス) が要る。出荷のビルドには要らない。
