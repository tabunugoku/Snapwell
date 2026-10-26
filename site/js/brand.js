/* Snapwell site · the logo and how it arrives. The mark is the app icon (js/brand-mark.js, generated from
 * assets/icons/app-large.svg); the name is set in Newsreader. Arrival is springs, like everything else:
 * the dashed arc draws, the square pops in on the Bouncy spring, the name rises on the Standard one.
 */
(function (global) {
  'use strict';
  const Feel = global.Feel;
  const P = global.SnapPresets;

  const body = (preset, onFrame, hi = 200) => new Feel.Body({
    ...Feel.fromModal(P.modal(P.PRESETS[preset])), x: 0, target: 0, min: -50, max: hi, onFrame,
  });

  /** Put the icon into an element (the generated SVG, so every place shows the same drawing). */
  function mount(el) { el.innerHTML = global.SNAP_MARK; return el.querySelector('svg'); }

  /**
   * @param {HTMLElement} root  holds a .logo-mark and a .logo-name
   * @returns {{show(), hide(), reset()}} drives opacity, the arc, the square and the name
   */
  function logo(root) {
    const svg = mount(root.querySelector('.logo-mark'));
    const arc = svg.querySelector('.mark-arc');
    const sq = svg.querySelector('.mark-sq');
    const name = root.querySelector('.logo-name');
    sq.style.transformBox = 'fill-box';
    sq.style.transformOrigin = 'center';
    const fade = body('standard', (b) => { svg.style.opacity = Math.min(1, Math.max(0, b.x / 100)).toFixed(3); }, 120);
    const draw = body('smooth', (b) => { arc.style.strokeDashoffset = (40 * (1 - Math.min(1, b.x / 100))).toFixed(2); }, 120);
    const pop = body('bouncy', (b) => {
      const r = b.x / 100;
      sq.style.transform = `translate(${((1 - r) * -16).toFixed(2)}px, ${((1 - r) * 20).toFixed(2)}px) scale(${(0.5 + 0.5 * r).toFixed(3)})`;
      sq.style.opacity = Math.min(1, Math.max(0, r * 2)).toFixed(3);
    });
    const rise = body('standard', (b) => {
      const r = Math.min(1, Math.max(0, b.x / 100));
      name.style.opacity = r.toFixed(3);
      name.style.transform = `translateY(${((1 - r) * 20).toFixed(2)}px)`;
    }, 120);
    arc.style.strokeDasharray = '3 3';
    const all = [fade, draw, pop, rise];
    const set = (v) => all.forEach((b) => b.jump(v));
    set(0);
    return {
      /** staggered: arc, then the square, then the name, each as its own spring */
      show(delay = (fn, s) => setTimeout(fn, s * 1000)) {
        fade.to(100); draw.to(100);
        delay(() => pop.to(100), 0.45);
        delay(() => rise.to(100), 0.35);
      },
      hide() { all.forEach((b) => b.to(0)); },
      reset: () => set(0),
      shown: () => set(100),
    };
  }

  global.Brand = { mount, logo };
})(window);
