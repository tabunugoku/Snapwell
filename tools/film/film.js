/* Snapwell film · 台本 (30 秒)。
 * 1) 導入: サイトのヒーローと同じ SnapIntro (点の場、窓 6 枚、3 × 2、ロゴ)。
 * 2) 投げる: 窓の動きは、サイトと同じ SnapStage (feel の Body2D と、DESIGN §5 のプリセット) をそのまま使う。
 * 3) 操作感のプリセット、ランチャー、Ctrl+Alt+A の整列、終わりのロゴ。
 * 時刻は clock.js の偽の時計で、手 (カーソル) の動きだけが台本。窓は手をばねで追い、離した速度から吸着先を決める。
 * 効果音のきっかけ (cues) は、台本の時刻と物理の事象 (離した時刻、止まった時刻) からここで集め、render.mjs が cues.json に書く。
 */
(function () {
  'use strict';
  const Feel = window.Feel;
  const { Stage } = window.SnapStage;
  const { Intro } = window.SnapIntro;
  const $ = (s) => document.querySelector(s);
  const $$ = (s) => [...document.querySelectorAll(s)];

  const cues = [];
  let clock = 0;
  const cue = (name, extra = {}) => cues.push({ t: Number(clock.toFixed(3)), name, ...extra });
  window.__cues = cues;

  /** 0..100 のばねで、要素の不透明度と移動を動かす。出入りの継続時間は書かない */
  function fader(el, dx = 0, dy = 24, preset = 'crisp') {
    const body = new Feel.Body({
      ...Feel.fromModal(window.SnapPresets.modal(window.SnapPresets.PRESETS[preset])), x: 0, target: 0, min: 0, max: 100,
      onFrame: (b) => {
        const r = b.x / 100;
        el.style.opacity = r.toFixed(3);
        el.style.transform = `translate(${((1 - r) * dx).toFixed(2)}px, ${((1 - r) * dy).toFixed(2)}px)`;
      },
    });
    return { in: () => body.to(100), out: () => body.to(0) };
  }

  const names = ['top left', 'top middle', 'top right', 'bottom left', 'bottom middle', 'bottom right'];
  const stage = new Stage($('#monitor'), {
    wins: [
      { title: 'Notes', color: 'var(--blue-tx)', zone: 0, lines: [82, 58, 90, 44, 70] },
      { title: 'Photos', color: 'var(--green-tx)', zone: 2, lines: [70, 92, 54, 76, 60] },
      { title: 'Mail', color: 'var(--yellow-tx)', zone: 4, lines: [88, 64, 50, 80, 58] },
      { title: 'Calc', color: 'var(--red-tx)', zone: 5, lines: [60, 84, 52, 72, 66] },
    ],
    cols: 3, rows: 2, gap: 10, feel: 'standard',
    onThrow: (t) => {
      $('#r-speed').textContent = `${Math.round(t.speed).toLocaleString('en-US')} px/s`;
      $('#r-zone').textContent = names[t.zone] || `zone ${t.zone + 1}`;
      cue('release', { speed: Math.round(t.speed) });
    },
    onLand: () => cue('land'),
  });
  const showFeel = () => {
    const p = stage.preset;
    $('#r-feel').textContent = `${p.label} · ${p.f.toFixed(2)} Hz · ζ ${p.z.toFixed(2)}`;
  };
  showFeel();

  const fx = {
    stage: fader($('#stagebox'), 0, 28, 'standard'),
    read: fader($('#readout'), 0, 12),
    cursor: fader($('#cursor'), 0, 0),
    launcher: fader($('#launcher'), 0, 0),
    opening: fader($('#opening'), 0, 0, 'standard'),
    end: fader($('#end'), 0, 20, 'standard'),
    c1: fader($('#c1')), c2: fader($('#c2')), c3: fader($('#c3')), c4: fader($('#c4')), c5: fader($('#c5')),
  };

  // ---- 手: カーソルは台本どおりに動き、窓はばねで追う ----
  const cur = $('#cursor');
  const hand = { x: 640, y: 380 };
  const setCursor = (x, y) => { hand.x = x; hand.y = y; cur.style.left = `${x}px`; cur.style.top = `${y}px`; };
  const smooth = (s) => { const c = Math.min(1, Math.max(0, s)); return c * c * (3 - 2 * c); };
  setCursor(hand.x, hand.y);
  let g = null;

  /** 窓を zone へ投げる。離す速さは、予測 (p + v·m/c) がちょうど届く値から決める */
  function throwWin(idx, zone, start, k = 0.9) {
    g = { w: stage.wins[idx], zone, t0: start, phase: 0, k };
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
  const PRESET_ROW = [['Left half', [0, 0, 0.5, 1]], ['Right half', [0.5, 0, 0.5, 1]], ['Top-left quarter', [0, 0, 0.5, 0.5]], ['Left third', [0, 0, 1 / 3, 1]],
    ['Right two-thirds', [1 / 3, 0, 2 / 3, 1]], ['Center third', [1 / 3, 0, 1 / 3, 1]], ['Center', [0.2, 0.2, 0.6, 0.6]], ['Maximize', [0, 0, 1, 1]]];
  const miniSvg = ([x, y, w, h]) => `<svg viewBox="0 0 18 12"><rect class="bg" x=".5" y=".5" width="17" height="11" rx="2"/>`
    + `<rect class="on" x="${(0.5 + x * 17).toFixed(1)}" y="${(0.5 + y * 11).toFixed(1)}" width="${(w * 17).toFixed(1)}" height="${(h * 11).toFixed(1)}" rx="1"/></svg>`;
  const panel = document.createElement('div');
  panel.className = 'lpanel';
  panel.innerHTML = '<div class="lpanel-top"><b id="lTarget">Photos</b><span id="lSpan">Drag across the grid</span></div>'
    + `<div class="presets">${PRESET_ROW.map(([n, r]) => `<span class="preset">${miniSvg(r)}${n}</span>`).join('')}</div>`;
  launcher.appendChild(panel);
  const selectSpan = (a, b) => {
    const c0 = Math.min(a.c, b.c); const c1 = Math.max(a.c, b.c);
    const r0 = Math.min(a.r, b.r); const r1 = Math.max(a.r, b.r);
    cells.forEach((q) => q.el.classList.toggle('sel', q.c >= c0 && q.c <= c1 && q.r >= r0 && q.r <= r1));
    $('#lSpan').textContent = `${c1 - c0 + 1} × ${r1 - r0 + 1} cells`;
    const p = cellRect(c0, r0); const q = cellRect(c1, r1);
    return { x: p.x, y: p.y, w: q.x + q.w - p.x, h: q.y + q.h - p.y };
  };
  const sweep = { t0: 0, from: { c: 2, r: 1 }, to: { c: 5, r: 3 }, dur: 1.3, on: false };
  const center = (c, r) => { const q = cellRect(c, r); return { x: q.x + q.w / 2, y: q.y + q.h / 2 }; };
  function tickSweep(t) {
    if (!sweep.on) return;
    const s = smooth((t - sweep.t0) / sweep.dur);
    const a = center(sweep.from.c, sweep.from.r);
    const b = center(sweep.to.c, sweep.to.r);
    const x = a.x + (b.x - a.x) * s;
    const y = a.y + (b.y - a.y) * s;
    setCursor(x, y);
    // 指のいる区画
    let best = cells[0]; let bd = Infinity;
    for (const q of cells) { const d = Math.hypot(center(q.c, q.r).x - x, center(q.c, q.r).y - y); if (d < bd) { bd = d; best = q; } }
    if (sweep.cur !== best) { sweep.cur = best; sweep.rect = selectSpan(sweep.from, best); if (s > 0.02 && s < 0.98) cue('cell'); }
  }

  // ---- 台本 ----
  const keyL = $$('#keysL .key');
  const keyEnter = keyL.pop(); // Enter は区画を選んだあとに押す
  const keyA = $$('#keysA .key');
  const events = [];
  const later = [];
  const at = (t, fn) => events.push({ t, fn, done: false });
  const after = (t, fn) => later.push({ t, fn });
  const logoDelay = (fn, s) => after(clock + s, fn);

  const arrange = () => {
    stage.cols = 2; stage.rows = 2;
    stage.layout();
    stage.wins.forEach((w, i) => after(clock + 0.09 * i, () => stage.arrangeTo(w, i)));
  };

  // 導入
  const opening = $('#opening');
  const cv = $('#introCanvas');
  const intro = new Intro(cv, {
    width: 1920, height: 1080, dpr: 1, scale: 2.2, seed: 11, veilMax: 0.97,
    colors: (() => {
      const cs = getComputedStyle(opening);
      const rgb = (c) => { const k = document.createElement('canvas').getContext('2d'); k.fillStyle = c; const v = k.fillStyle; return [1, 3, 5].map((i) => parseInt(v.slice(i, i + 2), 16)); };
      const v = (n) => cs.getPropertyValue(n).trim();
      return { accent: rgb(v('--intro-accent')), ink: rgb(v('--text')), bg: rgb(v('--bg')), surface: v('--surface'), surface2: v('--surface-2'), borderStrong: v('--border-strong'), skeleton: v('--skeleton') };
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

  at(8.9, () => { fx.opening.out(); cue('intro-out'); });
  at(9.1, () => { fx.stage.in(); fx.read.in(); cue('stage-in'); });
  at(9.4, () => fx.c1.in());
  at(9.9, () => throwWin(2, 1, 9.9));
  at(11.7, () => fx.c1.out());
  at(11.9, () => fx.c2.in());
  at(12.2, () => throwWin(3, 3, 12.2));
  at(14.0, () => fx.c2.out());
  at(14.2, () => fx.c3.in());
  at(14.5, () => { stage.setFeel('crisp'); showFeel(); cue('feel', { preset: 'crisp' }); throwWin(0, 4, 14.5); });
  at(16.5, () => { stage.setFeel('smooth'); showFeel(); cue('feel', { preset: 'smooth' }); throwWin(1, 5, 16.5); });
  at(18.5, () => { stage.setFeel('bouncy'); showFeel(); cue('feel', { preset: 'bouncy' }); throwWin(0, 0, 18.5); });
  at(20.4, () => { fx.c3.out(); fx.read.out(); stage.setFeel('standard'); });
  at(20.6, () => fx.c4.in());
  [21.0, 21.1, 21.2].forEach((t, i) => at(t, () => { keyL[i].classList.add('down'); cue('key'); }));
  at(21.5, () => { stage.raise(stage.wins[1]); fx.launcher.in(); cue('launcher-open'); });
  at(21.9, () => { setCursor(center(2, 1).x - 40, center(2, 1).y + 30); fx.cursor.in(); });
  at(22.1, () => { sweep.on = true; sweep.t0 = 22.1; selectSpan(sweep.from, sweep.from); cue('sweep-start'); });
  at(23.4, () => { sweep.on = false; });
  at(23.8, () => { keyEnter.classList.add('down'); cue('key'); });
  at(23.9, () => {
    cue('launcher-pick');
    fx.cursor.out();
    fx.launcher.out();
    stage.placeRect(stage.wins[1], sweep.rect);
  });
  at(25.0, () => { fx.c4.out(); keyL.forEach((k) => k.classList.remove('down')); keyEnter.classList.remove('down'); });
  at(25.2, () => fx.c5.in());
  [25.6, 25.7, 25.8].forEach((t, i) => at(t, () => { keyA[i].classList.add('down'); cue('key'); }));
  at(26.1, () => { arrange(); cue('arrange'); });
  at(27.6, () => fx.c5.out());
  at(27.8, () => fx.stage.out());
  at(28.0, () => { fx.end.in(); cue('end-in'); endLogo.show(logoDelay); });

  window.__onTick((t) => {
    const dt = lastT ? t - lastT : 1 / 60;
    lastT = t;
    clock = t;
    for (const ev of events) if (!ev.done && ev.t <= t) { ev.done = true; ev.fn(); }
    for (let i = later.length - 1; i >= 0; i--) if (later[i].t <= t) { later.splice(i, 1)[0].fn(); }
    tickHand(t);
    tickSweep(t);
    if (t < 9.6) { intro.step(dt); intro.draw(); }
  });
  window.__duration = 30;
})();
