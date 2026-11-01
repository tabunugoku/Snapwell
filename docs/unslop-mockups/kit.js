// Snapwell UI キット: 表示の切り替え（案／現状・明暗・言語）、文言、ばね、部品
(function () {
  'use strict';
  const S = window.SW_STRINGS || {};
  const root = document.documentElement;
  const params = new URLSearchParams(location.search);

  function stored(key, fallback) {
    try { return params.get(key) || localStorage.getItem('sw-mock-' + key) || fallback; } catch (e) { return params.get(key) || fallback; }
  }
  function store(key, value) { try { localStorage.setItem('sw-mock-' + key, value); } catch (e) { /* 保存できなくても表示は続ける */ } }

  const state = {
    variant: stored('v', 'tobe'),   // tobe | asis
    theme: stored('theme', 'auto'), // auto | light | dark
    lang: stored('lang', 'ja'),     // ja | en
  };
  const listeners = [];

  // to-be で文言を直したもの（意味と用語は変えない）。逸脱として index に載せる
  const COPY_TOBE = {
    'Setup.Welcome.Title': { ja: 'ウィンドウを投げよう', en: 'Throw a window' },
  };

  function t(key, vars) {
    let entry = (state.variant === 'tobe' && COPY_TOBE[key]) || S[key];
    let text = entry ? (entry[state.lang] || entry.ja || key) : key;
    if (vars) for (const name in vars) text = text.split('{' + name + '}').join(vars[name]);
    return text;
  }

  function apply() {
    root.dataset.variant = state.variant;
    if (state.theme === 'auto') delete root.dataset.theme; else root.dataset.theme = state.theme;
    root.lang = state.lang;
    document.querySelectorAll('[data-k]').forEach(el => { el.textContent = t(el.dataset.k); });
    document.querySelectorAll('[data-k-ph]').forEach(el => { el.placeholder = t(el.dataset.kPh); });
    document.querySelectorAll('[data-k-aria]').forEach(el => { el.setAttribute('aria-label', t(el.dataset.kAria)); });
    document.querySelectorAll('[data-ja]').forEach(el => { el.textContent = state.lang === 'en' ? el.dataset.en : el.dataset.ja; });
    document.querySelectorAll('.seg[data-control] button').forEach(b => {
      b.setAttribute('aria-pressed', String(state[b.parentElement.dataset.control] === b.dataset.value));
    });
    listeners.forEach(fn => fn(state));
  }

  function set(key, value) { state[key] = value; store(key === 'variant' ? 'v' : key, value); apply(); }

  // ---------- ばね: m・f・ζ から k と c を導き、240 Hz の固定刻みで進める ----------
  const reduced = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  function springParams(name) {
    const raw = getComputedStyle(root).getPropertyValue('--sw-spring-' + name).trim().split(/\s+/).map(Number);
    const [m, f, zeta] = raw.length === 3 && raw.every(Number.isFinite) ? raw : [1, 4.5, 1];
    const k = m * Math.pow(2 * Math.PI * f, 2);
    return { m, k, c: 2 * zeta * Math.sqrt(k * m) };
  }
  // value を target へ動かす。途中で呼び直すと、いまの位置と速度から続ける（キューに積まない）
  function Spring(name, value, onUpdate) {
    this.p = springParams(name); this.x = value; this.v = 0; this.target = value; this.onUpdate = onUpdate; this.raf = 0;
  }
  Spring.prototype.to = function (target, velocity) {
    this.target = target;
    if (velocity !== undefined) this.v = velocity;
    if (reduced()) { this.x = target; this.v = 0; this.onUpdate(this.x, this.v); return; }
    if (this.raf) return;
    let last = performance.now();
    const tick = now => {
      let dt = Math.min(1 / 30, (now - last) / 1000); last = now;
      const h = 1 / 240;
      while (dt > 1e-9) {
        const step = Math.min(h, dt); dt -= step;
        const a = (-this.p.k * (this.x - this.target) - this.p.c * this.v) / this.p.m;
        this.v += a * step; this.x += this.v * step;
      }
      this.onUpdate(this.x, this.v);
      if (Math.abs(this.x - this.target) < 0.01 && Math.abs(this.v) < 0.05) {
        this.x = this.target; this.v = 0; this.onUpdate(this.x, this.v); this.raf = 0; return;
      }
      this.raf = requestAnimationFrame(tick);
    };
    this.raf = requestAnimationFrame(tick);
  };
  Spring.prototype.jump = function (value) { this.x = this.target = value; this.v = 0; this.onUpdate(value, 0); };

  // ---------- アイコン（MainWindow.xaml のナビの Path をそのまま使う。viewBox 16） ----------
  const ICONS = {
    general: 'M8 5.5 A2.5 2.5 0 1 1 8 10.5 A2.5 2.5 0 1 1 8 5.5 Z M8 1.5 v2 M8 12.5 v2 M1.5 8 h2 M12.5 8 h2 M3.4 3.4 l1.4 1.4 M11.2 11.2 l1.4 1.4 M3.4 12.6 l1.4 -1.4 M11.2 4.8 l1.4 -1.4',
    grid: 'M3 2.5 H13 A1.5 1.5 0 0 1 14.5 4 V12 A1.5 1.5 0 0 1 13 13.5 H3 A1.5 1.5 0 0 1 1.5 12 V4 A1.5 1.5 0 0 1 3 2.5 Z M6 2.5 v11 M10 2.5 v11 M1.5 8 h13',
    hotkeys: 'M3 4 H13 A1.5 1.5 0 0 1 14.5 5.5 V10.5 A1.5 1.5 0 0 1 13 12 H3 A1.5 1.5 0 0 1 1.5 10.5 V5.5 A1.5 1.5 0 0 1 3 4 Z M4 7 h1 M7 7 h2 M11 7 h1 M5 9.5 h6',
    feel: 'M1.5 12 c2 0 3 -8 5 -8 s2.5 6 4 6 s1.5 -3 4 -3',
    rules: 'M2.5 3.5 h11 M2.5 8 h11 M2.5 12.5 h7',
    os: 'M2.5 1.5 H6 A1 1 0 0 1 7 2.5 V6 A1 1 0 0 1 6 7 H2.5 A1 1 0 0 1 1.5 6 V2.5 A1 1 0 0 1 2.5 1.5 Z M10 1.5 H13.5 A1 1 0 0 1 14.5 2.5 V6 A1 1 0 0 1 13.5 7 H10 A1 1 0 0 1 9 6 V2.5 A1 1 0 0 1 10 1.5 Z M2.5 9 H6 A1 1 0 0 1 7 10 V13.5 A1 1 0 0 1 6 14.5 H2.5 A1 1 0 0 1 1.5 13.5 V10 A1 1 0 0 1 2.5 9 Z M10 9 H13.5 A1 1 0 0 1 14.5 10 V13.5 A1 1 0 0 1 13.5 14.5 H10 A1 1 0 0 1 9 13.5 V10 A1 1 0 0 1 10 9 Z',
    backup: 'M13 6.5 A5 5 0 0 0 3.6 4.4 M3 9.5 a5 5 0 0 0 9.4 2.1 M13.5 2.5 v4 h-4 M2.5 13.5 v-4 h4',
    launcher: 'M2 3 H14 V13 H2 Z M2 6 H14 M6 6 V13',
    look: 'M1.5 13.5 v-6 a6 6 0 0 1 6 -6 h6 M5 13.5 v-6 a2.5 2.5 0 0 1 2.5 -2.5 h6',
    chevron: 'M4.5 6.5 L8 10 L11.5 6.5',
    chevronRight: 'M6.5 4.5 L10 8 L6.5 11.5',
    info: 'M8 1.5 A6.5 6.5 0 1 1 8 14.5 A6.5 6.5 0 1 1 8 1.5 Z M8 7.5 v4 M8 5 v0.5',
    warn: 'M8 2 L14.5 13.5 H1.5 Z M8 6.5 v3.5 M8 12 v0.5',
    error: 'M8 1.5 A6.5 6.5 0 1 1 8 14.5 A6.5 6.5 0 1 1 8 1.5 Z M5.5 5.5 L10.5 10.5 M10.5 5.5 L5.5 10.5',
    ok: 'M8 1.5 A6.5 6.5 0 1 1 8 14.5 A6.5 6.5 0 1 1 8 1.5 Z M5 8.2 L7.2 10.3 L11 6',
    search: 'M7 2 A5 5 0 1 1 7 12 A5 5 0 1 1 7 2 Z M10.6 10.6 L14 14',
    up: 'M8 13 V3 M4 7 L8 3 L12 7',
    down: 'M8 3 V13 M4 9 L8 13 L12 9',
    edit: 'M10.5 2.5 L13.5 5.5 L5.5 13.5 H2.5 V10.5 Z',
    trash: 'M2.5 4 H13.5 M6 4 V2.5 H10 V4 M4 4 L4.8 13.5 H11.2 L12 4',
    plus: 'M8 3 V13 M3 8 H13',
    folder: 'M1.5 4 A1 1 0 0 1 2.5 3 H6 L7.5 4.5 H13.5 A1 1 0 0 1 14.5 5.5 V12.5 A1 1 0 0 1 13.5 13.5 H2.5 A1 1 0 0 1 1.5 12.5 Z',
    close: 'M4 4 L12 12 M12 4 L4 12',
    min: 'M3.5 8 H12.5',
    max: 'M3.5 3.5 H12.5 V12.5 H3.5 Z',
    arrange: 'M2 2.5 H7 V7 H2 Z M9 2.5 H14 V7 H9 Z M2 9 H7 V13.5 H2 Z M9 9 H14 V13.5 H9 Z',
    exit: 'M6 2.5 H3 A0.5 0.5 0 0 0 2.5 3 V13 A0.5 0.5 0 0 0 3 13.5 H6 M10 5 L13 8 L10 11 M13 8 H6',
    update: 'M8 2 V10 M4.5 6.5 L8 10 L11.5 6.5 M2.5 13.5 H13.5',
  };
  function icon(name, cls) {
    return '<svg class="ico' + (cls ? ' ' + cls : '') + '" viewBox="0 0 16 16" aria-hidden="true"><path d="' + ICONS[name] + '"/></svg>';
  }

  // ---------- トグル ----------
  // 押すと on を反転し、つまみを StiffHeavy のばねで動かす。塗りとつまみの色は位置から決める
  function makeToggle(el, on, onChange) {
    el.classList.add('toggle'); el.type = 'button'; el.setAttribute('role', 'switch');
    el.innerHTML = '<span class="fill"></span><span class="knob"><i></i></span>';
    const fill = el.querySelector('.fill'), knob = el.querySelector('.knob'), dot = knob.querySelector('i');
    const travel = () => el.clientWidth - 2 * (parseFloat(getComputedStyle(root).getPropertyValue('--sw-knob-inset')) || 4) - knob.offsetWidth + 2;
    const draw = (x, v) => {
      const p = Math.max(0, Math.min(1, x));
      const stretch = 1 + Math.min(0.3, Math.abs(v) * 0.04);
      knob.style.transform = 'translateX(' + (x * travel()) + 'px) scaleX(' + stretch + ')';
      fill.style.opacity = p; dot.style.opacity = p;
      el.style.borderColor = p > 0.5 ? 'transparent' : '';
    };
    const spring = new Spring('toggle', on ? 1 : 0, draw);
    el.setAttribute('aria-checked', String(on));
    requestAnimationFrame(() => spring.jump(on ? 1 : 0));
    el.addEventListener('click', () => {
      if (el.disabled) return;
      on = !on; el.setAttribute('aria-checked', String(on)); spring.to(on ? 1 : 0);
      if (onChange) onChange(on);
    });
    el.swSet = value => { on = value; el.setAttribute('aria-checked', String(on)); spring.jump(on ? 1 : 0); };
    return el;
  }

  function hydrateToggles(scope) {
    (scope || document).querySelectorAll('button[data-toggle]:not(.toggle)').forEach(el => {
      makeToggle(el, el.dataset.toggle === 'on', on => el.dispatchEvent(new CustomEvent('sw-toggle', { bubbles: true, detail: on })));
    });
  }

  // ---------- モックの外枠 ----------
  function docBar(current) {
    const links = [
      ['index.html', '入口', 'Overview'],
      ['settings.html', '設定画面', 'Settings'],
      ['tray.html', 'トレイ', 'Tray'],
      ['launcher.html', 'ランチャー', 'Launcher'],
    ];
    const bar = document.createElement('header');
    bar.className = 'doc-bar';
    bar.innerHTML =
      '<a class="doc-brand" href="index.html">' + logo(20) + '<span>Snapwell UI</span></a>' +
      '<nav class="doc-links">' + links.map(([href, ja, en]) =>
        '<a href="' + href + '"' + (href === current ? ' aria-current="page"' : '') + ' data-ja="' + ja + '" data-en="' + en + '">' + ja + '</a>').join('') + '</nav>' +
      '<div class="doc-controls">' +
      '<div class="seg" data-control="variant" role="group" aria-label="案と現状"><button data-value="tobe" data-ja="案 (to-be)" data-en="To-be">案</button><button data-value="asis" data-ja="現状 (as-is)" data-en="As-is">現状</button></div>' +
      '<div class="seg" data-control="theme" role="group" aria-label="配色"><button data-value="auto" data-ja="OS" data-en="OS">OS</button><button data-value="light" data-ja="ライト" data-en="Light">ライト</button><button data-value="dark" data-ja="ダーク" data-en="Dark">ダーク</button></div>' +
      '<div class="seg" data-control="lang" role="group" aria-label="言語"><button data-value="ja">日本語</button><button data-value="en">English</button></div>' +
      '</div>';
    bar.querySelectorAll('.seg[data-control] button').forEach(b => b.addEventListener('click', () => set(b.parentElement.dataset.control, b.dataset.value)));
    document.body.prepend(bar);
  }

  // ロゴ: 窓の枠、投げの点線、止まった四角（既存のアイコンの形）
  function logo(size) {
    return '<svg width="' + size + '" height="' + size + '" viewBox="0 0 44 44" aria-hidden="true">' +
      '<rect x="3" y="3" width="38" height="38" rx="9" fill="none" stroke="var(--sw-ink)" stroke-width="4"/>' +
      '<path d="M10 34 L24 20" stroke="var(--sw-ink)" stroke-width="3" stroke-dasharray="3 4" fill="none"/>' +
      '<rect x="23" y="7" width="14" height="14" rx="3" fill="var(--sw-accent)"/></svg>';
  }

  window.SW = { state, t, set, apply, onChange: fn => listeners.push(fn), Spring, icon, makeToggle, hydrateToggles, docBar, logo, COPY_TOBE, reduced };
})();
