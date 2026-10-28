/* Snapwell site · the opening piece, shared by the hero and the film.
 * A field of dots, six windows that burst apart, dance, and settle into a 3 × 2 grid, then three more
 * arrangements, then the logo. It is the first-run intro of docs/mockups/snapwell.html (playIntro), ported:
 * the windows move on the Standard and Bouncy springs, the dots on their own softer spring, and nothing has a
 * duration. A seeded random source and a fixed time step make every run identical, which the film relies on.
 */
(function (global) {
  'use strict';
  const P = global.SnapPresets;

  /** Small seeded generator (mulberry32), so the film renders the same frames every time. */
  function seeded(seed) {
    let a = seed >>> 0;
    return () => {
      a = (a + 0x6D2B79F5) >>> 0;
      let t = a;
      t = Math.imul(t ^ (t >>> 15), t | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  const LAYOUTS = [
    [[0, 0, 1 / 3, 0.5], [1 / 3, 0, 1 / 3, 0.5], [2 / 3, 0, 1 / 3, 0.5], [0, 0.5, 1 / 3, 0.5], [1 / 3, 0.5, 1 / 3, 0.5], [2 / 3, 0.5, 1 / 3, 0.5]],
    [[0, 0, 0.4, 0.5], [0, 0.5, 0.4, 0.5], [0.4, 0, 0.35, 0.5], [0.75, 0, 0.25, 0.5], [0.4, 0.5, 0.3, 0.5], [0.7, 0.5, 0.3, 0.5]],
    [[0, 0, 0.5, 1 / 3], [0.5, 0, 0.5, 1 / 3], [0, 1 / 3, 0.5, 1 / 3], [0.5, 1 / 3, 0.5, 1 / 3], [0, 2 / 3, 0.5, 1 / 3], [0.5, 2 / 3, 0.5, 1 / 3]],
    [[0, 0, 0.5, 1], [0.5, 0, 0.5, 0.5], [0.5, 0.5, 1 / 6, 0.5], [2 / 3, 0.5, 1 / 6, 0.5], [5 / 6, 0.5, 1 / 6, 0.25], [5 / 6, 0.75, 1 / 6, 0.25]],
  ];
  const N = 6;
  const GAP = 8;
  const STEP = 22; // dot pitch
  const KD = 140;
  const CD = 2 * 0.55 * Math.sqrt(KD);

  /** Times (s) of the beats, for the film's sound table and for the logo hand-off. */
  const BEATS = { enter: 0.5, burst: 1.9, grid: 3.0, layoutB: 3.9, layoutC: 4.8, final: 5.7, hop: 6.5, veil: 7.1, logo: 7.4 };

  class Intro {
    /**
     * @param {HTMLCanvasElement} canvas
     * @param {object} o  { width, height, dpr, colors, seed, veilMax, onBeat(name, t), beats, tail }
     * colors: { accent:[r,g,b], ink:[r,g,b], bg:[r,g,b], surface, surface2, borderStrong, skeleton }
     * beats: この回だけ BEATS の代わりに使う（ホームページは短い版を流す）。layoutB・layoutC が null なら、その並べ替えを省く
     * tail: logo の拍から done までの秒数（既定 2.4）
     */
    constructor(canvas, o) {
      this.cv = canvas;
      this.o = o;
      this.rand = seeded(o.seed ?? 7);
      this.B = o.beats || BEATS;
      this.tail = o.tail ?? 2.4;
      this.t = 0;
      this.setSize(o.width, o.height, o.dpr || 1);
      this.colors = o.colors;
      this.standard = P.spring(P.PRESETS.standard);
      this.bouncy = P.spring(P.PRESETS.bouncy);
      this.veilMax = o.veilMax ?? 0.97;
      this.veilX = 0; this.veilV = 0;
      this.build();
    }

    setSize(W, H, dpr) {
      this.W = W; this.H = H;
      this.cv.width = Math.round(W * dpr);
      this.cv.height = Math.round(H * dpr);
      this.g = this.cv.getContext('2d');
      this.g.setTransform(dpr, 0, 0, dpr, 0, 0);
      this.sc = this.o.scale ?? Math.min(1.6, Math.max(0.8, Math.min(W, H) / 380)); // chrome scales with the stage
      this.step_ = Math.round(STEP * Math.max(1, this.sc / 1.2));
      const M = Math.round(Math.min(W, H) * 0.07);
      this.M = M;
      this.SW = W - M * 2;
      this.SH = H - M * 2;
      this.cellW = this.SW / 3 - GAP;
      this.cellH = this.SH / 2 - GAP;
    }

    rectOf(L, i) {
      const [l, t, w, h] = LAYOUTS[L][i];
      const ww = w * this.SW - GAP;
      const hh = h * this.SH - GAP;
      return { x: this.M + l * this.SW + GAP / 2 + ww / 2, y: this.M + t * this.SH + GAP / 2 + hh / 2, w: ww, h: hh };
    }

    build() {
      const { W, H, cellW, cellH } = this;
      this.wins = Array.from({ length: N }, (_, i) => ({
        x: -cellW, y: H * 0.55, w: cellW * 1.15, h: cellH * 1.15, r: 0,
        vx: 0, vy: 0, vw: 0, vh: 0, vr: 0,
        tx: W / 2, ty: H / 2, tw: cellW, th: cellH, tr: 0,
        sp: this.standard, flash: 0, settled: true, show: i === 0, trail: [],
      }));
      this.dots = [];
      const ST = this.step_;
      for (let y = ST / 2; y < H; y += ST) for (let x = ST / 2; x < W; x += ST) this.dots.push({ x, y, dx: 0, dy: 0, vx: 0, vy: 0 });
      this.waves = [];
      this.script = [];
      this.beatSeen = new Set();
      const at = (t, fn) => this.script.push({ t, fn, done: false });
      const aim = (w, r, feel, vx, vy) => {
        w.tx = r.x; w.ty = r.y; w.tw = r.w; w.th = r.h; w.tr = r.r || 0;
        w.sp = feel;
        if (vx !== undefined) { w.vx = vx; w.vy = vy; }
        w.settled = false;
      };
      const wins = this.wins;
      const B = this.B;
      const beat = (name, fn) => at(B[name], () => { fn(); if (this.o.onBeat) this.o.onBeat(name, B[name]); });
      const rnd = (a, b) => a + this.rand() * (b - a);
      beat('enter', () => {
        const w = wins[0];
        w.show = true;
        aim(w, { x: W / 2, y: H / 2, w: cellW * 1.5, h: cellH * 1.5 }, this.standard, 1500, -200);
      });
      beat('burst', () => {
        wins.forEach((w, i) => {
          w.show = true;
          if (i > 0) { w.x = wins[0].x; w.y = wins[0].y; w.w = wins[0].w; w.h = wins[0].h; }
          const a = (i / N) * Math.PI * 2 + 0.4;
          const rr = Math.min(this.SW, this.SH) * 0.36;
          aim(w, { x: W / 2 + Math.cos(a) * rr * 1.5, y: H / 2 + Math.sin(a) * rr * 0.9, w: cellW * 0.62, h: cellH * 0.62, r: rnd(-0.28, 0.28) }, this.bouncy,
            Math.cos(a) * 900, Math.sin(a) * 700);
          w.vr = rnd(-6, 6);
        });
        this.waves.push({ x: W / 2, y: H / 2, t0: B.burst, amp: 18, speed: 420, w: 60, life: 2 });
      });
      beat('grid', () => wins.forEach((w, i) => aim(w, this.rectOf(0, i), this.standard, rnd(-250, 250), rnd(-250, 250))));
      [['layoutB', 1], ['layoutC', 2]].filter(([name]) => B[name] != null).forEach(([name, L]) => {
        wins.forEach((w, i) => at(B[name] + i * 0.07, () => aim(w, this.rectOf(L, i), this.standard)));
        at(B[name], () => { if (this.o.onBeat) this.o.onBeat(name, B[name]); });
      });
      wins.forEach((w, i) => at(B.final + i * 0.07, () => aim(w, this.rectOf(3, i), this.standard)));
      at(B.final, () => { if (this.o.onBeat) this.o.onBeat('final', B.final); });
      wins.forEach((w, k) => at(B.hop + k * 0.1, () => {
        w.vy = -520;
        w.flash = 1;
        this.waves.push({ x: w.tx, y: w.ty, t0: B.hop + k * 0.1, amp: 8, speed: 260, w: 40, life: 1.4 });
        if (this.o.onBeat) this.o.onBeat('hop', B.hop + k * 0.1, k);
      }));
      at(B.logo, () => { if (this.o.onBeat) this.o.onBeat('logo', B.logo); });
    }

    /** Jump to the finished picture: every window in the last arrangement, at rest, the logo's veil in place. */
    rest() {
      this.wins.forEach((w, i) => {
        const r = this.rectOf(3, i);
        Object.assign(w, { show: true, x: r.x, y: r.y, w: r.w, h: r.h, r: 0, vx: 0, vy: 0, vw: 0, vh: 0, vr: 0, tx: r.x, ty: r.y, tw: r.w, th: r.h, tr: 0, flash: 0, settled: true });
      });
      this.waves.length = 0;
      this.t = 99;
      this.veilX = this.veilMax; this.veilV = 0;
      this.script.forEach((s) => { s.done = true; });
      this.dots.forEach((p) => { p.dx = p.dy = p.vx = p.vy = 0; });
    }

    /** A ring from a point (the hero uses it for pointer clicks). */
    ping(x, y, amp = 9) { this.waves.push({ x, y, t0: this.t, amp, speed: 260, w: 36, life: 1.4 }); }

    /** Nudge the nearest window up a little, like the final hop. */
    poke(x, y) {
      let best = null; let bd = Infinity;
      for (const w of this.wins) { const d = Math.hypot(x - w.x, y - w.y); if (d < bd) { bd = d; best = w; } }
      if (best && bd < Math.max(best.w, best.h)) { best.vy = -520; best.flash = 1; }
    }

    get done() { return this.t > this.B.logo + this.tail; }
    get busy() {
      return this.t < this.B.logo + this.tail || this.waves.length > 0
        || this.wins.some((w) => !w.settled || Math.hypot(w.vx, w.vy) > 1);
    }

    step(dt) {
      this.t += dt;
      const t = this.t;
      // The logo's veil is a spring toward its level, not a timed fade.
      const vs = this.standard;
      const goal = t >= this.B.veil ? this.veilMax : 0;
      for (let acc = dt; acc > 1e-6;) {
        const h = Math.min(1 / 240, acc);
        acc -= h;
        this.veilV += ((-vs.k * (this.veilX - goal) - vs.c * this.veilV) / vs.m) * h;
        this.veilX += this.veilV * h;
      }
      for (const s of this.script) if (!s.done && t >= s.t) { s.done = true; s.fn(); }
      for (const w of this.wins) {
        if (!w.show) continue;
        const { k, c, m } = w.sp;
        const kc = 2 * Math.sqrt(k * m);
        for (let acc = dt; acc > 1e-6;) {
          const h = Math.min(1 / 240, acc);
          acc -= h;
          w.vx += ((-k * (w.x - w.tx) - c * w.vx) / m) * h;
          w.vy += ((-k * (w.y - w.ty) - c * w.vy) / m) * h;
          w.vr += ((-k * 0.5 * (w.r - w.tr) - c * 0.6 * w.vr) / m) * h;
          w.vw += ((-k * (w.w - w.tw) - kc * w.vw) / m) * h;
          w.vh += ((-k * (w.h - w.th) - kc * w.vh) / m) * h;
          w.x += w.vx * h; w.y += w.vy * h; w.r += w.vr * h; w.w += w.vw * h; w.h += w.vh * h;
        }
        w.flash = Math.max(0, w.flash - dt * 1.3);
        w.trail.push([w.x, w.y]);
        if (w.trail.length > 40) w.trail.shift();
        const dist = Math.hypot(w.x - w.tx, w.y - w.ty) + Math.abs(w.w - w.tw) + Math.abs(w.h - w.th);
        if (!w.settled && dist < 1.6 && Math.hypot(w.vx, w.vy) < 10) {
          w.settled = true;
          w.flash = 1;
          this.waves.push({ x: w.tx, y: w.ty, t0: t, amp: 9, speed: 250, w: 36, life: 1.4 });
          if (this.o.onBeat) this.o.onBeat('settle', t, this.wins.indexOf(w));
        }
      }
      for (let i = this.waves.length - 1; i >= 0; i--) if (t - this.waves[i].t0 > this.waves[i].life) this.waves.splice(i, 1);
      for (const p of this.dots) {
        let tx = 0; let ty = 0;
        for (const w of this.waves) {
          const wx = p.x - w.x; const wy = p.y - w.y;
          const r = Math.hypot(wx, wy) || 1;
          const age = t - w.t0;
          const kk = w.amp * Math.exp(-age * 1.5) * Math.exp(-Math.pow((r - w.speed * age) / w.w, 2));
          tx += (wx / r) * kk; ty += (wy / r) * kk;
        }
        if (!tx && !ty && !p.dx && !p.dy && !p.vx && !p.vy) continue;
        for (let acc = dt; acc > 1e-6;) {
          const h = Math.min(1 / 120, acc);
          acc -= h;
          p.vx += (KD * (tx - p.dx) - CD * p.vx) * h;
          p.vy += (KD * (ty - p.dy) - CD * p.vy) * h;
          p.dx += p.vx * h; p.dy += p.vy * h;
        }
        if (!tx && !ty && Math.hypot(p.dx, p.dy) < 0.02 && Math.hypot(p.vx, p.vy) < 0.05) p.dx = p.dy = p.vx = p.vy = 0;
      }
    }

    /** 0 → veilMax: how far the page background covers the windows while the logo shows. */
    get veil() {
      return Math.min(this.veilMax, Math.max(0, this.veilX));
    }

    draw() {
      const { g, W, H, t, colors: C } = this;
      const rgba = (c, a) => `rgba(${c[0]},${c[1]},${c[2]},${a})`;
      g.clearRect(0, 0, W, H);
      const reveal = t * 700;
      for (const p of this.dots) {
        const rv = Math.min(1, Math.max(0, (reveal - Math.hypot(p.x - W / 2, p.y - H / 2)) / 160)); // a front moving at 700 px/s
        if (rv <= 0) continue;
        const heat = Math.min(1, Math.hypot(p.dx, p.dy) / 10);
        g.fillStyle = heat > 0.1 ? rgba(C.accent, (0.25 + heat * 0.65) * rv) : rgba(C.ink, 0.2 * rv);
        g.beginPath();
        g.arc(p.x + p.dx, p.y + p.dy, (1.3 + heat * 1.8) * Math.max(1, this.sc / 1.2) * (0.5 + rv * 0.5), 0, Math.PI * 2);
        g.fill();
      }
      for (const wv of this.waves) {
        const age = t - wv.t0;
        g.strokeStyle = rgba(C.accent, 0.45 * Math.exp(-age * 1.8));
        g.lineWidth = 1.5;
        g.beginPath();
        g.arc(wv.x, wv.y, wv.speed * age, 0, Math.PI * 2);
        g.stroke();
      }
      const lead = this.wins[0];
      if (lead.show && t < this.B.burst + 0.3) {
        for (let i = 1; i < lead.trail.length; i++) {
          const q = i / lead.trail.length;
          g.strokeStyle = rgba(C.accent, q * 0.5);
          g.lineWidth = 1 + q * 5;
          g.lineCap = 'round';
          g.beginPath();
          g.moveTo(lead.trail[i - 1][0], lead.trail[i - 1][1]);
          g.lineTo(lead.trail[i][0], lead.trail[i][1]);
          g.stroke();
        }
      }
      const order = this.wins.filter((w) => w.show).sort((a, b) => (a.settled - b.settled));
      const sc = this.sc;
      for (const w of order) this.drawWin(w, sc, rgba);
      const veil = this.veil;
      if (veil > 0) {
        g.fillStyle = rgba(C.bg, veil);
        g.fillRect(0, 0, W, H);
      }
    }

    drawWin(w, sc, rgba) {
      const { g, colors: C } = this;
      const lift = Math.min(1, Math.hypot(w.vx, w.vy) / 700);
      const R = 9 * sc;
      g.save();
      g.translate(w.x, w.y);
      g.rotate(w.r);
      g.shadowColor = `rgba(0,0,0,${0.12 + lift * 0.2})`;
      g.shadowBlur = (8 + lift * 26) * sc;
      g.shadowOffsetY = (2 + lift * 12) * sc;
      g.fillStyle = C.surface;
      g.beginPath();
      g.roundRect(-w.w / 2, -w.h / 2, w.w, w.h, R);
      g.fill();
      g.shadowColor = 'transparent';
      g.save();
      g.beginPath();
      g.roundRect(-w.w / 2, -w.h / 2, w.w, w.h, R);
      g.clip();
      // Windows 11 title bar: icon and title at the left; minimise, maximise, close at the right.
      const tb = 24 * sc;
      const left = -w.w / 2;
      const top = -w.h / 2;
      g.fillStyle = C.surface2;
      g.fillRect(left, top, w.w, tb);
      g.fillStyle = rgba(C.accent, 0.9);
      g.beginPath();
      g.roundRect(left + 8 * sc, top + 7 * sc, 10 * sc, 10 * sc, 2.5 * sc);
      g.fill();
      g.fillStyle = C.skeleton;
      g.beginPath();
      g.roundRect(left + 24 * sc, top + 9.5 * sc, Math.min(w.w * 0.28, 70 * sc), 5 * sc, 2.5 * sc);
      g.fill();
      g.strokeStyle = rgba(C.ink, 0.55);
      g.lineWidth = sc;
      const bw = 28 * sc;
      const cx0 = left + w.w - bw * 3;
      g.beginPath();
      g.moveTo(cx0 + bw / 2 - 4 * sc, top + 12.5 * sc);
      g.lineTo(cx0 + bw / 2 + 4 * sc, top + 12.5 * sc);
      g.stroke();
      g.strokeRect(cx0 + bw + bw / 2 - 3.5 * sc, top + 8.5 * sc, 7 * sc, 7 * sc);
      const xc = cx0 + bw * 2 + bw / 2;
      g.beginPath();
      g.moveTo(xc - 4 * sc, top + 8.5 * sc); g.lineTo(xc + 4 * sc, top + 16.5 * sc);
      g.moveTo(xc + 4 * sc, top + 8.5 * sc); g.lineTo(xc - 4 * sc, top + 16.5 * sc);
      g.stroke();
      g.fillStyle = rgba(C.ink, 0.08);
      g.fillRect(left, top + tb - 1, w.w, 1);
      g.fillStyle = C.skeleton;
      const bars = [0.7, 0.48, 0.58, 0.4];
      for (let i = 0; i < bars.length; i++) {
        const by = -w.h / 2 + 38 * sc + i * 14 * sc;
        if (by + 7 * sc > w.h / 2 - 8 * sc) break;
        g.beginPath();
        g.roundRect(-w.w / 2 + 12 * sc, by, (w.w - 24 * sc) * bars[i], 7 * sc, 3.5 * sc);
        g.fill();
      }
      g.restore();
      // The outline goes last, over the title bar, so its glow has one width everywhere.
      g.lineWidth = 1.5 * sc;
      g.strokeStyle = w.flash > 0.02 ? rgba(C.accent, 0.35 + w.flash * 0.65) : C.borderStrong;
      g.beginPath();
      g.roundRect(-w.w / 2, -w.h / 2, w.w, w.h, R);
      g.stroke();
      g.restore();
    }
  }

  global.SnapIntro = { Intro, BEATS, seeded };
})(window);
