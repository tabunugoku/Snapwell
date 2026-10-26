// ブランドの素材を 1 つの元から作る: node brand.mjs [--check]
// 元: assets/icons/app-large.svg (アプリのアイコン) と site/fonts/newsreader-500.woff2 (ワードマークの書体)。
// 作るもの: site/assets/icon.svg (app-large と同じ)、site/assets/favicon.svg (app-32 と同じ。アプリも小さいサイズは枠を太くして弧を省く)、
//   site/js/brand-mark.js (サイトと動画が差し込むアイコンの SVG。弧と正方形に動かす印を付ける)、
//   site/assets/apple-touch-icon.png (180)、site/assets/og.png (1200×630)。
// --check は PNG 以外を作り直して差を見るだけで、ずれていれば終了コード 1 で止まる。
import { chromium } from 'playwright-core';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const check = process.argv.includes('--check');
const read = (f) => readFileSync(resolve(root, 'assets/icons', f), 'utf8').replace(/\r\n/g, '\n');
const icon = read('app-large.svg');
const small = read('app-32.svg');

// 動かす印: 点線の弧が描かれ、青い正方形が弾んで入る (サイトの先頭と動画の最後で使う)
let mark = icon.trim();
const sq = '<rect x="23" y="7" width="14" height="14" rx="3" fill="#8CCBF2"/>';
const arc = '<path d="M8 34 C 14 34, 18 26, 26 18"';
if (!mark.includes(sq) || !mark.includes(arc)) throw new Error('app-large.svg の形が変わった。brand.mjs の印の付け方を直す');
mark = mark.replace(sq, '<rect class="mark-sq" x="23" y="7" width="14" height="14" rx="3" fill="#8CCBF2"/>')
  .replace(arc, '<path class="mark-arc"' + arc.slice('<path'.length))
  .replace('<svg ', '<svg class="mark" aria-hidden="true" focusable="false" ');
const markJs = `// 生成物: tools/film/brand.mjs が assets/icons/app-large.svg から作る。直接は直さない。\nwindow.SNAP_MARK = ${JSON.stringify(mark)};\n`;

const texts = {
  'site/assets/icon.svg': icon,
  'site/assets/favicon.svg': small,
  'site/js/brand-mark.js': markJs,
};
let drift = false;
for (const [rel, body] of Object.entries(texts)) {
  const p = resolve(root, rel);
  const cur = existsSync(p) ? readFileSync(p, 'utf8').replace(/\r\n/g, '\n') : null;
  if (cur !== body) {
    if (check) { console.error('ずれている:', rel); drift = true; } else writeFileSync(p, body);
  }
}
if (check) { if (drift) process.exit(1); console.log('ブランドの素材は元と一致している (PNG は対象外)'); process.exit(0); }

const chrome = ['C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
if (!chrome) throw new Error('Chrome か Edge が見つからない');
const browser = await chromium.launch({ executablePath: chrome });
const shoot = async (file, w, h, out) => {
  const page = await browser.newPage({ viewport: { width: w, height: h }, deviceScaleFactor: 1 });
  await page.goto(pathToFileURL(resolve(here, file)).href);
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: resolve(root, out), type: 'png' });
  await page.close();
};
await shoot('og.html', 1200, 630, 'site/assets/og.png');
await shoot('touch.html', 180, 180, 'site/assets/apple-touch-icon.png');
await browser.close();
console.log('done');
