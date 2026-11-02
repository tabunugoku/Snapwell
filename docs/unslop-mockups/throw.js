// 投げて吸い込まれる小さな舞台（操作感の「試す」と、ようこその導入で使う）
// 物理は DESIGN §5 の要点だけを写す: 予測点 p = x + v·m/c、150 px/s 未満は吸着しない、
// 目標へのばね k = m(2πf)²、c = 2ζ√(km)。サイズは着地したときに 1 回だけ変える
(function () {
  'use strict';
  const PRESETS = {
    standard: { m: 0.6, f: 2.4, zeta: 0.7 },
    crisp: { m: 0.8, f: 3.2, zeta: 0.6 },
    smooth: { m: 1.0, f: 1.8, zeta: 0.8 },
    bouncy: { m: 0.5, f: 2.8, zeta: 0.45 },
  };
  const GAP = 6;

  function ThrowStage(host, options) {
    this.o = Object.assign({ mode: 'zones', preset: 'standard', cardW: 212, cardH: 140, title: 'Window', onHint: null, showLines: true, showTrace: true, showPreview: true }, options);
    host.classList.add('throw-stage');
    host.innerHTML =
      '<svg class="throw-overlay" aria-hidden="true"><rect class="tz" rx="8"/><rect class="tz-line" rx="8"/><line class="trace"/><rect class="stop-mark" width="8" height="8" rx="2"/></svg>' +
      '<div class="throw-card" tabindex="0" role="group"><div class="throw-title"><span class="throw-dot"></span><span class="throw-name"></span></div>' +
      '<div class="throw-body"><i></i><i></i><i></i></div></div>';
    this.host = host;
    this.card = host.querySelector('.throw-card');
    this.card.querySelector('.throw-name').textContent = this.o.title;
    this.svg = host.querySelector('svg');
    this.zone = host.querySelector('.tz'); this.zoneLine = host.querySelector('.tz-line');
    this.trace = host.querySelector('.trace'); this.mark = host.querySelector('.stop-mark');
    this.w = this.o.cardW; this.h = this.o.cardH;
    this.x = 0; this.y = 0; this.vx = 0; this.vy = 0; this.target = null; this.raf = 0; this.samples = [];
    this.layout();
    this.x = (this.W - this.w) / 2; this.y = (this.H - this.h) / 2 - 8;
    this.draw(); this.hide();
    this.bind();
    new ResizeObserver(() => { this.layout(); this.clamp(); this.draw(); }).observe(host);
  }

  ThrowStage.prototype.layout = function () { this.W = this.host.clientWidth; this.H = this.host.clientHeight; };
  ThrowStage.prototype.clamp = function () {
    this.x = Math.max(0, Math.min(this.W - this.w, this.x)); this.y = Math.max(0, Math.min(this.H - this.h, this.y));
  };
  ThrowStage.prototype.feel = function () {
    const p = PRESETS[this.o.preset] || PRESETS.standard;
    const k = p.m * Math.pow(2 * Math.PI * p.f, 2);
    return { m: p.m, k, c: 2 * p.zeta * Math.sqrt(k * p.m) };
  };

  // 吸着先: 区画（左右の半分と四分割。上下 30% の帯で四分割）または四隅（サイズ維持）
  ThrowStage.prototype.pick = function (cx, cy) {
    const W = this.W, H = this.H;
    if (this.o.mode === 'corners') {
      const right = cx > W / 2, bottom = cy > H / 2;
      return { x: right ? W - this.w - GAP : GAP, y: bottom ? H - this.h - GAP : GAP, w: this.w, h: this.h, keep: true };
    }
    const right = cx > W / 2;
    const left = right ? W / 2 + GAP / 2 : GAP;
    const width = W / 2 - GAP * 1.5;
    if (cy < H * 0.3) return { x: left, y: GAP, w: width, h: H / 2 - GAP * 1.5 };
    if (cy > H * 0.7) return { x: left, y: H / 2 + GAP / 2, w: width, h: H / 2 - GAP * 1.5 };
    return { x: left, y: GAP, w: width, h: H - GAP * 2 };
  };

  ThrowStage.prototype.bind = function () {
    const title = this.card.querySelector('.throw-title');
    let dragging = false, offX = 0, offY = 0;
    title.addEventListener('pointerdown', e => {
      dragging = true; title.setPointerCapture(e.pointerId);
      cancelAnimationFrame(this.raf); this.raf = 0; this.target = null; this.hide();
      const r = this.host.getBoundingClientRect();
      offX = e.clientX - r.left - this.x; offY = e.clientY - r.top - this.y; this.samples = [];
      this.card.classList.add('is-held'); this.hint('held');
    });
    title.addEventListener('pointermove', e => {
      if (!dragging) return;
      const r = this.host.getBoundingClientRect();
      this.x = e.clientX - r.left - offX; this.y = e.clientY - r.top - offY; this.clamp(); this.draw();
      const now = performance.now(); this.samples.push({ t: now, x: this.x, y: this.y });
      this.samples = this.samples.filter(s => now - s.t < 80);
    });
    const release = () => {
      if (!dragging) return; dragging = false; this.card.classList.remove('is-held');
      const s = this.samples; let vx = 0, vy = 0;
      if (s.length > 1) { const a = s[0], b = s[s.length - 1], dt = (b.t - a.t) / 1000; if (dt > 0) { vx = (b.x - a.x) / dt; vy = (b.y - a.y) / dt; } }
      this.release(vx, vy);
    };
    title.addEventListener('pointerup', release);
    title.addEventListener('pointercancel', release);
    this.card.addEventListener('keydown', e => {
      const dir = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key];
      if (!dir) return; e.preventDefault(); this.release(dir[0] * 2200, dir[1] * 2200);
    });
  };

  ThrowStage.prototype.release = function (vx, vy) {
    const speed = Math.hypot(vx, vy);
    if (speed < 150) { this.hint('again'); return; }
    const f = this.feel();
    const px = this.x + vx * f.m / f.c, py = this.y + vy * f.m / f.c;
    const target = this.pick(px + this.w / 2, py + this.h / 2);
    if (!target.keep) { this.w = this.o.cardW; this.h = this.o.cardH; }
    this.vx = vx; this.vy = vy; this.target = target;
    this.show(target, this.x + this.w / 2, this.y + this.h / 2, px + this.w / 2, py + this.h / 2);
    this.run();
  };

  ThrowStage.prototype.throwRandom = function () {
    const a = Math.random() * Math.PI * 2; const s = 1600 + Math.random() * 1200;
    this.release(Math.cos(a) * s, Math.sin(a) * s);
  };

  ThrowStage.prototype.run = function () {
    const f = this.feel(); const t = this.target;
    if (window.SW && SW.reduced()) { this.land(); return; }
    let last = performance.now();
    const tick = now => {
      let dt = Math.min(1 / 30, (now - last) / 1000); last = now;
      while (dt > 1e-9) {
        const h = Math.min(1 / 240, dt); dt -= h;
        const ax = (-f.k * (this.x - t.x) - f.c * this.vx) / f.m, ay = (-f.k * (this.y - t.y) - f.c * this.vy) / f.m;
        this.vx += ax * h; this.vy += ay * h; this.x += this.vx * h; this.y += this.vy * h;
      }
      this.draw();
      if (Math.hypot(this.x - t.x, this.y - t.y) < 0.4 && Math.hypot(this.vx, this.vy) < 4) { this.land(); return; }
      this.raf = requestAnimationFrame(tick);
    };
    this.raf = requestAnimationFrame(tick);
  };

  ThrowStage.prototype.land = function () {
    const t = this.target; this.raf = 0; if (!t) return;
    this.x = t.x; this.y = t.y; this.w = t.w; this.h = t.h; this.vx = this.vy = 0; this.target = null;
    this.draw(); this.hide(); this.hint('landed');
  };

  ThrowStage.prototype.draw = function () {
    const s = this.card.style;
    s.width = this.w + 'px'; s.height = this.h + 'px';
    s.transform = 'translate(' + this.x + 'px,' + this.y + 'px)';
  };
  ThrowStage.prototype.show = function (z, x1, y1, x2, y2) {
    const set = (el, a) => { for (const k in a) el.setAttribute(k, a[k]); };
    if (this.o.showPreview) {
      set(this.zone, { x: z.x, y: z.y, width: z.w, height: z.h }); this.zone.style.display = '';
      set(this.zoneLine, { x: z.x + 0.75, y: z.y + 0.75, width: z.w - 1.5, height: z.h - 1.5 });
      this.zoneLine.style.display = this.o.showLines ? '' : 'none';
      if (this.o.showTrace) {
        set(this.trace, { x1, y1, x2, y2 }); this.trace.style.display = '';
        set(this.mark, { x: x2 - 4, y: y2 - 4 }); this.mark.style.display = '';
      }
    }
  };
  ThrowStage.prototype.hide = function () { [this.zone, this.zoneLine, this.trace, this.mark].forEach(e => { e.style.display = 'none'; }); };
  ThrowStage.prototype.hint = function (kind) { if (this.o.onHint) this.o.onHint(kind); };

  window.ThrowStage = ThrowStage;
  window.FEEL_PRESETS = PRESETS;
})();
