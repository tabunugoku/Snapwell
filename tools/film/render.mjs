// 紹介動画を書き出す: node render.mjs [--fps 60] [--out out] [--frames 120]
// feel の物理モデルを偽の時計で進め、1 コマずつ画面を撮り、ffmpeg で mp4 と webm にまとめる。
import { chromium } from 'playwright-core';
import { mkdirSync, rmSync, existsSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const arg = (name, def) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : def; };
const fps = Number(arg('fps', 60));
const out = resolve(arg('out', resolve(here, 'out')));
const only = arg('frames', null); // 試し撮り: 先頭の枚数だけ撮る (mp4 などは作らない)
const chrome = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
].find(existsSync);
if (!chrome) throw new Error('Chrome か Edge が見つからない');

const frames = resolve(out, 'frames');
rmSync(frames, { recursive: true, force: true });
mkdirSync(frames, { recursive: true });

const browser = await chromium.launch({ executablePath: chrome, args: ['--font-render-hinting=none'] });
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1, colorScheme: 'light' });
page.on('pageerror', (e) => console.error('pageerror', e.message));
page.on('console', (m) => { if (m.type() === 'error') console.error('console', m.text()); });
await page.goto(pathToFileURL(resolve(here, 'film.html')).href);
await page.waitForFunction(() => window.__duration);
await page.waitForTimeout(400); // ResizeObserver の最初の配置を待つ

const duration = await page.evaluate(() => window.__duration);
const total = only ? Number(only) : Math.round(duration * fps);
for (let i = 0; i < total; i++) {
  await page.evaluate((ms) => window.__tick(ms), 1000 / fps);
  const buf = await page.screenshot({ type: 'jpeg', quality: 94 });
  writeFileSync(resolve(frames, `${String(i).padStart(5, '0')}.jpg`), buf);
  if (i % 120 === 0) console.log(`frame ${i}/${total}`);
}
await browser.close();
if (only) process.exit(0);

const input = ['-y', '-framerate', String(fps), '-i', resolve(frames, '%05d.jpg')];
const color = [
  '-vf', 'scale=out_color_matrix=bt709:out_range=tv',
  '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709',
];
const run = (args) => {
  const r = spawnSync('ffmpeg', args, { stdio: 'inherit' });
  if (r.status !== 0) throw new Error('ffmpeg が失敗した');
};
run([...input, ...color, '-c:v', 'libx264', '-preset', 'slow', '-crf', '20', '-pix_fmt', 'yuv420p',
  '-movflags', '+faststart', '-an', resolve(out, 'snapwell.mp4')]);
run([...input, ...color, '-c:v', 'libvpx-vp9', '-crf', '34', '-b:v', '0', '-pix_fmt', 'yuv420p',
  '-an', resolve(out, 'snapwell.webm')]);
run(['-y', '-i', resolve(out, 'snapwell.mp4'), '-ss', '4.4', '-frames:v', '1', '-update', '1', '-q:v', '3', resolve(out, 'poster.jpg')]);
console.log('done', out);
