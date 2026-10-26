/* Snapwell site · the hero stage: the opening piece (js/intro.js) plays once, ends on the logo, and stays
 * alive: a click sends a ripple through the dots and nudges the nearest window. Reduced motion shows the
 * finished frame and nothing else. The loop sleeps when everything is still or the stage is off screen.
 */
(function () {
  'use strict';
  const { Intro } = window.SnapIntro;
  const box = document.getElementById('intro');
  const cv = document.getElementById('introCanvas');
  const logoEl = document.getElementById('introLogo');
  const replay = document.getElementById('introReplay');
  if (!box || !cv) return;

  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const logo = window.Brand.logo(logoEl);
  let intro = null;
  let raf = 0;
  let last = 0;
  let visible = true;
  let timers = [];

  const toRgb = (c) => {
    const k = document.createElement('canvas').getContext('2d');
    k.fillStyle = c;
    const v = k.fillStyle;
    if (v[0] === '#') return [1, 3, 5].map((i) => parseInt(v.slice(i, i + 2), 16));
    const m = /rgba?\(([^)]+)\)/.exec(v);
    return m ? m[1].split(',').slice(0, 3).map(Number) : [37, 99, 235];
  };
  const colors = () => {
    const cs = getComputedStyle(box);
    const v = (n) => cs.getPropertyValue(n).trim();
    return {
      accent: toRgb(v('--intro-accent')), ink: toRgb(v('--text')), bg: toRgb(v('--bg')),
      surface: v('--surface'), surface2: v('--surface-2'), borderStrong: v('--border-strong'), skeleton: v('--skeleton'),
    };
  };

  function build(playing) {
    timers.forEach(clearTimeout);
    timers = [];
    if (raf) { cancelAnimationFrame(raf); raf = 0; }
    const W = box.clientWidth;
    const H = box.clientHeight;
    if (!W || !H) return;
    intro = new Intro(cv, {
      width: W, height: H, dpr: Math.min(2, devicePixelRatio || 1), colors: colors(), veilMax: 0.8, seed: 11,
      onBeat: (name) => { if (name === 'logo') logo.show((fn, s) => timers.push(setTimeout(fn, s * 1000))); },
    });
    if (playing && !reduced.matches) {
      logo.reset();
      start();
    } else {
      intro.rest();
      intro.draw();
      logo.shown();
    }
  }

  function frame(now) {
    raf = 0;
    if (!intro || !visible) return;
    const dt = Math.min(0.05, (now - last) / 1000);
    last = now;
    intro.step(dt);
    intro.draw();
    if (intro.busy) raf = requestAnimationFrame(frame);
  }
  function start() {
    if (raf || !intro || !visible) return;
    last = performance.now();
    raf = requestAnimationFrame(frame);
  }

  box.addEventListener('pointerdown', (e) => {
    if (reduced.matches || !intro) return;
    const r = box.getBoundingClientRect();
    const x = e.clientX - r.left;
    const y = e.clientY - r.top;
    intro.ping(x, y, 12);
    intro.poke(x, y);
    start();
  });
  replay.addEventListener('click', () => build(true));

  new IntersectionObserver((entries) => {
    visible = entries.some((e) => e.isIntersecting);
    if (visible) start();
  }).observe(box);
  let w0 = 0;
  new ResizeObserver(() => {
    const w = box.clientWidth;
    if (w && w !== w0) {
      const playing = !intro || !intro.done;
      w0 = w;
      build(playing);
    }
  }).observe(box);
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (intro) { intro.colors = colors(); intro.draw(); }
  });
  reduced.addEventListener('change', () => build(false));
})();
