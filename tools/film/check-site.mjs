// ホームページの表示を確かめる: node check-site.mjs [--url http://127.0.0.1:8766/] [--out <フォルダ>]
// デスクトップ (明・暗)、幅 375、モーション削減で撮り、外部へのリクエストとコンソールのエラーを数える。
import { chromium } from 'playwright-core';
import { mkdirSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';

const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? process.argv[i + 1] : d; };
const url = arg('url', 'http://127.0.0.1:8766/');
const out = resolve(arg('out', 'out/site'));
mkdirSync(out, { recursive: true });
const chrome = ['C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
const browser = await chromium.launch({ executablePath: chrome });
const cases = [
  { name: 'desktop-light', viewport: { width: 1440, height: 900 }, colorScheme: 'light' },
  { name: 'desktop-dark', viewport: { width: 1440, height: 900 }, colorScheme: 'dark' },
  { name: 'mobile-light', viewport: { width: 375, height: 812 }, colorScheme: 'light' },
  { name: 'mobile-dark', viewport: { width: 375, height: 812 }, colorScheme: 'dark' },
  { name: 'reduced-light', viewport: { width: 1440, height: 900 }, colorScheme: 'light', reducedMotion: 'reduce' },
  { name: 'reduced-dark-mobile', viewport: { width: 375, height: 812 }, colorScheme: 'dark', reducedMotion: 'reduce' },
];
let bad = 0;
for (const c of cases) {
  const ctx = await browser.newContext({ viewport: c.viewport, colorScheme: c.colorScheme, reducedMotion: c.reducedMotion || 'no-preference' });
  const page = await ctx.newPage();
  const external = [];
  const errors = [];
  page.on('request', (r) => { if (!r.url().startsWith(new URL(url).origin) && !r.url().startsWith('data:')) external.push(r.url()); });
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(url);
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(c.reducedMotion ? 800 : 9500); // 導入が終わるまで待つ
  await page.screenshot({ path: resolve(out, `${c.name}-hero.png`) });
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  const logo = await page.evaluate(() => {
    const n = document.querySelector('#introLogo .logo-name');
    const s = document.querySelector('#introLogo svg');
    return { name: n && getComputedStyle(n).opacity, mark: s && getComputedStyle(s).opacity, family: n && getComputedStyle(n).fontFamily.slice(0, 30) };
  });
  console.log(c.name, JSON.stringify({ overflowPx: overflow, logo, external, errors }));
  if (overflow > 0 || external.length || errors.length) bad++;
  await page.screenshot({ path: resolve(out, `${c.name}-full.png`), fullPage: true });
  await ctx.close();
}
await browser.close();
process.exit(bad ? 1 : 0);
