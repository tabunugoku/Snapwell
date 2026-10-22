/* 偽の時計。requestAnimationFrame と performance.now を差し替え、__tick(ms) で 1 コマだけ進める。
 * 実時間に依存しないので、書き出すたびに同じ映像になる。 */
(function () {
  'use strict';
  let t = 0;
  let queue = [];
  performance.now = () => t;
  window.requestAnimationFrame = (cb) => { queue.push(cb); return queue.length; };
  window.cancelAnimationFrame = () => {};
  const hooks = [];
  window.__onTick = (fn) => hooks.push(fn);
  window.__tick = (ms) => {
    t += ms;
    for (const fn of hooks) fn(t / 1000);
    const run = queue;
    queue = [];
    for (const cb of run) cb(t);
  };
})();
