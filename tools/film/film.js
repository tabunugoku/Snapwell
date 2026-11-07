/* Snapwell film · 台本 (37.9 秒。下の時刻は導入を 1.9 秒伸ばす前のもので、導入のあとの時刻には D を足して動かす)。言語は ?lang=ja|en (文字は film-text.js)。時刻・動き・効果音のきっかけは言語に依存しない。
 *  0.0– 4.5 導入 (サイトのヒーローと同じ SnapIntro の短い版)
 *  4.5– 9.0 投げる (1 回) と操作感の切り替え
 *  9.0–13.5 ランチャー
 * 13.5–18.0 整列 (グリッド・横一列・メインとスタック)
 * 18.0–20.5 隣へ寄せる
 * 20.5–23.0 隣のモニターへ
 * 23.0–27.0 ホットキーとルール
 * 27.0–30.0 標準スナップの置き換え
 * 30.0–33.0 持ち運べる設定
 * 33.0–36.0 終わり
 * 窓の動きは SnapStage (feel の Body2D と DESIGN §5 のプリセット) をそのまま使い、出入りは feel のばね (継続時間は書かない)。
 * 手 (カーソル) の動きだけが台本。効果音のきっかけ (cues) は、台本の時刻と物理の事象 (離した時刻、止まった時刻) から集める。
 */
