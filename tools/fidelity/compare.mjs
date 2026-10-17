// モックアップ (shoot-mockup.mjs) と実機 (shoot-native.ps1) の出力を突き合わせ、差を測る。
// 使い方: node compare.mjs --mock out/mock --native out/native --out out/report [--tol 2] [--pixtol 24] [--hot 0.15]
//   1. 画像: 共通の大きさに切り、チャンネル差が pixtol を超える画素の割合とヒートマップを出す
//   2. 寸法: モックアップの要素と、実機の UIA の矩形を (種類, 名前) で対応づけ、位置・大きさの差が tol (論理 px) を超えるものを並べる
//   3. 要素ごとの画素差: 対応した要素の矩形の中の差の割合が hot を超えるものを並べる (ToggleSwitch の形のずれなど、寸法では出ない差)
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { PNG } from 'pngjs';

const args = Object.fromEntries(process.argv.slice(2).reduce((a, v, i, all) => {
    if (v.startsWith('--')) a.push([v.slice(2), all[i + 1]]);
    return a;
}, []));
const mockDir = resolve(args.mock ?? 'out/mock');
const nativeDir = resolve(args.native ?? 'out/native');
const out = resolve(args.out ?? 'out/report');
const tol = Number(args.tol ?? 2);
const pixtol = Number(args.pixtol ?? 24);
const hot = Number(args.hot ?? 0.15);
mkdirSync(out, { recursive: true });

const readJson = (p) => JSON.parse(readFileSync(p, 'utf8').replace(/^﻿/, ''));
const mock = readJson(`${mockDir}/elements.json`);
const dpr = mock.dpr;

// 実機の UIA の種類との対応。要素の見方が違うため、種類の集合で対応づける
const KIND_TO_UIA = {
    nav: ['ListItem'], navlabel: ['Text'], title: ['Text'], lead: ['Text'], rowtitle: ['Text'], rowdesc: ['Text'],
    group: ['Text'], switch: ['Button'], input: ['ComboBox', 'Edit', 'Spinner'], button: ['Button'], chip: ['Button', 'ListItem'],
};
// row は UIA に矩形が出ない (行の箱は Border) ので、寸法の対応はしない
const NO_UIA = new Set(['row']);
// 文字の UIA の矩形は、折り返す文では枠の幅 (文字の幅ではない) が入る。幅の差は見ない
const TEXT_KINDS = new Set(['navlabel', 'title', 'lead', 'rowtitle', 'rowdesc', 'group']);

const loadPng = (p) => PNG.sync.read(readFileSync(p));
const chanDiff = (a, i, b, j) => Math.max(Math.abs(a.data[i] - b.data[j]), Math.abs(a.data[i + 1] - b.data[j + 1]), Math.abs(a.data[i + 2] - b.data[j + 2]));

function diffImages(a, b) {
    const w = Math.min(a.width, b.width), h = Math.min(a.height, b.height);
    const heat = new PNG({ width: w, height: h });
    let bad = 0;
    const regions = { titlebar: [0, 0], nav: [0, 0], content: [0, 0] }; // [差のある画素, 全体]
    const navW = Math.round(232 * dpr), barH = Math.round(36 * dpr);
    for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
            const i = (y * a.width + x) * 4, j = (y * b.width + x) * 4, k = (y * w + x) * 4;
            const isBad = chanDiff(a, i, b, j) > pixtol;
            const r = y < barH ? regions.titlebar : x < navW ? regions.nav : regions.content;
            r[1]++;
            if (isBad) { bad++; r[0]++; }
            // 実機の画像を暗くして、差のある画素を赤く塗る
            const base = (b.data[j] + b.data[j + 1] + b.data[j + 2]) / 3 * 0.35;
            heat.data[k] = isBad ? 255 : base; heat.data[k + 1] = isBad ? 40 : base; heat.data[k + 2] = isBad ? 40 : base; heat.data[k + 3] = 255;
        }
    }
    const ratios = Object.fromEntries(Object.entries(regions).map(([k, v]) => [k, v[1] ? v[0] / v[1] : 0]));
    return { heat, ratio: bad / (w * h), regions: ratios };
}

// 矩形の中だけの差の割合。モックアップと実機は各自の左上を合わせ、小さいほうの大きさで比べる
function rectDiff(a, b, mr, nr) {
    const w = Math.floor(Math.min(mr.w, nr.w) * dpr), h = Math.floor(Math.min(mr.h, nr.h) * dpr);
    if (w < 1 || h < 1) return 0;
    const ax = Math.round(mr.x * dpr), ay = Math.round(mr.y * dpr), bx = Math.round(nr.x * dpr), by = Math.round(nr.y * dpr);
    let bad = 0, n = 0;
    for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
            const px = ax + x, py = ay + y, qx = bx + x, qy = by + y;
            if (px < 0 || py < 0 || qx < 0 || qy < 0 || px >= a.width || py >= a.height || qx >= b.width || qy >= b.height) continue;
            n++;
            if (chanDiff(a, (py * a.width + px) * 4, b, (qy * b.width + qx) * 4) > pixtol) bad++;
        }
    }
    return n ? bad / n : 0;
}

