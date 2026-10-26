/* Snapwell site · the four feel presets and the force law, as the app defines them.
 * Values and formulas follow docs/DESIGN.md §5.1–5.2 (Core: FeelPreset, WindowBody) and the mockup's FEELS.
 * Every stage on this page and the film read them from here, so none of them can drift from the others.
 */
(function (global) {
  'use strict';
  const TAU = Math.PI * 2;

  /** m: mass, f: natural frequency (Hz), z: damping ratio, magnet: strength of the near-field spring. */
  const PRESETS = {
    standard: { id: 'standard', label: 'Standard', m: 0.6, f: 2.4, z: 0.7, magnet: 0.55 },
    crisp: { id: 'crisp', label: 'Crisp', m: 0.8, f: 3.2, z: 0.6, magnet: 0.6 },
    smooth: { id: 'smooth', label: 'Smooth', m: 1.0, f: 1.8, z: 0.8, magnet: 0.5 },
    bouncy: { id: 'bouncy', label: 'Bouncy', m: 0.5, f: 2.8, z: 0.45, magnet: 0.6 },
  };

  const WALL_FREQ = 8; // Hz: the wall is a stiff spring, critically damped both ways
  const CLICK_RADIUS = 28; // px: where the near-field magnet starts to act

  /** k = m(2πf)², c = 2ζ√(km). */
  function spring(p) {
    const k = p.m * (TAU * p.f) ** 2;
    return { m: p.m, k, c: 2 * p.z * Math.sqrt(k * p.m) };
  }
  /** Far spring toward the nearest stop: clamp(ζ·f·0.9, 0.6, 6) Hz. */
  const reachFreq = (p) => Math.min(6, Math.max(0.6, p.z * p.f * 0.9));
  /** Near spring inside the click radius: 3 + 4×strength Hz. */
  const clickFreq = (p) => 3 + 4 * p.magnet;
  /** Modal parameters in the shape Feel.Body takes. */
  const modal = (p) => ({ mass: p.m, freq: p.f, zeta: p.z });

  /** A force for Feel.Body2D that keeps a window inside [0,maxX]×[0,maxY]. It pushes back, never pins. */
  function wall(p, limits) {
    const kw = p.m * (TAU * WALL_FREQ) ** 2;
    const cw = 2 * Math.sqrt(kw * p.m);
    const axis = (x, v, hi) => (x < 0 ? -kw * x - cw * v : x > hi ? -kw * (x - hi) - cw * v : 0);
    return (x, y, vx, vy) => { const l = limits(); return [axis(x, vx, l.x), axis(y, vy, l.y)]; };
  }

  global.SnapPresets = { PRESETS, spring, reachFreq, clickFreq, modal, wall, CLICK_RADIUS, WALL_FREQ };
})(window);