(function () {
  'use strict';
  const Feel = window.Feel;
  const { Stage } = window.SnapStage;
  const { Intro } = window.SnapIntro;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];

  const q = new URLSearchParams(location.search).get('lang');
  const lang = ['ja', 'zh-hans', 'zh-hant'].includes(q) ? q : 'en';
  const T = window.FILM_TEXT[lang];
  // html の lang は BCP 47 の綴り (zh-Hans / zh-Hant)。Chrome が中国語の字形を選ぶ手がかりになる
  document.documentElement.lang = { 'zh-hans': 'zh-Hans', 'zh-hant': 'zh-Hant' }[lang] || lang;
  $$('[data-t]').forEach((el) => { el.textContent = T[el.dataset.t]; });

  const cues = [];
  let clock = 0;
  const cue = (name, extra = {}) => cues.push({ t: Number(clock.toFixed(3)), name, ...extra });
  window.__cues = cues;
  window.__lang = lang;

  const P = window.SnapPresets;
  const spring = (preset, onFrame, hi = 100) => new Feel.Body({ ...Feel.fromModal(P.modal(P.PRESETS[preset])), x: 0, target: 0, min: 0, max: hi, onFrame });
  /** 0..100 のばねで、要素の不透明度と移動を動かす。出入りの継続時間は書かない */
  function fader(el, dx = 0, dy = 24, preset = 'crisp', shown = false) {
    const body = spring(preset, (b) => {
      const r = b.x / 100;
      el.style.opacity = r.toFixed(3);
      el.style.transform = `translate(${((1 - r) * dx).toFixed(2)}px, ${((1 - r) * dy).toFixed(2)}px)`;
    });
    if (shown) { body.x = 100; body.target = 100; } else { el.style.opacity = '0'; el.style.transform = `translate(${dx}px, ${dy}px)`; }
    return { in: () => body.to(100), out: () => body.to(0) };
  }
  /** 設定の面のスイッチ。つまみは Standard のばねで動く */
  function swtch(el) {
    const thumb = $('i', el);
    const body = spring('standard', (b) => {
      const f = Math.min(1, Math.max(0, b.x / 100));
      const ink = `color-mix(in srgb, var(--primary) ${(f * 100).toFixed(1)}%, var(--muted))`;
      el.style.borderColor = ink;
      el.style.background = `color-mix(in srgb, var(--primary) ${(f * 100).toFixed(1)}%, transparent)`;
      thumb.style.background = `color-mix(in srgb, var(--primary-text) ${(f * 100).toFixed(1)}%, var(--muted))`;
      thumb.style.transform = `translateX(${(f * 20).toFixed(2)}px)`;
    });
    body.onFrame(body);
    return { on: () => body.to(100), off: () => body.to(0) };
  }

  const stage = new Stage($('#monitor'), {
    wins: [
      { title: T.wins[0], color: 'var(--ico-1)', zone: 0, lines: [82, 58, 90, 44, 70] },
      { title: T.wins[1], color: 'var(--ico-2)', zone: 2, lines: [70, 92, 54, 76, 60] },
      { title: T.wins[2], color: 'var(--ico-3)', zone: 4, lines: [88, 64, 50, 80, 58] },
      { title: T.wins[3], color: 'var(--ico-1)', zone: 5, lines: [60, 84, 52, 72, 66] },
    ],
    cols: 3, rows: 2, gap: 10, feel: 'standard',
    onThrow: (t) => {
      $('#r-speed').textContent = `${Math.round(t.speed).toLocaleString('en-US')} px/s`;
      $('#r-zone').textContent = T.zones[t.zone] || `#${t.zone + 1}`;
      cue('release', { speed: Math.round(t.speed) });
    },
    onLand: () => { if (landCue) cue('land'); },
  });
  let landCue = true;
  const showFeel = () => {
    const p = stage.preset;
    $('#r-feel').textContent = `${T.feel[stage.feelName]} · ${p.f.toFixed(2)} Hz · ζ ${p.z.toFixed(2)}`;
  };
  showFeel();

  // 2 枚のモニター: 区画 2 つを画面として描く。窓は画面いっぱい (最大化) で、もう一方へ投げる
  const dual = new Stage($('#dual'), {
    wins: [{ title: T.wins[2], color: 'var(--ico-3)', zone: 0, lines: [88, 64, 50, 80, 58, 70] }],
    cols: 2, rows: 1, gap: 24, feel: 'standard',
    onThrow: () => cue('monitor'),
    onLand: () => cue('land'),
  });
  dual.rects.forEach((r, i) => {
    const s = document.createElement('div');
    s.className = 'screen';
    s.style.cssText = `left:${r.x - 8}px;top:${r.y - 8}px;width:${r.w + 16}px;height:${r.h + 16}px`;
    s.innerHTML = `<span class="screen-n">${i + 1}</span>`;
    dual.area.insertBefore(s, dual.area.firstChild);
  });

  const fx = {
    mon: fader($('#scMon'), 0, 28, 'standard'),
    dual: fader($('#scDual'), 0, 28, 'standard'),
    hot: fader($('#scHot'), 0, 28, 'standard'),
    rep: fader($('#scRep'), 0, 28, 'standard'),
    trav: fader($('#scTrav'), 0, 28, 'standard'),
    read: fader($('#readout'), 0, 12),
    cursor: fader($('#cursor'), 0, 0),
    launcher: fader($('#launcher'), 0, 0),
    contact: fader($('#contact'), 0, 0),
    opening: fader($('#opening'), 0, 0, 'standard', true),
    end: fader($('#end'), 0, 20, 'standard'),
  };
  [1, 2, 3, 4, 5, 6, 7, 8].forEach((n) => { fx[`c${n}`] = fader($(`#c${n}`)); });

  // ---- 手: カーソルは台本どおりに動き、窓はばねで追う ----
  const cur = $('#cursor');
  const hand = { x: 640, y: 380 };
  const setCursor = (x, y) => { hand.x = x; hand.y = y; cur.style.left = `${x}px`; cur.style.top = `${y}px`; };
  const smooth = (s) => { const c = Math.min(1, Math.max(0, s)); return c * c * (3 - 2 * c); };
  setCursor(hand.x, hand.y);
  let g = null;

  /** 窓を zone へ投げる。離す速さは、予測 (p + v·m/c) がちょうど届く値から決める */
  function throwWin(idx, zone, start, k = 0.9) {
    g = { w: stage.wins[idx], zone, t0: shift(start), phase: 0, k };
    fx.cursor.in();
    cue('grab');
  }

  function planGesture(gs) {
    const { w } = gs;
    const b = w.body;
    gs.b0 = { x: b.x, y: b.y };
    gs.tp = { x: b.x + w.bw.x * 0.45, y: b.y + 14 };
    gs.from = { x: hand.x, y: hand.y };
    const r = stage.rects[gs.zone];
    const dx = r.x - b.x; const dy = r.y - b.y;
    const dist = Math.hypot(dx, dy) || 1;
    gs.dir = { x: dx / dist, y: dy / dist };
    const tau = b.mass / b.damping;
    gs.T = 0.18;
    gs.L = ((dist / tau) * gs.k * gs.T) / 2;
    gs.tPress = 0.55; gs.tHold = gs.tPress + 0.14; gs.tRel = gs.tHold + gs.T;
  }

  function tickHand(t) {
    if (!g) return;
    if (g.phase === 0) { planGesture(g); g.phase = 1; }
    const e = t - g.t0;
    const { w, b0, tp, dir } = g;
    const tms = t * 1000;
    if (e < g.tPress) {
      const s = smooth(e / g.tPress);
      setCursor(g.from.x + (tp.x - g.from.x) * s, g.from.y + (tp.y - g.from.y) * s);
    } else if (e < g.tHold) {
      setCursor(tp.x, tp.y);
      if (g.phase === 1) { g.phase = 2; stage.raise(w); w.el.classList.add('held'); w.body.grab(b0.x, b0.y, tms); }
    } else if (e < g.tRel) {
      const s = (e - g.tHold) / g.T;
      const d = g.L * s * s;
      setCursor(tp.x + dir.x * d, tp.y + dir.y * d);
      w.body.move(b0.x + dir.x * d, b0.y + dir.y * d, tms);
    } else if (g.phase === 2) {
      g.phase = 3;
      w.el.classList.remove('held');
      w.body.release();
      g.end = { x: hand.x, y: hand.y };
      fx.cursor.out();
    } else {
      const s = smooth((e - g.tRel) / 0.6);
      setCursor(g.end.x + dir.x * 50 * s, g.end.y + dir.y * 50 * s);
      if (e > g.tRel + 0.7) g = null;
    }
  }

  // ---- ランチャー: 作業領域いっぱいの格子と、プリセットの帯 ----
  const LC = 6; const LR = 4; const LG = 6; const LE = 8;
  const launcher = $('#launcher');
  const cells = [];
  const cellRect = (c, r) => {
    const cw = (stage.W - 2 * LE - (LC - 1) * LG) / LC;
    const ch = (stage.H - 2 * LE - (LR - 1) * LG) / LR;
    return { x: LE + c * (cw + LG), y: LE + r * (ch + LG), w: cw, h: ch };
  };
  for (let r = 0; r < LR; r++) {
    for (let c = 0; c < LC; c++) {
      const el = document.createElement('i');
      el.className = 'cell';
      const q = cellRect(c, r);
      el.style.cssText = `left:${q.x}px;top:${q.y}px;width:${q.w}px;height:${q.h}px`;
      launcher.appendChild(el);
      cells.push({ el, c, r });
    }
  }
  const PRESET_RECTS = [[0, 0, 0.5, 1], [0.5, 0, 0.5, 1], [0, 0, 0.5, 0.5], [0, 0, 1 / 3, 1], [1 / 3, 0, 2 / 3, 1], [1 / 3, 0, 1 / 3, 1], [0.2, 0.2, 0.6, 0.6], [0, 0, 1, 1]];
  const miniSvg = ([x, y, w, h]) => `<svg viewBox="0 0 18 12"><rect class="bg" x=".5" y=".5" width="17" height="11" rx="2"/>`
    + `<rect class="on" x="${(0.5 + x * 17).toFixed(1)}" y="${(0.5 + y * 11).toFixed(1)}" width="${(w * 17).toFixed(1)}" height="${(h * 11).toFixed(1)}" rx="1"/></svg>`;
  const panel = document.createElement('div');
  panel.className = 'lpanel';
  panel.innerHTML = `<div class="lpanel-top"><b>${T.wins[1]}</b><span id="lSpan">${T.lDrag}</span></div>`
    + `<div class="presets">${PRESET_RECTS.map((r, i) => `<span class="preset">${miniSvg(r)}${T.presets[i]}</span>`).join('')}</div>`;
  launcher.appendChild(panel);
  const selectSpan = (a, b) => {
    const c0 = Math.min(a.c, b.c); const c1 = Math.max(a.c, b.c);
    const r0 = Math.min(a.r, b.r); const r1 = Math.max(a.r, b.r);
    cells.forEach((q) => q.el.classList.toggle('sel', q.c >= c0 && q.c <= c1 && q.r >= r0 && q.r <= r1));
    $('#lSpan').textContent = T.lCells(c1 - c0 + 1, r1 - r0 + 1);
    const p = cellRect(c0, r0); const q = cellRect(c1, r1);
    return { x: p.x, y: p.y, w: q.x + q.w - p.x, h: q.y + q.h - p.y };
  };
  const sweep = { t0: 0, from: { c: 2, r: 1 }, to: { c: 5, r: 3 }, dur: 1.0, on: false };
  const center = (c, r) => { const q = cellRect(c, r); return { x: q.x + q.w / 2, y: q.y + q.h / 2 }; };
  function tickSweep(t) {
    if (!sweep.on) return;
    const s = smooth((t - sweep.t0) / sweep.dur);
    const a = center(sweep.from.c, sweep.from.r);
    const b = center(sweep.to.c, sweep.to.r);
    const x = a.x + (b.x - a.x) * s;
    const y = a.y + (b.y - a.y) * s;
    setCursor(x, y);
    let best = cells[0]; let bd = Infinity;
    for (const q of cells) { const d = Math.hypot(center(q.c, q.r).x - x, center(q.c, q.r).y - y); if (d < bd) { bd = d; best = q; } }
    if (sweep.cur !== best) { sweep.cur = best; sweep.rect = selectSpan(sweep.from, best); if (s > 0.02 && s < 0.98) cue('cell'); }
  }

  // ---- 設定の面 ----
  const kbd = (ks) => ks.map((k) => `<kbd>${k}</kbd>`).join('');
  const HK = [['Ctrl', 'Alt', 'Space'], ['Ctrl', 'Alt', 'A'], ['Ctrl', 'Alt', 'PageDown'], ['Ctrl', 'Alt', 'Pause']];
  $('#paneHot').innerHTML = `<div class="p-head"><b>${T.hHot}</b><span class="p-sub">${T.hHotSub}</span></div>`
    + T.hk.map((l, i) => `<div class="row" id="hk${i}"><span class="lab">${l}</span><span class="kb" id="kb${i}">${kbd(HK[i])}</span></div>`).join('')
    + `<div class="p-sep"></div><div class="p-head"><b>${T.hRules}</b></div>`
    + T.rules.map(([n, d], i) => `<div class="row" id="rule${i}"><i class="ico-s" style="background:var(--ico-${i + 1})"></i><span class="lab">${n}</span><span class="sub">${d}</span><span class="sw" id="rsw${i}"><i></i></span></div>`).join('');
  $('#paneRep').innerHTML = `<div class="row hl"><span class="lab"><b>${T.rep}</b><br><span class="sub">${T.repSub}</span></span><span class="sw" id="repSw"><i></i></span></div>`
    + `<div class="p-sep"></div>`
    + T.cmpA.map((a, i) => `<div class="cmp" id="cmp${i}"><span>${a}<span class="sub"> · ${T.cmpFrom[i]}</span></span><span class="arr">→</span><span class="to">${T.cmpTo[i]}</span></div>`).join('')
    + `<div class="note" id="repNote">${T.repNote}</div><div class="row" id="repOk"><span class="chip ok">${T.restored}</span></div>`;
  $('#paneTrav').innerHTML = `<div class="row"><span class="lab">${T.hFile}</span><span class="btn" id="bExp">${T.exp}</span><span class="btn">${T.imp}</span></div>`
    + `<div class="row" id="fileRow"><span class="lab"></span><span class="path" style="flex:none;width:260px">${T.fileName}</span></div>`
    + `<div class="p-sep"></div>`
    + `<div class="row"><span class="lab">${T.hSync}</span><span class="path">Documents\\Sync\\Snapwell</span><span class="chip ok" id="synced">${T.synced}</span></div>`
    + `<div class="p-sep"></div>`
    + `<div class="row"><span class="lab">${T.hLang}</span><span class="seg"><span class="chip${lang !== 'en' ? ' on' : ''}" id="lgJa">${T.lgName || '日本語'}</span><span class="chip${lang === 'en' ? ' on' : ''}" id="lgEn">English</span></span></div>`
    + `<div class="row"><span class="lab">${T.hMotion}</span><span class="sw" id="msw"><i></i></span></div>`;
  const sw = { r0: swtch($('#rsw0')), r1: swtch($('#rsw1')), rep: swtch($('#repSw')), motion: swtch($('#msw')) };
  const hide = (sel, dy = 8) => fader($(sel), 0, dy);
  const f = {
    cmp0: hide('#cmp0'), cmp1: hide('#cmp1'), note: hide('#repNote'), ok: hide('#repOk'),
    file: hide('#fileRow'), synced: hide('#synced', 0),
  };

  // ---- 台本 ----
  const events = [];
  const later = [];
  // 導入 (ロゴが出て止まるまで) を D 秒だけ長くしてある。導入の終わり (4.15 秒) より後の時刻は、台本に書いた秒に D を足して動かす
  const D = 1.9;
  const shift = (t) => (t >= 4.15 ? t + D : t);
  const at = (t, fn) => events.push({ t: shift(t), fn, done: false });
  const after = (t, fn) => later.push({ t, fn });
  const logoDelay = (fn, s) => after(clock + s, fn);
  const keysOf = (sel) => $$(`${sel} .key`);
  const press = (list, t0, step = 0.1) => list.forEach((k, i) => at(t0 + i * step, () => { k.classList.add('down'); cue('key'); }));
  const release = (list, t) => at(t, () => list.forEach((k) => k.classList.remove('down')));

  // 窓を、いまの動きの途中でも向きを変えられるように置く (placeRect は置いている途中に重ねると力の組を失う)
  const place = (s, w, r) => {
    if (w.saved) { w.body.forces = w.saved; w.saved = null; }
    s.placeRect(w, r);
  };
  const GAP = 10;
  const inner = () => ({ x: GAP, y: GAP, w: stage.W - 2 * GAP, h: stage.H - 2 * GAP });
  const layouts = {
    grid: () => { const i = inner(); const w = (i.w - GAP) / 2; const h = (i.h - GAP) / 2; return [0, 1, 2, 3].map((k) => ({ x: i.x + (k % 2) * (w + GAP), y: i.y + Math.floor(k / 2) * (h + GAP), w, h })); },
    row: () => { const i = inner(); const w = (i.w - 3 * GAP) / 4; return [0, 1, 2, 3].map((k) => ({ x: i.x + k * (w + GAP), y: i.y, w, h: i.h })); },
    main: () => {
      const i = inner(); const mw = Math.round(i.w * 0.58); const sw2 = i.w - mw - GAP; const sh = (i.h - 2 * GAP) / 3;
      return [{ x: i.x, y: i.y, w: mw, h: i.h }, ...[0, 1, 2].map((k) => ({ x: i.x + mw + GAP, y: i.y + k * (sh + GAP), w: sw2, h: sh }))];
    },
  };
  const arrangeWith = (name) => { const rs = layouts[name](); stage.wins.forEach((w, i) => after(clock + 0.09 * i, () => place(stage, w, rs[i]))); };

  // 導入
  const opening = $('#opening');
  const cv = $('#introCanvas');
  const INTRO_BEATS = { enter: 0.15, burst: 0.75, grid: 1.45, layoutB: null, layoutC: null, final: 2.1, hop: 2.9, veil: 3.4, logo: 3.55 }; // site/js/home.js の短い版と同じ
  const intro = new Intro(cv, {
    width: 1920, height: 1080, dpr: 1, scale: 2.2, seed: 11, veilMax: 0.97, beats: INTRO_BEATS, tail: 2.4,
    colors: (() => {
      const cs = getComputedStyle(document.documentElement);
      const rgb = (c) => { const k = document.createElement('canvas').getContext('2d'); k.fillStyle = c; const v = k.fillStyle; return [1, 3, 5].map((i) => parseInt(v.slice(i, i + 2), 16)); };
      const v = (n) => cs.getPropertyValue(n).trim();
      return { accent: rgb(v('--accent')), ink: rgb(v('--fg')), bg: rgb(v('--bg')), surface: v('--surface'), surface2: v('--surface-2'), borderStrong: v('--border-strong'), skeleton: v('--skeleton') };
    })(),
    onBeat: (name, t, k) => {
      if (name === 'settle') return;
      cue(`intro-${name}`, k === undefined ? {} : { k });
      if (name === 'logo') { introLogo.show(logoDelay); }
    },
  });
  const introLogo = window.Brand.logo($('#introLogo'));
  const endLogo = window.Brand.logo($('#endLogo'));
  let lastT = 0;

  at(4.2, () => { fx.opening.out(); cue('intro-out'); });

  // 投げる 4.5–9.0
  at(4.4, () => { fx.mon.in(); fx.read.in(); cue('stage-in'); });
  at(4.7, () => fx.c1.in());
  at(5.0, () => throwWin(2, 1, 5.0));
  at(7.0, () => { stage.setFeel('bouncy'); showFeel(); cue('feel', { preset: 'bouncy' }); throwWin(3, 3, 7.0); });
  at(8.7, () => { fx.c1.out(); fx.read.out(); });

  // ランチャー 9.0–13.5
  const k2 = keysOf('#c2'); const k2enter = k2.pop();
  at(9.0, () => { stage.setFeel('standard'); fx.c2.in(); });
  press(k2, 9.4);
  at(9.9, () => { stage.raise(stage.wins[1]); fx.launcher.in(); cue('launcher-open'); });
  at(10.2, () => { setCursor(center(2, 1).x - 40, center(2, 1).y + 30); fx.cursor.in(); });
  at(10.4, () => { sweep.on = true; sweep.t0 = shift(10.4); selectSpan(sweep.from, sweep.from); cue('sweep-start'); });
  at(11.45, () => { sweep.on = false; });
  at(11.9, () => { k2enter.classList.add('down'); cue('key'); });
  at(12.0, () => {
    cue('launcher-pick');
    fx.cursor.out();
    fx.launcher.out();
    landCue = false;
    place(stage, stage.wins[1], sweep.rect);
  });
  at(13.3, () => { fx.c2.out(); k2.forEach((k) => k.classList.remove('down')); k2enter.classList.remove('down'); });

  // 整列 13.5–18.0
  const k3 = keysOf('#c3');
  at(13.5, () => fx.c3.in());
  press([k3[0], k3[1], k3[3]], 13.9);
  at(14.4, () => { arrangeWith('grid'); cue('arrange'); $('#c3p').textContent = T.c3a; });
  release(k3, 14.9);
  at(15.4, () => { $('#c3shift').classList.remove('off'); $('#c3k').textContent = 'H'; $('#c3p').textContent = T.c3b; });
  press(k3, 15.5, 0.08);
  at(16.0, () => { arrangeWith('row'); cue('arrange'); });
  release(k3, 16.6);
  at(16.8, () => { $('#c3k').textContent = 'M'; $('#c3p').textContent = T.c3c; });
  press(k3, 16.9, 0.08);
  at(17.1, () => { arrangeWith('main'); cue('arrange'); });
  at(18.0, () => { k3.forEach((k) => k.classList.remove('down')); fx.c3.out(); fx.mon.out(); });

  // 隣へ寄せる 18.0–20.5 (モニターが引いている間に 2 枚の配置へ組み直す)
  const k4 = keysOf('#c4');
  let nudgeTo = null;
  at(18.35, () => {
    const W = stage.W; const H = stage.H;
    stage.wins.forEach((w, i) => { w.el.style.visibility = i < 2 ? 'visible' : 'hidden'; if (w.saved) { w.body.forces = w.saved; w.saved = null; } });
    stage.jumpRect(stage.wins[0], { x: GAP + 10, y: 56, w: Math.round(W * 0.32), h: Math.round(H * 0.6) });
    stage.jumpRect(stage.wins[1], { x: Math.round(W * 0.56), y: 24, w: Math.round(W * 0.38), h: Math.round(H * 0.7) });
    stage.raise(stage.wins[0]);
    const a = stage.wins[0]; const b = stage.wins[1];
    nudgeTo = { x: b.body.x - GAP - a.bw.x, y: a.body.y, w: a.bw.x, h: a.bh.x };
    fx.mon.in();
  });
  at(18.6, () => fx.c4.in());
  press(k4, 19.0, 0.1);
  at(19.5, () => { cue('nudge'); landCue = false; place(stage, stage.wins[0], nudgeTo); });
  at(20.2, () => {
    const a = stage.wins[0]; const b = stage.wins[1];
    const top = Math.max(a.body.y, b.body.y); const bot = Math.min(a.body.y + a.bh.x, b.body.y + b.bh.x);
    Object.assign($('#contact').style, { left: `${b.body.x - GAP / 2 - 1}px`, top: `${top}px`, height: `${bot - top}px` });
    fx.contact.in();
  });
  at(20.5, () => { fx.c4.out(); fx.mon.out(); k4.forEach((k) => k.classList.remove('down')); });

  // 隣のモニターへ 20.5–23.0
  const k5 = keysOf('#c5');
  at(20.55, () => { fx.dual.in(); fx.c5.in(); });
  press(k5, 20.8, 0.1);
  at(21.2, () => dual.throwTo(dual.wins[0], 1));
  at(22.9, () => { fx.c5.out(); fx.dual.out(); k5.forEach((k) => k.classList.remove('down')); });

  // ホットキーとルール 23.0–27.0
  at(23.05, () => { fx.hot.in(); fx.c6.in(); });
  at(23.8, () => { const r = $('#hk3'); r.classList.add('hl'); $('#kb3').classList.add('rec'); $('#kb3').innerHTML = `<span class="msg">${T.press}</span>`; cue('key'); });
  at(24.6, () => { $('#kb3').innerHTML = kbd(['Ctrl', 'Alt', 'P']); cue('key'); });
  at(24.9, () => { $('#kb3').classList.remove('rec'); $('#hk3').classList.remove('hl'); });
  at(25.3, () => { sw.r0.on(); $('#rule0').classList.add('hl'); cue('toggle'); });
  at(25.9, () => { sw.r1.on(); $('#rule1').classList.add('hl'); cue('toggle'); });
  at(26.9, () => { fx.c6.out(); fx.hot.out(); });

  // 標準スナップの置き換え 27.0–30.0
  at(27.05, () => { fx.rep.in(); fx.c7.in(); });
  at(27.7, () => { sw.rep.on(); cue('toggle'); f.cmp0.in(); });
  at(27.85, () => f.cmp1.in());
  at(28.3, () => f.note.in());
  at(29.0, () => { sw.rep.off(); cue('toggle'); });
  at(29.2, () => { f.ok.in(); cue('restore'); });
  at(29.9, () => { fx.c7.out(); fx.rep.out(); });

  // 持ち運べる設定 30.0–33.0
  at(30.05, () => { fx.trav.in(); fx.c8.in(); });
  at(30.6, () => { $('#bExp').classList.add('down'); cue('export'); });
  at(30.85, () => { $('#bExp').classList.remove('down'); f.file.in(); });
  at(31.5, () => { f.synced.in(); cue('sync'); });
  at(32.0, () => { $('#lgJa').classList.toggle('on'); $('#lgEn').classList.toggle('on'); cue('feel', { preset: 'lang' }); });
  at(32.9, () => { fx.c8.out(); fx.trav.out(); });

  // 終わり 33.0–36.0
  at(33.0, () => { fx.end.in(); cue('end-in'); endLogo.show(logoDelay); });

  window.__onTick((t) => {
    const dt = lastT ? t - lastT : 1 / 60;
    lastT = t;
    clock = t;
    for (const ev of events) if (!ev.done && ev.t <= t) { ev.done = true; ev.fn(); }
    for (let i = later.length - 1; i >= 0; i--) if (later[i].t <= t) { later.splice(i, 1)[0].fn(); }
    tickHand(t);
    tickSweep(t);
    if (t < 4.6 + D) { intro.step(dt); intro.draw(); }
  });
  window.__duration = 36 + D;
})();