const pageKeys = Object.keys(mock.elements).filter((k) => existsSync(`${nativeDir}/${k}.png`));
if (pageKeys.length === 0) { console.error('対応する実機の画像がない'); process.exit(2); }
const nativeByTheme = {};
for (const t of new Set(pageKeys.map((k) => k.split('-')[0]))) nativeByTheme[t] = readJson(`${nativeDir}/${t}-elements.json`);

const report = { dpr, tol, pixtol, hot, pages: {} };
const pct = (v) => `${(v * 100).toFixed(1)} %`;
const head = [
    '# 忠実度の測定結果\n',
    `- dpr ${dpr}、位置と大きさの許容 ${tol} px (論理)、画素の許容 ${pixtol}/255、要素ごとの画素差の警告 ${(hot * 100).toFixed(0)} %\n`,
    '## 画像の差\n',
    '| 画面 | 差のある画素 | タイトルバー | 左の一覧 | 本文 |',
    '|---|---|---|---|---|',
];
const details = [];
for (const key of pageKeys) {
    const a = loadPng(`${mockDir}/${key}.png`), b = loadPng(`${nativeDir}/${key}.png`);
    const d = diffImages(a, b);
    writeFileSync(`${out}/${key}-heat.png`, PNG.sync.write(d.heat));
    head.push(`| ${key} | ${pct(d.ratio)} | ${pct(d.regions.titlebar)} | ${pct(d.regions.nav)} | ${pct(d.regions.content)} |`);

    const natAll = nativeByTheme[key.split('-')[0]].pages[key] ?? [];
    const used = new Set();
    const issues = [], unmatched = [];
    let matched = 0;
    for (const m of mock.elements[key]) {
        const types = KIND_TO_UIA[m.kind];
        if (!types || !m.name || NO_UIA.has(m.kind)) continue;
        const cands = natAll.map((n, idx) => ({ n, idx })).filter(({ n, idx }) => !used.has(idx) && types.includes(n.type) && n.name === m.name);
        if (cands.length === 0) { unmatched.push({ kind: m.kind, name: m.name }); continue; }
        // 同じ名前が複数あるとき (ページ名と一覧の項目など) は、位置が近いものを選ぶ
        cands.sort((p, q) => Math.hypot(p.n.x - m.x, p.n.y - m.y) - Math.hypot(q.n.x - m.x, q.n.y - m.y));
        const { n, idx } = cands[0];
        used.add(idx);
        matched++;
        const dx = n.x - m.x, dy = n.y - m.y, dw = n.w - m.w, dh = n.h - m.h;
        const px = rectDiff(a, b, m, n);
        const over = (TEXT_KINDS.has(m.kind) ? [dx, dy, dh] : [dx, dy, dw, dh]).some((v) => Math.abs(v) > tol);
        if (over || px > hot) {
            issues.push({ kind: m.kind, name: m.name, dx, dy, dw, dh, pixelDiff: px, mock: m, native: n, why: [over ? '寸法' : '', px > hot ? '画素' : ''].filter(Boolean).join('+') });
        }
    }
    report.pages[key] = { imageDiff: d.ratio, regions: d.regions, matched, issues, unmatched };
    details.push([key, issues, unmatched, matched]);
}
const body = [
    '\n画像の差には、モックアップと実機の内容そのものの違い (実機だけの「準備中」の札、保存バー、状態の文言など) も含まれる。どこが違うかはヒートマップ `*-heat.png` で見る。\n',
    '## 寸法と要素ごとの差 (許容を超えたもの)\n',
];
for (const [key, issues, unmatched, n] of details) {
    body.push(`### ${key} — 対応 ${n} 件、超過 ${issues.length} 件、対応なし ${unmatched.length} 件\n`);
    if (issues.length) {
        body.push('| 種類 | 名前 | Δx | Δy | Δw | Δh | 要素の画素差 | 理由 |', '|---|---|---|---|---|---|---|---|');
        for (const i of issues) {
            const f = (v, skip) => (skip ? `(${v.toFixed(0)})` : Math.abs(v) > tol ? `**${v.toFixed(1)}**` : v.toFixed(1));
            const t = TEXT_KINDS.has(i.kind);
            body.push(`| ${i.kind} | ${i.name.slice(0, 24)} | ${f(i.dx)} | ${f(i.dy)} | ${f(i.dw, t)} | ${f(i.dh)} | ${(i.pixelDiff * 100).toFixed(0)} % | ${i.why} |`);
        }
        body.push('');
    }
    if (unmatched.length) body.push(`対応なし (モックアップにあって実機の UIA に同名がない): ${unmatched.map((u) => `${u.kind}「${u.name.slice(0, 16)}」`).join('、')}\n`);
}
body.push('※ 角丸と色は UIA に出ない。モックアップ側の値は `elements.json` の radius / bg / color にある。実機側の形の差は、要素ごとの画素差とヒートマップで見つける。');
writeFileSync(`${out}/report.md`, [...head, ...body].join('\n'));
writeFileSync(`${out}/report.json`, JSON.stringify(report, null, 1));
console.log(head.join('\n'));
console.log(`\n-> ${out}/report.md`);
