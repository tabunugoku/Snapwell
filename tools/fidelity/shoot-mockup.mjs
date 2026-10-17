// モックアップの設定画面を、実機と同じ大きさで撮影し、要素の寸法・角丸・色を JSON で出す。
// 使い方: node shoot-mockup.mjs --out out/mock --pxwidth 1568 --pxheight 1159 --dpr 1.5 [--themes dark,light] [--pages general,grid,...]
// pxwidth / pxheight は実機のクライアント領域の物理 px (shoot-native.ps1 の JSON の clientPx)。dpr は実機の DPI / 96。
// Edge (channel: msedge) をヘッドレスで使う。playwright の同梱ブラウザは要らない。
import { chromium } from 'playwright-core';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const args = Object.fromEntries(process.argv.slice(2).reduce((a, v, i, all) => {
    if (v.startsWith('--')) a.push([v.slice(2), all[i + 1]]);
    return a;
}, []));
const out = resolve(args.out ?? 'out/mock');
const dpr = Number(args.dpr ?? 1);
// 論理 px。小数のまま枠に与え、ビューポートだけ切り上げる (画像の比較側で共通の大きさに切る)
const width = Number(args.pxwidth ?? 1046) / dpr;
const height = Number(args.pxheight ?? 744) / dpr;
const vw = Math.ceil(width);
const vh = Math.ceil(height);
const themes = (args.themes ?? 'dark,light').split(',');
const pages = (args.pages ?? 'general,grid,keys,feel,rules,os,sync,look').split(',');
const html = pathToFileURL(resolve(dirname(fileURLToPath(import.meta.url)), '../../docs/mockups/snapwell.html')).href;
mkdirSync(out, { recursive: true });

// 画面ごとの差し替え口。ランチャーなどを足すときは、ここに「画面名 → 撮影の準備」を足す
const SCREENS = {
    settings: {
        // 設定画面の枠だけを左上に固定して、実機のクライアント領域と同じ大きさにする
        prepare: ({ w, h }) => {
            const frame = document.querySelector('#screen-settings .app-frame');
            document.body.innerHTML = '';
            document.body.appendChild(frame);
            document.body.style.cssText = 'margin:0;background:var(--surface)';
            frame.style.cssText = `position:fixed;left:0;top:0;width:${w}px;height:${h}px;border:0;border-radius:0;display:flex;flex-direction:column`;
            const settings = frame.querySelector('.settings');
            settings.style.cssText = 'flex:1;min-height:0';
            return frame;
        },
        pages: true,
    },
};

// 計測する要素。name は実機の UIA の Name と突き合わせるキー
// text: true は、要素の箱ではなく文字そのものの矩形を測る (実機の UIA の Text と同じ基準)
const PROBES = [
    ['nav', '.snav button', (e) => e.textContent.trim()],
    ['navlabel', '.snav button span', (e) => e.textContent.trim(), true],
    ['title', '.spage.active h3', (e) => e.textContent.trim(), true],
    ['lead', '.spage.active > p.lead', (e) => e.textContent.trim(), true],
    ['row', '.spage.active .row', (e) => e.querySelector('.t')?.textContent.trim()],
    ['rowtitle', '.spage.active .row .t', (e) => e.textContent.trim(), true],
    ['rowdesc', '.spage.active .row .d', (e) => e.textContent.trim(), true],
    ['group', '.spage.active .group-title', (e) => e.textContent.trim(), true],
    ['switch', '.spage.active .switch span', (e) => e.closest('.switch').querySelector('input').getAttribute('aria-label')],
    ['input', '.spage.active select.input, .spage.active input.input', (e) => e.getAttribute('aria-label') ?? e.value],
    ['button', '.spage.active .btn', (e) => e.textContent.trim()],
    ['chip', '.spage.active .chip', (e) => e.textContent.trim()],
];

const browser = await chromium.launch({ channel: 'msedge', headless: true });
const result = { width, height, dpr, viewport: [vw, vh], elements: {} };
for (const theme of themes) {
    const ctx = await browser.newContext({
        viewport: { width: vw, height: vh }, deviceScaleFactor: dpr,
        colorScheme: theme, reducedMotion: 'reduce',
    });
    for (const page of pages) {
        const tab = await ctx.newPage();
        await tab.goto(html + '#settings');
        await tab.evaluate((t) => document.documentElement.setAttribute('data-theme', t), theme);
        await tab.evaluate(([fn, w, h, id]) => {
            const frame = (0, eval)(`(${fn})`)({ w, h });
            document.querySelectorAll('.spage').forEach((p) => p.classList.toggle('active', p.dataset.page === id));
            document.querySelectorAll('#snav button').forEach((b) => b.setAttribute('aria-current', String(b.dataset.page === id)));
            document.getAnimations().forEach((a) => a.finish());
            return !!frame;
        }, [SCREENS.settings.prepare.toString(), width, height, page]);
        await tab.waitForTimeout(150);
        await tab.screenshot({ path: `${out}/${theme}-${page}.png`, clip: { x: 0, y: 0, width: vw, height: vh } });
        const items = await tab.evaluate((probes) => {
            const res = [];
            for (const [kind, sel, nameFn, text] of probes) {
                const fn = (0, eval)(`(${nameFn})`);
                document.querySelectorAll(sel).forEach((e) => {
                    let r = e.getBoundingClientRect();
                    if (text) { const g = document.createRange(); g.selectNodeContents(e); r = g.getBoundingClientRect(); }
                    if (r.width === 0 || r.height === 0) return;
                    const cs = getComputedStyle(e);
                    res.push({
                        kind, name: fn(e) ?? '', x: r.x, y: r.y, w: r.width, h: r.height,
                        radius: cs.borderTopLeftRadius, bg: cs.backgroundColor, color: cs.color, fontSize: cs.fontSize,
                    });
                });
            }
            return res;
        }, PROBES.map(([k, s, f, t]) => [k, s, f.toString(), !!t]));
        result.elements[`${theme}-${page}`] = items;
        await tab.close();
    }
    await ctx.close();
}
await browser.close();
writeFileSync(`${out}/elements.json`, JSON.stringify(result, null, 1));
console.log(`mock: ${themes.length * pages.length} 枚 -> ${out}`);
