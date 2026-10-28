// README 用の画面写真を、モックアップから撮る: node shots.mjs
// モックアップ (docs/mockups/snapwell.html) の設定画面の「操作感」と「ホットキー」、ランチャーを docs/images/ に書き出す。
// GitHub のソーシャルプレビュー (1280 × 640) も、og.html と同じ見た目で作る。
import { chromium } from 'playwright-core';
import { existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const chrome = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
].find(existsSync);
if (!chrome) throw new Error('Chrome か Edge が見つからない');

const mock = pathToFileURL(resolve(root, 'docs/mockups/snapwell.html')).href;
const browser = await chromium.launch({ executablePath: chrome, args: ['--font-render-hinting=none'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1, colorScheme: 'light' });
page.on('pageerror', (e) => console.error('pageerror', e.message));

// 設定画面
await page.goto(`${mock}#settings`);
await page.waitForSelector('#snav button');
await page.evaluate(() => document.fonts.ready);
const frame = page.locator('#screen-settings .app-frame');
for (const [name, key] of [['settings-feel', 'feel'], ['settings-keys', 'keys']]) {
  await page.click(`#snav button[data-page="${key}"]`);
  await page.waitForTimeout(500);
  const box = await frame.boundingBox();
  await page.screenshot({ path: resolve(root, `docs/images/${name}.png`), clip: { ...box, height: Math.min(box.height, 820) } });
}

// ランチャー
await page.goto(`${mock}#launcher`);
await page.reload();
await page.waitForSelector('#lMonitor');
await page.click('#lOpen');
await page.waitForTimeout(600);
await page.click('#lWinHead');
await page.waitForTimeout(800);
await page.locator('#lMonitor').screenshot({ path: resolve(root, 'docs/images/launcher.png') });

// ソーシャルプレビュー (GitHub の Settings → Social preview に、手でアップロードする)
const og = await browser.newPage({ viewport: { width: 1280, height: 640 }, deviceScaleFactor: 1 });
await og.goto(pathToFileURL(resolve(here, 'social.html')).href);
await og.evaluate(() => document.fonts.load('500 64px Newsreader'));
await og.evaluate(() => document.fonts.ready);
await og.waitForTimeout(300);
await og.screenshot({ path: resolve(root, 'tools/film/social-preview.png') });

await browser.close();
console.log('done');
