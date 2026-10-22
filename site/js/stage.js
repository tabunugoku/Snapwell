/* Snapwell site · the throwable desktop.
 *
 * One stage = one small monitor with windows on it. Every window is a body in a
 * plane (Feel.Body2D) whose field is the zone stops, so a throw decides by where
 * the window would coast. Its size follows two 1D springs. Nothing here has a
 * duration: all motion comes from mass, stiffness, damping and the magnets.
 */
(function (global) {
  'use strict';
  const Feel = global.Feel;

  const FEELS = {
    airy: 'airy, slightly magnetic',
    snappy: 'snappy, precise',
    heavy: 'heavy, settled',
    light: 'light, playful',
  };

  const CAPS = '<span class="caps" aria-hidden="true">'
    + '<i><svg viewBox="0 0 10 10"><path d="M1 5h8" stroke="currentColor"/></svg></i>'
    + '<i><svg viewBox="0 0 10 10"><rect x="1.5" y="1.5" width="7" height="7" fill="none" stroke="currentColor"/></svg></i>'
    + '<i><svg viewBox="0 0 10 10"><path d="M1.5 1.5l7 7M8.5 1.5l-7 7" stroke="currentColor"/></svg></i></span>';
  const SVGNS = 'http://www.w3.org/2000/svg';

  class Stage {
    /**
     * @param {HTMLElement} root  the .monitor element
     * @param {object} o  { wins: [{title, color, lines, zone}], cols, rows, gap, mode, feel, onThrow, onLand }
     */
    constructor(root, o) {
      this.root = root;
      this.area = root.querySelector('.workarea');
      this.fx = document.createElementNS(SVGNS, 'svg');
      this.fx.setAttribute('class', 'fx');
      this.area.appendChild(this.fx);
      this.o = o;
      this.cols = o.cols || 2;
      this.rows = o.rows || 2;
      this.gap = o.gap ?? 8;
      this.mode = o.mode || 'zones';
      this.feelName = o.feel || 'airy';
      this.z = 10;
      this.wins = o.wins.map((def, i) => this.makeWin(def, i));
      this.measure();
      this.layout();
      this.wins.forEach((w) => this.jump(w, w.zone));
      new ResizeObserver(() => { this.measure(); this.layout(); this.wins.forEach((w) => this.jump(w, w.zone)); }).observe(this.area);
    }

    get words() { return Feel.words(FEELS[this.feelName] || this.feelName); }

    measure() {
      this.W = this.area.clientWidth;
      this.H = this.area.clientHeight;
    }

    /** Zone rectangles (zones mode) or the four corners at a kept size (corners mode). */
    layout() {
      const { W, H, gap: g } = this;
      const e = g;
      let rects;
      if (this.mode === 'zones') {
        const cw = (W - 2 * e - (this.cols - 1) * g) / this.cols;
        const ch = (H - 2 * e - (this.rows - 1) * g) / this.rows;
        rects = [];
        for (let r = 0; r < this.rows; r++) {
          for (let c = 0; c < this.cols; c++) rects.push({ x: e + c * (cw + g), y: e + r * (ch + g), w: cw, h: ch });
        }
      } else {
        const w = Math.round(W * 0.36);
        const h = Math.round(H * 0.42);
        const inset = Math.max(e, 6);
        rects = [
          { x: inset, y: inset, w, h },
          { x: W - w - inset, y: inset, w, h },
          { x: inset, y: H - h - inset, w, h },
          { x: W - w - inset, y: H - h - inset, w, h },
        ];
      }
      this.rects = rects;
      const p = this.words;
      const strength = p.magnet || 0.6;
      this.field = Feel.points({
        stops: rects.map((r) => ({ x: r.x, y: r.y })),
        reachFreq: 0.6 + 0.5 * strength,
        clickFreq: 3 + 4 * strength,
        clickRadius: 28,
      });
      const forces = [this.field, ...(p.friction ? [Feel.friction2d({ level: 700 * p.friction })] : [])];
      for (const w of this.wins) {
        for (const b of [w.body, w.bw, w.bh]) {
          b.mass = p.params.mass; b.stiffness = p.params.stiffness; b.damping = p.params.damping;
        }
        w.body.forces = forces;
        w.body.min = { x: 0, y: 0 };
        w.body.max = { x: W - w.bw.x, y: H - w.bh.x };
        if (w.zone >= rects.length) w.zone = rects.length - 1;
      }
    }

    makeWin(def, i) {
      const el = document.createElement('div');
      el.className = 'win';
      el.innerHTML = `<div class="win-title"><i class="ico" style="--ico:${def.color}"></i>`
        + `<span class="name">${def.title}</span>${CAPS}</div>`
        + `<div class="win-body">${(def.lines || [78, 62, 90, 46]).map((n) => `<b style="width:${n}%"></b>`).join('')}</div>`;
      this.area.appendChild(el);
      const w = { el, def, zone: def.zone ?? i, z: ++this.z };
      const p = this.words;
      w.bw = new Feel.Body({ ...p.params, x: 100, target: 100, onFrame: () => this.place(w) });
      w.bh = new Feel.Body({ ...p.params, x: 80, target: 80, onFrame: () => this.place(w) });
      w.body = new Feel.Body2D({
        ...p.params, x: 0, y: 0, target: null, forces: [],
        onFrame: () => this.place(w),
        onRelease: (b) => this.released(w, b),
        onSettle: () => this.settled(w),
      });
      const title = $('.win-title', el);
      Feel.drag(title, w.body, { axis: 'xy', onGrab: () => this.raise(w) });
      title.tabIndex = 0;
      title.addEventListener('keydown', (e) => this.key(w, e));
      el.addEventListener('pointerdown', () => this.raise(w));
      w.body.name = def.title;
      return w;
    }

    /** Arrow keys give the one push that coasts to the neighbouring zone. */
    key(w, e) {
      const d = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key];
      if (!d) return;
      e.preventDefault();
      const n = this.rects.length;
      const cols = this.mode === 'zones' ? this.cols : 2;
      const c = (w.zone % cols) + d[0];
      const r = Math.floor(w.zone / cols) + d[1];
      const to = r * cols + c;
      if (c < 0 || c >= cols || r < 0 || to < 0 || to >= n) return;
      this.throwTo(w, to);
    }

    raise(w) {
      w.z = ++this.z;
      w.el.style.zIndex = w.z;
      this.wins.forEach((o) => o.el.classList.toggle('active', o === w));
      this.clearFx();
    }

    place(w) {
      const b = w.body;
      // Walls follow the window's own size: a window can't be thrown out of the work area.
      b.max.x = this.W - w.bw.x; b.max.y = this.H - w.bh.x;
      const lift = Math.min(1, b.speed / 3000);
      w.el.style.width = `${w.bw.x.toFixed(1)}px`;
      w.el.style.height = `${w.bh.x.toFixed(1)}px`;
      w.el.style.transform = `translate(${b.x.toFixed(2)}px, ${b.y.toFixed(2)}px) scale(${(1 + 0.05 * lift).toFixed(4)})`;
      w.el.style.setProperty('--lift', lift.toFixed(3));
      w.el.classList.toggle('flying', lift > 0.02);
    }

    /** Put a window straight into a zone (layout changes are the hand's, not the model's). */
    jump(w, zone) {
      const r = this.rects[zone];
      w.zone = zone;
      w.body.x = r.x; w.body.y = r.y; w.body.vx = 0; w.body.vy = 0;
      w.bw.x = w.bw.target = r.w; w.bh.x = w.bh.target = r.h;
      this.place(w);
    }

    /** Put a window at any rectangle, in or out of a zone (the film scatters windows this way). */
    jumpRect(w, r) {
      w.body.x = r.x; w.body.y = r.y; w.body.vx = 0; w.body.vy = 0;
      w.bw.x = w.bw.target = r.w; w.bh.x = w.bh.target = r.h;
      this.place(w);
    }

    /** Send a window to a zone with the push that comes to rest there (no hand involved). */
    arrangeTo(w, zone) {
      const r = this.rects[zone];
      w.zone = zone;
      w.bw.to(r.w);
      w.bh.to(r.h);
      w.body.kickTo(this.field.stops[zone]);
    }

    zoneOf(stop) {
      return this.field.stops.indexOf(stop);
    }

    released(w, b) {
      const speed = b.speed;
      const stop = b.predict();
      const zone = this.zoneOf(stop);
      if (zone < 0) return;
      w.zone = zone;
      const r = this.rects[zone];
      w.bw.to(r.w);
      w.bh.to(r.h);
      this.showFx(w, stop, r);
      if (this.o.onThrow) this.o.onThrow({ speed, from: { x: b.x, y: b.y }, to: stop, zone, rect: r });
    }

    settled(w) {
      this.clearFx();
      if (this.o.onLand) this.o.onLand(w);
    }

    clearFx() { this.fx.replaceChildren(); }

    showFx(w, stop, r) {
      this.clearFx();
      const mk = (tag, attrs) => {
        const n = document.createElementNS(SVGNS, tag);
        for (const k in attrs) n.setAttribute(k, attrs[k]);
        this.fx.appendChild(n);
        return n;
      };
      mk('rect', { class: 'zone', x: r.x, y: r.y, width: r.w, height: r.h, rx: 4 });
      const b = w.body;
      mk('path', { class: 'path', d: `M${b.x + w.bw.x / 2} ${b.y + 15} L${stop.x + r.w / 2} ${stop.y + 15}` });
      mk('circle', { class: 'dot', cx: stop.x + r.w / 2, cy: stop.y + 15, r: 3.5 });
    }

    setGap(g) {
      this.gap = g;
      this.layout();
      this.wins.forEach((w) => this.jump(w, w.zone));
    }

    setFeel(name) {
      this.feelName = name;
      this.layout();
    }

    setGrid(cols, rows) {
      if (this.mode !== 'zones') { this.cols = cols; this.rows = rows; return; }
      this.cols = cols; this.rows = rows;
      this.layout();
      this.wins.forEach((w, i) => this.jump(w, Math.min(w.zone, this.rects.length - 1) === w.zone ? w.zone : i % this.rects.length));
    }

    /** Switch between zones (the window takes the zone's size) and corners (it keeps its size). */
    setMode(mode) {
      if (mode === this.mode) return;
      const old = this.wins.map((w) => ({ x: w.body.x + w.bw.x / 2, y: w.body.y + w.bh.x / 2 }));
      this.mode = mode;
      this.layout();
      this.wins.forEach((w, i) => {
        // The nearest new stop to where the window's centre is now; the model carries it there.
        let best = 0; let d = Infinity;
        this.rects.forEach((r, k) => {
          const dd = (r.x + r.w / 2 - old[i].x) ** 2 + (r.y + r.h / 2 - old[i].y) ** 2;
          if (dd < d) { d = dd; best = k; }
        });
        w.zone = best;
        const r = this.rects[best];
        w.bw.to(r.w);
        w.bh.to(r.h);
        w.body.kickTo(this.field.stops[best]);
      });
    }

    /** An impulse toward a zone: where it lands is up to the field. */
    throwTo(w, zone) {
      this.raise(w);
      const b = w.body;
      b.sample();
      // The push that comes to rest at the zone's stop, solved by simulation in the field.
      b.kickTo(this.field.stops[zone]);
      this.released(w, b);
    }

    /** The window farthest from the others that still has an empty zone to fly to. */
    fling() {
      const used = new Set(this.wins.map((w) => w.zone));
      const free = this.rects.map((_, i) => i).filter((i) => !used.has(i));
      const w = [...this.wins].sort((a, c) => c.z - a.z)[0];
      const zone = free.length ? free[0] : (w.zone + 2) % this.rects.length;
      this.throwTo(w, zone);
    }
  }

  function $(s, root = document) { return root.querySelector(s); }

  global.SnapStage = { Stage, FEELS };
})(window);
