// GitHub のソーシャルプレビュー (1280 × 640) を、social.html から書き出す: node shots.mjs
// README の画面写真はここでは作らない。紹介動画の場面は `node render.mjs --lang <ja|en> --still 8.2,13.4,17.5`、
// 設定画面は実物を `capture-settings.ps1` で撮る (手順は README.md「README の画面写真」)。
import { chromium } from 'playwright-core';
import { existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const chrome = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
].find(existsSync);
if (!chrome) throw new Error('Chrome か Edge が見つからない');

const browser = await chromium.launch({ executablePath: chrome, args: ['--font-render-hinting=none'] });

// ソーシャルプレビュー (GitHub の Settings → Social preview に、手でアップロードする)
const og = await browser.newPage({ viewport: { width: 1280, height: 640 }, deviceScaleFactor: 1 });
await og.goto(pathToFileURL(resolve(here, 'social.html')).href);
await og.evaluate(() => document.fonts.load('500 64px Newsreader'));
await og.evaluate(() => document.fonts.ready);
await og.waitForTimeout(300);
await og.screenshot({ path: resolve(here, 'social-preview.png') });

await browser.close();
console.log('done');
