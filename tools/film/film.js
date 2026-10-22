/* Snapwell film · 台本。
 * 窓の動きは、サイトと同じ SnapStage (feel の Body2D) をそのまま使う。時刻は clock.js の偽の時計で、
 * 手 (カーソル) の動きだけが台本で、窓は手をばねで追い、離した速度から吸着先を決める。
 */
(function () {
  'use strict';
  const Feel = window.Feel;
  const { Stage } = window.SnapStage;
  const $ = (s) => document.querySelector(s);

  /** 0..100 のばねで、要素の不透明度と移動を動かす。出入りの継続時間は書かない */
  function fader(el, dx = 0, dy = 24, feel = 'snappy') {
    const body = new Feel.Body({
      feel, x: 0, target: 0, min: 0, max: 100,
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
    cols: 3, rows: 2, gap: 10, feel: 'airy',
    onThrow: (t) => {
      $('#r-speed').textContent = `${Math.round(t.speed).toLocaleString('en-US')} px/s`;
      $('#r-zone').textContent = names[t.zone] || `zone ${t.zone + 1}`;
    },
  });
  const showFeel = () => {
    const w = stage.words;
    $('#r-feel').textContent = `${w.words.join(', ')} · ${Feel.metrics(w.params).freq.toFixed(2)} Hz`;
  };
  showFeel();

  const fx = {
    title: fader($('#title'), 0, 20, 'airy'),
    end: fader($('#end'), 0, 20, 'airy'),
    stage: fader($('#stagebox'), 0, 28, 'airy'),
    read: fader($('#readout'), 0, 12),
    cursor: fader($('#cursor'), 0, 0),
    c1: fader($('#c1')), c2: fader($('#c2')), c3: fader($('#c3')), c4: fader($('#c4')),
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

  // ---- 台本 ----
  const keyEls = [...document.querySelectorAll('.key')];
  const scatter = () => {
    const rects = [
      { x: 70, y: 36, w: 300, h: 190 }, { x: 300, y: 90, w: 270, h: 210 },
      { x: 470, y: 200, w: 290, h: 190 }, { x: 110, y: 250, w: 250, h: 170 },
    ];
    stage.clearFx();
    stage.wins.forEach((w, i) => { stage.jumpRect(w, rects[i]); stage.raise(w); });
  };

  let clock = 0;
  const events = [];
  const later = [];
  const at = (t, fn) => events.push({ t, fn, done: false });
  const after = (t, fn) => later.push({ t, fn });

  const arrange = () => {
    stage.cols = 2; stage.rows = 2;
    stage.layout();
    stage.wins.forEach((w, i) => after(clock + 0.09 * i, () => stage.arrangeTo(w, i)));
  };

  at(0.1, () => fx.title.in());
  at(2.5, () => fx.title.out());
  at(2.6, () => { fx.stage.in(); fx.read.in(); });
  at(2.9, () => fx.c1.in());
  at(3.5, () => throwWin(2, 1, 3.5));
  at(6.1, () => fx.c1.out());
  at(6.3, () => fx.c2.in());
  at(6.6, () => throwWin(3, 3, 6.6));
  at(9.7, () => fx.c2.out());
  at(9.9, () => { fx.c3.in(); stage.setFeel('heavy'); showFeel(); });
  at(10.3, () => throwWin(0, 4, 10.3));
  at(13.5, () => { stage.setFeel('light'); showFeel(); });
  at(13.9, () => throwWin(1, 5, 13.9));
  at(16.4, () => { fx.c3.out(); fx.read.out(); });
  at(16.9, () => { scatter(); fx.c4.in(); });
  at(18.3, () => keyEls.forEach((k, i) => after(clock + 0.12 * i, () => k.classList.add('down'))));
  at(18.9, () => arrange());
  at(22.2, () => { fx.c4.out(); fx.stage.out(); });
  at(22.7, () => fx.end.in());

  window.__onTick((t) => {
    clock = t;
    for (const ev of events) if (!ev.done && ev.t <= t) { ev.done = true; ev.fn(); }
    for (let i = later.length - 1; i >= 0; i--) if (later[i].t <= t) { later.splice(i, 1)[0].fn(); }
    tickHand(t);
  });
  window.__duration = 26;
})();
