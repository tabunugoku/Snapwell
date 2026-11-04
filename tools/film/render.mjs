// 紹介動画を書き出す: node render.mjs --lang en|ja [--fps 60] [--out out] [--frames 120] [--still 6.9,12.5,...] [--poster 6.9]
// 映像 (無音の mp4 と webm)、ポスター、効果音のきっかけ cues.json を out/<lang>/ に作る。音は README の手順で足す。
// feel の物理モデルを偽の時計で進め、1 コマずつ画面を撮り、ffmpeg で mp4 と webm にまとめる。
// --still: 動画は作らず、指定した秒の静止画だけを out/<lang>/stills/ に書く (場面の確認用。時計は進めるが撮るのは指定の秒だけ)。
import { chromium } from 'playwright-core';
import { mkdirSync, rmSync, existsSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const arg = (name, def) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : def; };
const lang = arg('lang', 'en');
if (!['en', 'ja'].includes(lang)) throw new Error('--lang は en か ja');
const fps = Number(arg('fps', 60));
const out = resolve(arg('out', resolve(here, 'out')), lang);
const only = arg('frames', null); // 試し撮り: 先頭の枚数だけ撮る (mp4 などは作らない)
const stills = arg('still', null)?.split(',').map(Number);
const posterAt = Number(arg('poster', 6.9));
const chrome = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
].find(existsSync);
if (!chrome) throw new Error('Chrome か Edge が見つからない');

const frames = resolve(out, 'frames');
rmSync(frames, { recursive: true, force: true });
mkdirSync(frames, { recursive: true });
const stillDir = resolve(out, 'stills');
if (stills) { rmSync(stillDir, { recursive: true, force: true }); mkdirSync(stillDir, { recursive: true }); }

const browser = await chromium.launch({ executablePath: chrome, args: ['--font-render-hinting=none'] });
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1, colorScheme: 'light' });
page.on('pageerror', (e) => console.error('pageerror', e.message));
page.on('console', (m) => { if (m.type() === 'error') console.error('console', m.text()); });
await page.goto(`${pathToFileURL(resolve(here, 'film.html')).href}?lang=${lang}`);
await page.waitForFunction(() => window.__duration);
await page.evaluate(() => document.fonts.load('500 64px Newsreader'));
await page.evaluate(() => document.fonts.ready);
await page.waitForTimeout(400); // ResizeObserver の最初の配置を待つ

const duration = await page.evaluate(() => window.__duration);
const total = only ? Number(only) : Math.round(duration * fps);
const want = stills ? new Set(stills.map((s) => Math.round(s * fps))) : null;
const lastWanted = want ? Math.max(...want) : 0;
for (let i = 0; i < total; i++) {
  await page.evaluate((ms) => window.__tick(ms), 1000 / fps);
  if (want) {
    if (want.has(i + 1)) writeFileSync(resolve(stillDir, `${((i + 1) / fps).toFixed(2)}.png`), await page.screenshot({ type: 'png' }));
    if (i + 1 >= lastWanted) break;
    continue;
  }
  const buf = await page.screenshot({ type: 'jpeg', quality: 94 });
  writeFileSync(resolve(frames, `${String(i).padStart(5, '0')}.jpg`), buf);
  if (i % 120 === 0) console.log(`frame ${i}/${total}`);
}
const cues = await page.evaluate(() => window.__cues);
await browser.close();
if (only || stills) { if (stills) console.log(`stills: ${stillDir}`); process.exit(0); }
writeFileSync(resolve(out, 'cues.json'), JSON.stringify({ duration, fps, lang, cues }, null, 1));

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
  '-movflags', '+faststart', '-an', resolve(out, `snapwell-${lang}.mp4`)]);
run([...input, ...color, '-c:v', 'libvpx-vp9', '-crf', '34', '-b:v', '0', '-pix_fmt', 'yuv420p',
  '-an', resolve(out, `snapwell-${lang}.webm`)]);
run(['-y', '-i', resolve(out, `snapwell-${lang}.mp4`), '-ss', String(posterAt), '-frames:v', '1', '-update', '1', '-q:v', '3', resolve(out, `poster-${lang}.jpg`)]);
