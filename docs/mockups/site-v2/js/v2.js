/* Snapwell site v2 (モックアップ) の振る舞い。
 * 動きはすべて feel のばね（質量・剛性・減衰）から出す。継続時間・イージング・遅延タイマーは使わない。
 * 入場の段差も、前の要素のばねが一定の位置を越えたら次を動かす、という形でばねから決める。 */
(function () {
  'use strict';
  const Feel = window.Feel;
  const P = window.SnapPresets;
  const { Stage } = window.SnapStage;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const root = document.documentElement;

  // ---------------------------------------------------------------- 動きを減らす
  // OS の設定は feel.js が読む（読み取り専用）。ページの切り替えは、ここで各物体の step を差し替えて扱う。
  const osRM = matchMedia('(prefers-reduced-motion: reduce)');
  let userRM = false;
  try { userRM = localStorage.getItem('snapwell.reduceMotion') === '1'; } catch (e) { /* 保存できない環境 */ }
  const reduced = () => osRM.matches || userRM;

  // predict() と settleNow() は内部で step を回して先を読むので、その間は元の step を使う
  let inReduce = false;
  function patchStep(C, planar) {
    const step = C.prototype.step;
    C.prototype.step = function (dt) {
      if (!userRM || this.hand || inReduce) return step.call(this, dt);
      inReduce = true;
      try {
        // 停止点へ即座に移す（DESIGN §5.2 アクセシビリティ）。場の中なら、投げで選んだ停止点を予測で求める
        const s = planar && !this.target ? this.predict() : null;
        if (s) { this.x = s.x; this.y = s.y; this.vx = 0; this.vy = 0; } else this.settleNow();
      } finally {
        inReduce = false;
      }
      return undefined;
    };
  }
  patchStep(Feel.Body, false);
  patchStep(Feel.Body2D, true);

  /** 目標へばねで動かす。動きを減らすときは飛ばす。 */
  const go = (b, x) => (reduced() ? b.jump(x) : b.to(x));
  const spring = (mass, freq, zeta) => Feel.fromModal({ mass, freq, zeta });
  // 入場は少し重く、落ち着いた減衰。ホバーは軽く速い
  const RISE = spring(1, 1.7, 0.86);
  const HOVER = spring(0.6, 3.2, 0.7);
  const SCROLL = spring(1, 1.5, 1);

  // ---------------------------------------------------------------- 言語
  const ZONES = {
    en: { 6: ['top left', 'top middle', 'top right', 'bottom left', 'bottom middle', 'bottom right'], 4: ['top left', 'top right', 'bottom left', 'bottom right'] },
    ja: { 6: ['左上', '中央上', '右上', '左下', '中央下', '右下'], 4: ['左上', '右上', '左下', '右下'] },
  };
  const WIN_NAMES = { en: ['Notes', 'Photos', 'Mail', 'Browser'], ja: ['メモ', 'フォト', 'メール', 'ブラウザー'] };
  const TITLES = { en: 'Snapwell · Window manager for Windows 11', ja: 'Snapwell · Windows 11 のウィンドウマネージャー' };
  let lang = root.dataset.lang === 'ja' ? 'ja' : 'en';
  const langHooks = [];

  function setLang(l) {
    lang = l;
    root.lang = l;
    root.dataset.lang = l;
    document.title = TITLES[l];
    try { localStorage.setItem('snapwell.lang', l); } catch (e) { /* 保存できない環境 */ }
    $$('[data-aria-en]').forEach((el) => el.setAttribute('aria-label', el.dataset[l === 'ja' ? 'ariaJa' : 'ariaEn']));
    $$('[data-set-lang]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.setLang === l)));
    langHooks.forEach((f) => f());
  }
  $$('[data-set-lang]').forEach((b) => b.addEventListener('click', () => setLang(b.dataset.setLang)));

  // ---------------------------------------------------------------- 入場: ばねの連鎖
  /** 要素の列を順に立ち上げる。次の要素は、前のばねが 22% を越えた瞬間に動き出す（遅延タイマーなし）。 */
  function chain(els, onDone) {
    if (!els.length) return;
    const isLine = (el) => el.classList.contains('line-in');
    const paint = (el, x) => {
      const p = x / 100;
      el.style.opacity = isLine(el) ? '1' : p.toFixed(3);
      el.style.transform = isLine(el) ? `translateY(${((1 - p) * 108).toFixed(2)}%)` : `translateY(${((1 - p) * 30).toFixed(2)}px)`;
    };
    const rise = (el) => { if (el.onRise) { const f = el.onRise; el.onRise = null; f(); } };
    // 立ち上がりきったら印を外し、ホバーなどの transform を邪魔しない
    const finish = (el) => { el.removeAttribute('data-rise'); el.style.transform = ''; el.style.opacity = ''; rise(el); };
    if (reduced()) { els.forEach(finish); if (onDone) onDone(); return; }
    els.forEach((el) => paint(el, 0));
    const start = (i) => {
      const el = els[i];
      let fired = false;
      const b = new Feel.Body({
        ...RISE, x: 0, target: 0,
        onFrame: (body) => {
          paint(el, body.x);
          if (!fired && body.x > 22) { fired = true; if (i + 1 < els.length) start(i + 1); }
          if (body.x > 40) rise(el);
        },
        onSettle: () => { finish(el); if (i === els.length - 1 && onDone) onDone(); },
      });
      b.to(100);
    };
    start(0);
  }
  // ---------------------------------------------------------------- 磁石のボタン
  const canHover = matchMedia('(hover: hover) and (pointer: fine)');
  for (const el of $$('[data-mag]')) {
    const paint = () => { el.style.transform = `translate(${bx.x.toFixed(2)}px, ${by.x.toFixed(2)}px)`; };
    const bx = new Feel.Body({ ...HOVER, x: 0, target: 0, onFrame: paint });
    const by = new Feel.Body({ ...HOVER, x: 0, target: 0, onFrame: paint });
    el.addEventListener('pointermove', (e) => {
      if (!canHover.matches || reduced()) return;
      const r = el.getBoundingClientRect();
      bx.to(((e.clientX - r.left) / r.width - 0.5) * 12);
      by.to(((e.clientY - r.top) / r.height - 0.5) * 8);
    });
    el.addEventListener('pointerleave', () => { go(bx, 0); go(by, 0); });
  }

  // ---------------------------------------------------------------- 背景の磁石場（点）
  const field = (() => {
    const hero = $('.hero');
    const cv = $('#field');
    const ctx = cv.getContext('2d');
    const GAP = 26;
    // 点は 1 個ずつ質量 1 のばね。k = (2πf)²、c = 2ζ√k
    const kDot = (2 * Math.PI * 1.9) ** 2;
    const cDot = 2 * 0.42 * Math.sqrt(kDot);
    const R = 170; // ポインタの磁石が届く半径（px）
    const PULL = 14; // 磁石の中心で釣り合う変位（px）
    let W = 0, H = 0, dpr = 1, n = 0;
    let rx, ry, ox, oy, vx, vy;
    let ptr = null; let raf = 0; let last = 0;

    function build() {
      const r = hero.getBoundingClientRect();
      dpr = Math.min(2, window.devicePixelRatio || 1);
      W = Math.round(r.width); H = Math.round(Math.min(r.height, 1400));
      cv.width = W * dpr; cv.height = H * dpr;
      cv.style.height = `${H}px`;
      const cols = Math.ceil(W / GAP) + 1; const rows = Math.ceil(H / GAP) + 1;
      n = cols * rows;
      rx = new Float32Array(n); ry = new Float32Array(n);
      ox = new Float32Array(n); oy = new Float32Array(n); vx = new Float32Array(n); vy = new Float32Array(n);
      let i = 0;
      const x0 = (W - (cols - 1) * GAP) / 2;
      for (let r2 = 0; r2 < rows; r2++) for (let c = 0; c < cols; c++) { rx[i] = x0 + c * GAP; ry[i] = 10 + r2 * GAP; i++; }
      draw();
    }

    function stepAll(h) {
      let energy = 0;
      for (let i = 0; i < n; i++) {
        let fx = -kDot * ox[i] - cDot * vx[i];
        let fy = -kDot * oy[i] - cDot * vy[i];
        if (ptr) {
          const dx = ptr.x - rx[i]; const dy = ptr.y - ry[i];
          const d = Math.hypot(dx, dy);
          if (d < R && d > 0.001) {
            const s = (1 - d / R) ** 2 * kDot * PULL;
            fx += (dx / d) * s; fy += (dy / d) * s;
          }
        }
        // 半陰的オイラー（刻み h）
        vx[i] += fx * h; vy[i] += fy * h;
        ox[i] += vx[i] * h; oy[i] += vy[i] * h;
        energy += vx[i] * vx[i] + vy[i] * vy[i] + kDot * (ox[i] * ox[i] + oy[i] * oy[i]);
      }
      return energy;
    }

    // 点の色は配色のトークンから読む（配色を変えたら読み直す）
    let ink = '242,242,240'; let acc = '140,203,242'; let base = 0.15;
    function colors() {
      const cs = getComputedStyle(root);
      ink = cs.getPropertyValue('--dot-rgb').trim() || ink;
      acc = cs.getPropertyValue('--dot-accent-rgb').trim() || acc;
      base = parseFloat(cs.getPropertyValue('--dot-base')) || base;
    }
    function draw() {
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, W, H);
      for (let i = 0; i < n; i++) {
        const fade = Math.max(0, 1 - ry[i] / H) ** 0.8; // 下ほど消える
        const m = Math.min(1, Math.hypot(ox[i], oy[i]) / PULL);
        const a = (base + 0.55 * m) * fade;
        if (a < 0.01) continue;
        ctx.fillStyle = m > 0.15 ? `rgba(${acc},${a.toFixed(3)})` : `rgba(${ink},${a.toFixed(3)})`;
        const s = 1.4 + 1.2 * m;
        ctx.fillRect(rx[i] + ox[i] - s / 2, ry[i] + oy[i] - s / 2, s, s);
      }
    }

    function frame(t) {
      const dt = last ? Math.min((t - last) / 1000, 1 / 30) : 1 / 60;
      last = t;
      const h = 1 / 240;
      let e = 0;
      for (let acc = 0; acc < dt - 1e-6; acc += h) e = stepAll(h);
      draw();
      if (ptr || e > 0.5) raf = requestAnimationFrame(frame);
      else { raf = 0; last = 0; }
    }
    const wake = () => { if (!raf && !reduced()) raf = requestAnimationFrame(frame); };

    hero.addEventListener('pointermove', (e) => {
      if (e.pointerType !== 'mouse') return;
      const r = hero.getBoundingClientRect();
      ptr = { x: e.clientX - r.left, y: e.clientY - r.top };
      wake();
    });
    hero.addEventListener('pointerleave', () => { ptr = null; wake(); });
    new ResizeObserver(() => build()).observe(hero);
    colors();
    build();

    return {
      /** 着地点から外向きの速度を与える。ばねが点を元に戻すので、波のように揺れて収まる。 */
      ripple(px, py, strength) {
        if (reduced()) return;
        for (let i = 0; i < n; i++) {
          const dx = rx[i] - px; const dy = ry[i] - py;
          const d = Math.hypot(dx, dy) || 1;
          const s = strength * Math.exp(-d / 220) * (d < 40 ? d / 40 : 1);
          vx[i] += (dx / d) * s; vy[i] += (dy / d) * s;
        }
        wake();
      },
      stop() { ptr = null; ox.fill(0); oy.fill(0); vx.fill(0); vy.fill(0); draw(); },
      origin() { return hero.getBoundingClientRect(); },
      recolor() { colors(); draw(); },
    };
  })();

  // ---------------------------------------------------------------- ヒーローの画面
  const narrow = matchMedia('(max-width: 760px)');
  const heroGrid = () => (narrow.matches ? [2, 2] : [3, 2]);
  const [hc, hr] = heroGrid();
  const heroWins = [
    { title: WIN_NAMES[lang][0], color: 'var(--ico-1)', zone: 0, lines: [82, 58, 90, 44, 66] },
    { title: WIN_NAMES[lang][1], color: 'var(--ico-2)', zone: hc - 1, lines: [70, 92, 54, 76] },
    { title: WIN_NAMES[lang][2], color: 'var(--ico-3)', zone: hc * hr - 2, lines: [88, 64, 50, 80, 40] },
  ];
  let heroFeel = 'standard';
  const zoneName = (stage, i) => (ZONES[lang][stage.rects.length] || [])[i] || `#${i + 1}`;
  const heroStage = new Stage($('#heroMonitor'), {
    wins: heroWins, cols: hc, rows: hr, gap: 10, feel: heroFeel,
    onThrow: (t) => {
      $('#roSpeed').textContent = `${Math.round(t.speed).toLocaleString('en-US')} px/s`;
      $('#roZone').textContent = zoneName(heroStage, t.zone);
      heroStage.lastZone = t.zone;
    },
    onLand: (w) => {
      // 着地したウィンドウの中心から、背景の点を揺らす
      const o = field.origin();
      const r = w.el.getBoundingClientRect();
      field.ripple(r.left + r.width / 2 - o.left, r.top + r.height / 2 - o.top, 220);
    },
  });
  narrow.addEventListener('change', () => { const [c, r] = heroGrid(); heroStage.setGrid(c, r); });

  function showSpring() {
    const p = heroStage.preset;
    $('#roSpring').textContent = `m ${p.m.toFixed(2)} · ${p.f.toFixed(1)} Hz · ζ ${p.z.toFixed(2)}`;
  }
  function setFeel(v) {
    heroFeel = v;
    heroStage.setFeel(v);
    launchStage.setFeel(v);
    launchWallOnly();
    $$('#heroFeel button, #feelCards .card').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.v === v)));
    showSpring();
  }
  $('#heroFeel').addEventListener('click', (e) => { const b = e.target.closest('button'); if (b) setFeel(b.dataset.v); });
  $('#heroThrow').addEventListener('click', () => heroStage.fling());
  langHooks.push(() => {
    heroStage.wins.forEach((w, i) => { $('.name', w.el).textContent = WIN_NAMES[lang][i]; });
    if (heroStage.lastZone != null) $('#roZone').textContent = zoneName(heroStage, heroStage.lastZone);
  });

  // ---------------------------------------------------------------- スクロールに追う量（ばねで平滑化）
  const scrollers = [];
  function scrollLinked(el, progress, paint) {
    const b = new Feel.Body({ ...SCROLL, x: 0, target: 0, onFrame: (body) => paint(body.x / 1000) });
    const s = { b, update(first) { const p = Math.max(0, Math.min(1, progress(el))) * 1000; if (first || reduced()) { b.jump(p); paint(p / 1000); } else b.to(p); } };
    scrollers.push(s);
    s.update(true);
  }
  const vh = () => window.innerHeight;
  // ヒーローの画面: 少し奥に倒れた状態から、スクロールで手前に起き上がる
  const stageGlass = $('#stageWrap .stage-glass');
  scrollLinked(stageGlass, () => window.scrollY / (vh() * 0.55), (p) => {
    stageGlass.style.setProperty('--tilt', `${(16 * (1 - p)).toFixed(3)}deg`);
    stageGlass.style.setProperty('--sc', (0.94 + 0.06 * p).toFixed(4));
  });
  // 映像: 下から近づくにつれて、小さな額から全幅へ
  const filmFrame = $('#filmFrame');
  scrollLinked(filmFrame, (el) => (vh() - el.getBoundingClientRect().top) / (vh() * 0.9), (p) => {
    filmFrame.style.setProperty('--sc', (0.86 + 0.14 * p).toFixed(4));
  });
  window.addEventListener('scroll', () => scrollers.forEach((s) => s.update(false)), { passive: true });
  window.addEventListener('resize', () => scrollers.forEach((s) => s.update(true)));

  // ---------------------------------------------------------------- 動きのカード
  const CARD_LABELS = {
    en: ['Mass', 'Frequency', 'Damping ratio', 'Overshoot'],
    ja: ['質量', '固有振動数', '減衰比', '行き過ぎ'],
  };
  function cardNums(card) {
    const p = P.PRESETS[card.dataset.v];
    const m = Feel.metrics(Feel.fromModal(P.modal(p)));
    const vals = [p.m.toFixed(2), `${p.f.toFixed(1)} Hz`, p.z.toFixed(2), `${(m.overshoot * 100).toFixed(1)} %`];
    $('.card-nums', card).innerHTML = vals.map((v, i) => `<span><em>${CARD_LABELS[lang][i]}</em><b>${v}</b></span>`).join('');
  }
  const NS = 'http://www.w3.org/2000/svg';
  for (const card of $$('#feelCards .card')) {
    const p = P.PRESETS[card.dataset.v];
    const svg = $('.curve', card);
    // 静止から 1 区画のキー操作: 変位 u0 = -1 から目標 0 へ。横軸 1.4 秒（DESIGN §5.2）
    const res = Feel.response(Feel.fromModal(P.modal(p)), -1, 0);
    const T = 1.4; const N = 140;
    const yOf = (x) => 108 - (x / 1.3) * 96; // 0 → 108、1.3 → 12
    let d = '';
    for (let i = 0; i <= N; i++) {
      const t = (T * i) / N;
      const x = 1 + res(t)[0];
      d += `${i ? 'L' : 'M'}${((240 * i) / N).toFixed(2)} ${yOf(x).toFixed(2)}`;
    }
    const mk = (tag, a) => { const e = document.createElementNS(NS, tag); for (const k in a) e.setAttribute(k, a[k]); svg.appendChild(e); return e; };
    mk('line', { class: 'axis', x1: 0, y1: 108, x2: 240, y2: 108 });
    mk('line', { class: 'goal', x1: 0, y1: yOf(1), x2: 240, y2: yOf(1) });
    const trace = mk('path', { class: 'trace', d, pathLength: 100 });
    trace.style.strokeDasharray = '100 100';
    trace.style.strokeDashoffset = '100';
    // 曲線は、カードが立ち上がったところから描き始める（描く速さもばね）
    const draw = new Feel.Body({ ...spring(1, 0.9, 1), x: 0, target: 0, onFrame: (b) => { trace.style.strokeDashoffset = (100 - b.x).toFixed(2); } });
    card.onRise = () => go(draw, 100);
    cardNums(card);
    card.addEventListener('click', () => setFeel(card.dataset.v));
  }
  langHooks.push(() => $$('#feelCards .card').forEach(cardNums));

  // ---------------------------------------------------------------- 機能の行: ホバーで水色の線が伸びる
  for (const row of $$('.rows .row')) {
    const b = new Feel.Body({
      ...HOVER, x: 0, target: 0,
      onFrame: (body) => {
        row.style.setProperty('--ln', (body.x / 100).toFixed(4));
        row.style.setProperty('--nx', `${(body.x * 0.1).toFixed(2)}px`);
      },
    });
    row.addEventListener('pointerenter', () => { if (canHover.matches) go(b, 100); });
    row.addEventListener('pointerleave', () => go(b, 0));
  }

  // ---------------------------------------------------------------- ランチャー
  // 何度でも選び直せる。毎回、いまの位置と速度から、選んだ矩形を目標にしたばねで動く（毎回の初期化はしない）。
  // 区画の磁石は使わず、壁だけを残す。セルは説明用の 4 × 3。
  const launchStage = new Stage($('#launchMonitor'), {
    wins: [{ title: WIN_NAMES[lang][3], color: 'var(--ico-1)', zone: 5, lines: [86, 62, 92, 48, 72, 58] }],
    cols: 4, rows: 3, gap: 8, feel: heroFeel,
  });
  const lw = launchStage.wins[0];
  /** Stage.layout が区画の磁石を戻すので、そのたびに壁だけにする。 */
  function launchWallOnly() {
    if (lw.body.forces.length > 1) lw.body.forces = [lw.body.forces[1]];
    lw.saved = null;
  }
  launchWallOnly();
  const LAUNCH_NAMES = {
    en: { 'left-half': 'Left half', 'right-half': 'Right half', 'q-tl': 'Top-left quarter', 'q-tr': 'Top-right quarter', 'q-bl': 'Bottom-left quarter', 'q-br': 'Bottom-right quarter', 't-l': 'Left third', 't-m': 'Middle third', 't-r': 'Right third', center: 'Center', max: 'Maximize', start: 'Start', cells: (w, h) => `${w} × ${h} cells` },
    ja: { 'left-half': '左半分', 'right-half': '右半分', 'q-tl': '左上の 4 分の 1', 'q-tr': '右上の 4 分の 1', 'q-bl': '左下の 4 分の 1', 'q-br': '右下の 4 分の 1', 't-l': '左の 3 分の 1', 't-m': '中央の 3 分の 1', 't-r': '右の 3 分の 1', center: '中央', max: '最大化', start: '最初の位置', cells: (w, h) => `${w} × ${h} セル` },
  };
  const PRESET_RECTS = {
    'left-half': (W, H, g) => ({ x: g, y: g, w: (W - 3 * g) / 2, h: H - 2 * g }),
    'right-half': (W, H, g) => ({ x: 2 * g + (W - 3 * g) / 2, y: g, w: (W - 3 * g) / 2, h: H - 2 * g }),
    'q-tl': (W, H, g) => ({ x: g, y: g, w: (W - 3 * g) / 2, h: (H - 3 * g) / 2 }),
    'q-tr': (W, H, g) => ({ x: 2 * g + (W - 3 * g) / 2, y: g, w: (W - 3 * g) / 2, h: (H - 3 * g) / 2 }),
    'q-bl': (W, H, g) => ({ x: g, y: 2 * g + (H - 3 * g) / 2, w: (W - 3 * g) / 2, h: (H - 3 * g) / 2 }),
    'q-br': (W, H, g) => ({ x: 2 * g + (W - 3 * g) / 2, y: 2 * g + (H - 3 * g) / 2, w: (W - 3 * g) / 2, h: (H - 3 * g) / 2 }),
    't-l': (W, H, g) => ({ x: g, y: g, w: (W - 4 * g) / 3, h: H - 2 * g }),
    't-m': (W, H, g) => ({ x: 2 * g + (W - 4 * g) / 3, y: g, w: (W - 4 * g) / 3, h: H - 2 * g }),
    't-r': (W, H, g) => ({ x: 3 * g + (2 * (W - 4 * g)) / 3, y: g, w: (W - 4 * g) / 3, h: H - 2 * g }),
    center: (W, H) => ({ x: W * 0.2, y: H * 0.15, w: W * 0.6, h: H * 0.7 }),
    max: (W, H, g) => ({ x: g, y: g, w: W - 2 * g, h: H - 2 * g }),
  };
  // いまの矩形は作業領域に対する比で持つ（幅が変わっても同じ場所に保つ）
  const toFrac = (r) => ({ x: r.x / launchStage.W, y: r.y / launchStage.H, w: r.w / launchStage.W, h: r.h / launchStage.H });
  const fromFrac = (f) => ({ x: f.x * launchStage.W, y: f.y * launchStage.H, w: f.w * launchStage.W, h: f.h * launchStage.H });
  const startFrac = toFrac(launchStage.rects[5]);
  let launchCur = startFrac;
  let launchLabel = ['start'];
  let pending = null;
  const labelText = (l) => (l[0] === 'cells' ? LAUNCH_NAMES[lang].cells(l[1], l[2]) : LAUNCH_NAMES[lang][l[0]]);
  /** いまの位置を出す。選んでいる途中は、置く前の範囲を矢印つきで出す（セルの枠はウィンドウに重ねないため）。 */
  function showLaunch() {
    $('#launchNow').textContent = pending ? `${labelText(launchLabel)} → ${labelText(pending)}` : labelText(launchLabel);
  }
  /** 選んだ矩形へ、いまの状態からばねで動かす。続けて呼べば、動いている途中からそのまま向きを変える。 */
  function launchPlace(r, label) {
    launchStage.raise(lw);
    launchWallOnly();
    launchCur = toFrac(r);
    launchLabel = label;
    pending = null;
    go(lw.bw, r.w);
    go(lw.bh, r.h);
    if (reduced()) lw.body.jump({ x: r.x, y: r.y }); else lw.body.to({ x: r.x, y: r.y });
    showLaunch();
  }
  langHooks.push(() => { $('.name', lw.el).textContent = WIN_NAMES[lang][3]; showLaunch(); });

  const cellsBox = $('#launchCells');
  const vis = $('#launchVis');
  const dim = new Feel.Body({ ...HOVER, x: 0, target: 0, onFrame: (b) => vis.style.setProperty('--dim-on', (b.x / 100).toFixed(4)) });
  let cells = [];
  function layoutCells() {
    vis.replaceChildren();
    cells = launchStage.rects.map((r) => {
      const i = document.createElement('i');
      Object.assign(i.style, { left: `${r.x}px`, top: `${r.y}px`, width: `${r.w}px`, height: `${r.h}px` });
      vis.appendChild(i);
      return i;
    });
    markCur();
  }
  // Stage の ResizeObserver（区画へ戻す）のあとに動き、選んだ矩形の位置へ置き直す
  new ResizeObserver(() => {
    launchWallOnly();
    const r = fromFrac(launchCur);
    launchStage.jumpRect(lw, r);
    lw.body.target = { x: r.x, y: r.y };
    layoutCells();
  }).observe(cellsBox);
  const C = () => launchStage.cols;
  const R = () => launchStage.rows;
  const cellAt = (e) => {
    const box = cellsBox.getBoundingClientRect();
    const x = e.clientX - box.left; const y = e.clientY - box.top;
    let best = 0; let dmin = Infinity;
    launchStage.rects.forEach((r, i) => {
      const dx = Math.max(r.x - x, 0, x - (r.x + r.w)); const dy = Math.max(r.y - y, 0, y - (r.y + r.h));
      const d = dx * dx + dy * dy;
      if (d < dmin) { dmin = d; best = i; }
    });
    return best;
  };
  const span = (a, b) => ({
    c0: Math.min(a % C(), b % C()), c1: Math.max(a % C(), b % C()),
    r0: Math.min(Math.floor(a / C()), Math.floor(b / C())), r1: Math.max(Math.floor(a / C()), Math.floor(b / C())),
  });
  const mark = (s) => {
    cells.forEach((el, i) => {
      const c = i % C(); const r = Math.floor(i / C());
      el.classList.toggle('on', !!s && c >= s.c0 && c <= s.c1 && r >= s.r0 && r <= s.r1);
    });
    pending = s ? ['cells', s.c1 - s.c0 + 1, s.r1 - s.r0 + 1] : null;
    showLaunch();
  };
  const rectOf = (s) => {
    const rs = launchStage.rects;
    const a = rs[s.r0 * C() + s.c0]; const b = rs[s.r1 * C() + s.c1];
    return { x: a.x, y: a.y, w: b.x + b.w - a.x, h: b.y + b.h - a.y };
  };
  const pressPreset = (k) => $$('#launchPresets button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.p === k)));
  const placeSpan = (s) => {
    launchPlace(rectOf(s), ['cells', s.c1 - s.c0 + 1, s.r1 - s.r0 + 1]);
    pressPreset(null);
  };

  // ポインター: なぞった範囲に置く
  let sweep = null;
  cellsBox.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    cellsBox.setPointerCapture(e.pointerId);
    const c = cellAt(e);
    sweep = { from: c, s: span(c, c) };
    kb.anchor = kb.cur = c;
    markCur();
    mark(sweep.s);
    go(dim, 100);
  });
  cellsBox.addEventListener('pointermove', (e) => {
    if (!sweep) return;
    const c = cellAt(e);
    sweep.s = span(sweep.from, c);
    kb.cur = c;
    markCur();
    mark(sweep.s);
  });
  const endSweep = () => {
    if (!sweep) return;
    placeSpan(sweep.s);
    sweep = null;
    mark(null);
    go(dim, 0);
  };
  cellsBox.addEventListener('pointerup', endSweep);
  cellsBox.addEventListener('pointercancel', endSweep);

  // キーボード: 矢印で動かし、Shift で範囲を広げ、Enter か Space で置く。Esc で選択を消す
  const kb = { anchor: 5, cur: 5 };
  function markCur() { cells.forEach((el, i) => el.classList.toggle('cur', i === kb.cur)); }
  cellsBox.addEventListener('keydown', (e) => {
    const d = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key];
    if (d) {
      e.preventDefault();
      const c = Math.min(C() - 1, Math.max(0, (kb.cur % C()) + d[0]));
      const r = Math.min(R() - 1, Math.max(0, Math.floor(kb.cur / C()) + d[1]));
      kb.cur = r * C() + c;
      if (!e.shiftKey) kb.anchor = kb.cur;
      markCur();
      mark(span(kb.anchor, kb.cur));
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      placeSpan(span(kb.anchor, kb.cur));
      mark(null);
    } else if (e.key === 'Escape') {
      kb.anchor = kb.cur;
      mark(null);
    }
  });
  cellsBox.addEventListener('blur', () => mark(null));

  $('#launchPresets').addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    const { W, H, gap: g } = launchStage;
    launchPlace(PRESET_RECTS[b.dataset.p](W, H, g), [b.dataset.p]);
    pressPreset(b.dataset.p);
  });
  $('#launchReset').addEventListener('click', () => {
    launchPlace(fromFrac(startFrac), ['start']);
    pressPreset(null);
    kb.anchor = kb.cur = 5;
    markCur();
  });
  layoutCells();
  showLaunch();

  // ---------------------------------------------------------------- 導入（訪問の最初に 1 回）
  // 旧サイトと動画の導入（js/intro.js）を、画面いっぱいの重ね板で短く流す。窓は Standard と Bouncy のばね、
  // 点は自前のばねで動き、時刻は intro.step に渡すシミュレーションの時計だけで進む（遅延タイマーなし）。
  // 見せるかどうかは <head> の小さなスクリプトが描画前に決め、html に intro の印を付ける。
  const introCtl = (() => {
    const box = $('#intro');
    const cv = $('#introCanvas');
    const skip = $('#introSkip');
    const replayBtn = $('#introReplay');
    // 動画の拍（約 9.8 秒）から並べ替えの 2 つを省き、間を詰めた版。logo の拍から done まで TAIL 秒
    const BEATS = { enter: 0.15, burst: 0.75, grid: 1.45, layoutB: null, layoutC: null, final: 2.1, hop: 2.9, veil: 3.4, logo: 3.55 };
    const TAIL = 0.85;
    const logo = window.Brand.logo($('#introLogo'));
    let intro = null; let raf = 0; let last = 0; let leaving = false; let queue = [];
    let onEnd = null; let handed = false; let width = 0; let fromReplay = false;
    const toRgb = (c) => {
      const k = document.createElement('canvas').getContext('2d');
      k.fillStyle = c;
      const v = k.fillStyle;
      if (v[0] === '#') return [1, 3, 5].map((i) => parseInt(v.slice(i, i + 2), 16));
      const m = /rgba?\(([^)]+)\)/.exec(v);
      return m ? m[1].split(',').slice(0, 3).map(Number) : [31, 108, 159];
    };
    const colors = () => {
      const cs = getComputedStyle(root);
      const v = (n) => cs.getPropertyValue(n).trim();
      return {
        accent: toRgb(v('--accent')), ink: toRgb(v('--fg')), bg: toRgb(v('--bg')),
        surface: v('--surface'), surface2: v('--surface-2'), borderStrong: v('--border-strong'), skeleton: v('--skeleton'),
      };
    };
    // 重ね板の不透明度。消えるときも Standard のばねで、6 割まで透けたところでヒーローの入場へ渡す
    const fade = new Feel.Body({
      ...Feel.fromModal(P.modal(P.PRESETS.standard)), x: 100, target: 100, min: -20, max: 120,
      onFrame: (b) => {
        box.style.opacity = Math.min(1, Math.max(0, b.x / 100)).toFixed(3);
        if (leaving && !handed && b.x < 60) { handed = true; if (onEnd) onEnd(); }
      },
      onSettle: (b) => { if (leaving && b.x < 1) finish(); },
    });

    function frame(now) {
      raf = 0;
      if (!intro) return;
      const dt = Math.min(0.05, (now - last) / 1000);
      last = now;
      intro.step(dt);
      // ロゴの段差も、シミュレーションの時計で待つ
      queue = queue.filter((q) => (intro.t >= q.t ? (q.fn(), false) : true));
      intro.draw();
      if (!leaving && intro.done) leave();
      raf = requestAnimationFrame(frame);
    }
    function finish() {
      if (raf) { cancelAnimationFrame(raf); raf = 0; }
      intro = null; queue = []; leaving = false;
      const hadFocus = box.contains(document.activeElement);
      root.classList.remove('intro');
      box.style.opacity = '';
      box.style.pointerEvents = '';
      if (!handed && onEnd) { handed = true; onEnd(); }
      onEnd = null;
      // もう一度見るから始めたときはそのボタンへ戻す。最初の訪問では外すだけにして、ページを動かさない
      if (hadFocus) { if (fromReplay) replayBtn.focus(); else document.activeElement.blur(); }
    }
    function leave() {
      if (!intro || leaving) return;
      leaving = true;
      // 透けている間は下のページを押せるようにする
      box.style.pointerEvents = 'none';
      if (reduced()) { fade.jump(0); finish(); return; }
      fade.to(0);
    }
    function play(done, replay) {
      fromReplay = !!replay;
      onEnd = done; handed = false; leaving = false;
      root.classList.add('intro');
      const W = cv.clientWidth; const H = cv.clientHeight;
      width = window.innerWidth;
      if (!W || !H) { finish(); return; }
      intro = new window.SnapIntro.Intro(cv, {
        width: W, height: H, dpr: Math.min(2, window.devicePixelRatio || 1), colors: colors(), seed: 11, veilMax: 0.86,
        beats: BEATS, tail: TAIL, scale: Math.max(0.5, Math.min(1.6, Math.min(W, H) / 380, W / 700)),
        onBeat: (name) => { if (name === 'logo') logo.show((fn, s) => queue.push({ t: intro.t + s, fn })); },
      });
      logo.reset();
      fade.jump(100);
      intro.draw();
      last = performance.now();
      raf = requestAnimationFrame(frame);
    }
    // どこを押しても、Esc・Enter・Space でも飛ばせる。Tab は奪わず、重ね板の外へフォーカスが出たら閉じる
    const isOn = () => !!intro && !leaving;
    skip.addEventListener('click', leave);
    box.addEventListener('pointerdown', (e) => { if (e.target !== skip) leave(); });
    window.addEventListener('keydown', (e) => {
      if (!isOn()) return;
      if (e.key === 'Escape' || ((e.key === 'Enter' || e.key === ' ') && e.target !== skip)) { e.preventDefault(); leave(); }
    });
    window.addEventListener('wheel', () => { if (isOn()) leave(); }, { passive: true });
    window.addEventListener('touchmove', () => { if (isOn()) leave(); }, { passive: true });
    document.addEventListener('focusin', (e) => { if (isOn() && !box.contains(e.target)) leave(); });
    // 幅が変わったら（回転・窓の大きさ）組み直さずに終える。スマホのアドレスバーによる高さの変化は無視する
    window.addEventListener('resize', () => { if (intro && window.innerWidth !== width) { fade.jump(0); finish(); } });
    replayBtn.addEventListener('click', () => {
      if (reduced() || intro) return;
      play(null, true);
      skip.focus();
    });
    return {
      /** 印が付いていれば流す。付いていなければ（動きを減らす、同じセッションの 2 回目）すぐ done を呼ぶ */
      start(done) {
        if (root.classList.contains('intro') && !reduced()) play(done);
        else { root.classList.remove('intro'); done(); }
      },
      recolor() { if (intro) { intro.colors = colors(); intro.draw(); } },
      /** 動きを減らすに切り替えたら、流れている導入を閉じ、もう一度見るを止める */
      motion() {
        replayBtn.disabled = reduced();
        if (intro && reduced()) { fade.jump(0); finish(); }
      },
    };
  })();

  // ---------------------------------------------------------------- 配色
  const osDark = matchMedia('(prefers-color-scheme: dark)');
  let theme = root.dataset.theme === 'light' || root.dataset.theme === 'dark' ? root.dataset.theme : 'system';
  function setTheme(t) {
    theme = t;
    if (t === 'system') delete root.dataset.theme; else root.dataset.theme = t;
    try { localStorage.setItem('snapwell.theme', t); } catch (e) { /* 保存できない環境 */ }
    $$('[data-set-theme]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.setTheme === t)));
    field.recolor();
    introCtl.recolor();
  }
  $$('[data-set-theme]').forEach((b) => b.addEventListener('click', () => setTheme(b.dataset.setTheme)));
  osDark.addEventListener('change', () => { field.recolor(); introCtl.recolor(); });
  langHooks.push(() => $$('[data-set-theme]').forEach((b) => { b.title = b.dataset[lang === 'ja' ? 'titleJa' : 'titleEn']; }));

  // ---------------------------------------------------------------- 映像: site/video を読めないときは案内を出す
  const filmVideo = $('#filmFrame video');
  const filmSources = $$('source', filmVideo);
  const filmMissing = () => {
    $('#filmFrame').classList.add('missing');
    $('.film-missing').hidden = false;
  };
  filmSources[filmSources.length - 1].addEventListener('error', filmMissing);
  // このスクリプトより先に読み込みが失敗していた場合
  if (filmVideo.networkState === HTMLMediaElement.NETWORK_NO_SOURCE) filmMissing();
  // ---------------------------------------------------------------- 動きを減らすボタン
  const motionBtn = $('#motionBtn');
  function showMotion() {
    motionBtn.setAttribute('aria-pressed', String(reduced()));
    motionBtn.disabled = osRM.matches;
    if (reduced()) field.stop();
    introCtl.motion();
  }
  motionBtn.addEventListener('click', () => {
    userRM = !userRM;
    try { localStorage.setItem('snapwell.reduceMotion', userRM ? '1' : '0'); } catch (e) { /* 保存できない環境 */ }
    showMotion();
  });
  osRM.addEventListener('change', showMotion);

  // ---------------------------------------------------------------- 起動
  setLang(lang);
  setTheme(theme);
  setFeel(heroFeel);
  showMotion();

  // ヒーローは導入が透けはじめたところで（導入がなければ読み込みで）立ち上げ、立ち上がりきったら一度だけ見本の投げをする
  const heroEls = $$('.hero [data-rise]');
  introCtl.start(() => chain(heroEls, () => { if (!reduced()) heroStage.fling(); }));
  // ほかの節は、見えたときに節ごとに立ち上げる
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      io.unobserve(e.target);
      chain($$('[data-rise]', e.target));
    }
  }, { rootMargin: '0px 0px -12% 0px' });
  $$('main > section:not(.hero)').forEach((s) => io.observe(s));
})();
