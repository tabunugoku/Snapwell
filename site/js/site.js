/* Snapwell site · behaviour. Every move is a Feel body: a spring, never a duration.
 * Small bodies run in px-like units (0..100) so the runtime's rest thresholds apply. */
(function () {
  'use strict';
  const Feel = window.Feel;
  const { Stage, FEELS } = window.SnapStage;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const root = document.documentElement;
  root.classList.add('ready');

  /** A 0..100 spring that writes a custom property in 0..1. */
  function level(el, prop, feel, from = 0) {
    return new Feel.Body({
      feel, x: from, target: from, min: 0, max: 100,
      onFrame: (b) => el.style.setProperty(prop, (b.x / 100).toFixed(4)),
    });
  }

  // ---- entrance: a rise with opacity, staggered by the delay of the target change ----
  const reveals = $$('[data-reveal], .faq-list details, .tile-main > *, .foot > *, .step-then');
  const risers = new Map();
  for (const el of reveals) {
    const body = new Feel.Body({
      feel: 'snappy', x: 0, target: 0, min: 0, max: 100,
      onFrame: (b) => {
        el.style.opacity = (b.x / 100).toFixed(3);
        el.style.transform = `translateY(${((1 - b.x / 100) * 28).toFixed(2)}px)`;
      },
    });
    body.set0 = () => { el.style.opacity = '0'; el.style.transform = 'translateY(28px)'; };
    body.set0();
    risers.set(el, body);
  }
  const seen = new Set();
  const io = new IntersectionObserver((entries) => {
    let n = 0;
    for (const e of entries) {
      if (!e.isIntersecting || seen.has(e.target)) continue;
      seen.add(e.target);
      const body = risers.get(e.target);
      setTimeout(() => body.to(100), 70 * n++);
      io.unobserve(e.target);
    }
  }, { rootMargin: '0px 0px -8% 0px' });
  reveals.forEach((el) => io.observe(el));

  // ---- press and hover feedback ----
  for (const el of $$('[data-fx]')) {
    const body = new Feel.Body({
      feel: 'snappy', x: 0, target: 0, min: 0, max: 100,
      onFrame: (b) => {
        const p = b.x / 100;
        el.style.transform = `scale(${(1 - 0.03 * p).toFixed(4)})`;
        el.style.filter = `brightness(${(1 + 0.18 * p).toFixed(3)})`;
      },
    });
    el.addEventListener('pointerenter', () => body.to(45));
    el.addEventListener('pointerleave', () => body.to(0));
    el.addEventListener('pointerdown', () => body.to(100));
    el.addEventListener('pointerup', () => body.to(45));
    el.addEventListener('focus', () => body.to(45));
    el.addEventListener('blur', () => body.to(0));
  }

  // ---- features: a heavy switch per band; the band's field fills from the thumb ----
  const TRAVEL = 28;
  const bands = $$('[data-band]');
  const bandBodies = bands.map((band) => {
    const sw = $('.switch', band);
    const thumb = $('.thumb', band);
    const body = new Feel.Body({
      feel: 'heavy', x: 0, target: 0, min: 0, max: TRAVEL, name: 'switch',
      onFrame: (b) => {
        const on = b.x / TRAVEL;
        band.style.setProperty('--on', on.toFixed(4));
        sw.style.setProperty('--on', on.toFixed(4));
        thumb.style.setProperty('--tx', `${b.x.toFixed(2)}px`);
        thumb.style.setProperty('--sx', (1 + Math.min(0.25, Math.abs(b.v) / 900)).toFixed(3));
      },
    });
    const set = (on) => { body.to(on ? TRAVEL : 0); sw.setAttribute('aria-checked', String(on)); };
    sw.addEventListener('click', () => set(sw.getAttribute('aria-checked') !== 'true'));
    // the band answers a tap anywhere on it as well
    $('.band-text', band).addEventListener('click', () => set(sw.getAttribute('aria-checked') !== 'true'));
    return set;
  });
  const fio = new IntersectionObserver((entries) => {
    if (!entries.some((e) => e.isIntersecting)) return;
    fio.disconnect();
    bandBodies[0](true);
    bandBodies[2](true);
  }, { threshold: 0.4 });
  if (bands[0]) fio.observe(bands[0]);

  // ---- stages ----
  const wins = () => [
    { title: 'Notes', color: 'var(--blue-tx)', zone: 0, lines: [82, 58, 90, 44] },
    { title: 'Photos', color: 'var(--blue-tx)', zone: 1, lines: [70, 92, 54, 76] },
    { title: 'Mail', color: 'var(--blue-tx)', zone: 3, lines: [88, 64, 50, 80] },
  ];

  // Feel panel
  const R = (k) => $(`[data-r="${k}"]`);
  const names2 = ['top left', 'top right', 'bottom left', 'bottom right'];
  const zoneName = (stage, i) => {
    if (stage.mode === 'corners' || (stage.cols === 2 && stage.rows === 2)) return names2[i] || `zone ${i + 1}`;
    const cols = ['left', 'middle', 'right'];
    const c = i % stage.cols; const r = Math.floor(i / stage.cols);
    return `${r === 0 ? 'top' : 'bottom'} ${cols[c]}`;
  };
  const feelStage = new Stage($('#feelMonitor'), {
    wins: wins(), cols: 2, rows: 2, gap: 8, feel: 'standard',
    onThrow: (t) => {
      R('throw').textContent = `${Math.round(t.speed)} px/s → ${zoneName(feelStage, t.zone)}`;
    },
  });
  function showFeel() {
    const p = feelStage.preset;
    const m = Feel.metrics(Feel.fromModal(SnapPresets.modal(p)));
    R('words').textContent = p.label;
    R('mass').textContent = p.m.toFixed(2);
    R('freq').textContent = `${p.f.toFixed(2)} Hz`;
    R('zeta').textContent = p.z.toFixed(2);
    R('over').textContent = `${(m.overshoot * 100).toFixed(1)} %`;
  }
  showFeel();

  function seg(id, onPick) {
    const box = $(id);
    box.addEventListener('click', (e) => {
      const b = e.target.closest('button');
      if (!b) return;
      $$('button', box).forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
      onPick(b.dataset.v);
    });
  }
  seg('#ctlMode', (v) => feelStage.setMode(v));
  seg('#ctlGrid', (v) => { const [c, r] = v.split('x').map(Number); feelStage.setGrid(c, r); });
  seg('#ctlFeel', (v) => { feelStage.setFeel(v); showFeel(); });
  $('#ctlGap').addEventListener('input', (e) => {
    const g = Number(e.target.value);
    $('#outGap').textContent = `${g} px`;
    feelStage.setGap(g);
  });
  $('#feelThrow').addEventListener('click', () => feelStage.fling());

  // How it works: one window, one scripted throw (a real impulse into the same field)
  const H = (k) => $(`[data-h="${k}"]`);
  const stepLabels = ['Grab', 'Release', 'Settled'];
  const howStage = new Stage($('#howMonitor'), {
    wins: [{ title: 'Notes', color: 'var(--blue-tx)', zone: 0, lines: [82, 58, 90, 44, 70] }],
    cols: 2, rows: 2, gap: 8, feel: 'standard',
    onThrow: (t) => {
      H('speed').textContent = `${Math.round(t.speed)} px/s`;
      H('zone').textContent = zoneName(howStage, t.zone);
    },
  });
  const howWin = howStage.wins[0];
  const stepEls = $$('.step');
  const stepBodies = stepEls.map((el) => level(el, '--a', 'snappy', 0));
  let current = -1;
  function enterStep(i) {
    if (i === current) return;
    const back = i < current;
    const prev = current;
    current = i;
    stepBodies.forEach((b, k) => b.to(k === i ? 100 : 0));
    stepEls.forEach((el, k) => el.classList.toggle('on', k === i));
    $$('button', $('#howTabs')).forEach((b) => b.setAttribute('aria-pressed', String(Number(b.dataset.v) === i)));
    H('step').textContent = stepLabels[i];
    howWin.el.classList.toggle('held', i === 0);
    if (i === 0) {
      H('speed').textContent = '—';
      H('zone').textContent = '—';
      if (back) {
        const r = howStage.rects[0];
        howStage.raise(howWin);
        howWin.zone = 0;
        howWin.bw.to(r.w); howWin.bh.to(r.h);
        howWin.body.kickTo(howStage.field.stops[0]);
      }
    } else if (prev <= 0) {
      howStage.throwTo(howWin, 3);
    }
  }
  // Wide screens: the pinned stage follows the scroll. Narrow screens: three buttons step through it.
  const narrow = matchMedia('(max-width: 900px)');
  const sio = new IntersectionObserver((entries) => {
    if (narrow.matches) return;
    for (const e of entries) if (e.isIntersecting) enterStep(Number(e.target.dataset.step));
  }, { rootMargin: '-45% 0px -45% 0px' });
  $('#howTabs').addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (b) enterStep(Number(b.dataset.v));
  });
  stepEls.forEach((el) => sio.observe(el));
  enterStep(0);
})();
