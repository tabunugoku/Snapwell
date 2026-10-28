/* 和文の改行位置を文節の切れ目にそろえる。
 * CSS の word-break: keep-all で文字の間の改行を止め、ここで文節の切れ目に <wbr> を入れる
 * （Chromium・Firefox・Safari のどれでも効く。word-break: auto-phrase は Chromium だけ）。
 * 数と単位（1.4 秒、0〜200 px）、英字の語（Ctrl+Alt+Space、SHA-256）、キーの組み合わせは切らない。
 * 切れ目の判定は「ひらがなの後ろで、ひらがな以外が始まるところ」を基本にし、次は切らない:
 *   ・「の」の直後（「実際の録画」のような名詞句の途中）
 *   ・漢字に付く 1 字の送り仮名の直後（「置き換える」「読み込み」）。1 字なら助詞のときだけ切る
 * 最後の切れ目のあとが 3 字未満なら、その切れ目を外して短い行を残さない。
 * JS が動かないときは、ブラウザーの既定の改行のまま（keep-all は付けない）。 */
(function () {
  'use strict';
  const HIRA = /[ぁ-ゟ]/;
  const AFTER = /[、。，．・」』）！？]/;
  const OPEN = /[「『（]/;
  const PARTICLE = 'はがをにへでともやて';
  // 切らないまとまり: 名前、数と単位、記号でつないだ英字の語
  const KEEP = new RegExp([
    'Windows 11', 'SignPath Foundation',
    '\\d[\\d.,]*(?: ?〜 ?\\d[\\d.,]*)?(?: × \\d+)?(?: ?(?:px|秒|つ|区画|分割|方式|枚|版|セル|分の 1))?',
    '[A-Za-z0-9{}]+(?:[.+\\-][A-Za-z0-9{}]+)+',
  ].join('|'), 'g');

  /** 文字列を、切らないまとまりと 1 字ずつの単位に分ける。 */
  function atoms(text) {
    const out = [];
    let last = 0;
    for (const m of text.matchAll(KEEP)) {
      for (const ch of text.slice(last, m.index)) out.push({ t: ch });
      out.push({ t: m[0], keep: true });
      last = m.index + m[0].length;
    }
    for (const ch of text.slice(last)) out.push({ t: ch });
    return out;
  }
  const isHira = (a) => a && !a.keep && HIRA.test(a.t);
  const isSpace = (a) => a && !a.keep && a.t === ' ';

  /** テキストを、<wbr> と改行しない部分に分けた DocumentFragment にする。 */
  function wrap(text) {
    const a = atoms(text);
    const parts = []; // 文字列か 'WBR'
    let run = 0; // 直前まで続いているひらがなの数
    for (let i = 0; i < a.length; i++) {
      const prev = a[i - 1];
      const cur = a[i];
      if (prev && !isSpace(cur) && !isSpace(prev)) {
        let cut = false;
        if (isHira(prev) && !isHira(cur) && !AFTER.test(cur.t)) {
          cut = prev.t !== 'の' && (run >= 2 || PARTICLE.includes(prev.t));
        } else if (!prev.keep && AFTER.test(prev.t) && !AFTER.test(cur.t)) {
          cut = true;
        } else if (!cur.keep && OPEN.test(cur.t) && !OPEN.test(prev.t)) {
          cut = true;
        }
        if (cut) parts.push('WBR');
      }
      if (isSpace(cur)) {
        // 助詞の前の空白（「x64 と」「Snapwell の」）と、「の」のあとの空白（「Windows の SmartScreen」）では切らない
        const keepTight = isHira(a[i + 1]) || !a[i + 1] || (prev && !prev.keep && prev.t === 'の');
        parts.push(keepTight ? ' ' : ' ');
      } else {
        parts.push(cur.keep ? { keep: cur.t } : cur.t);
      }
      run = isHira(cur) ? run + 1 : 0;
    }
    // 最後の切れ目のあとが 3 字未満なら、その切れ目を外す
    const tail = (k) => parts.slice(k + 1).map((p) => (typeof p === 'string' ? p : p.keep)).join('')
      .replace(/[\s 、。！？」』）]/g, '').length;
    for (let k = parts.length - 1; k >= 0; k--) {
      if (parts[k] === 'WBR' || parts[k] === ' ') {
        if (tail(k) < 3) parts[k] = parts[k] === ' ' ? ' ' : '';
        break;
      }
    }
    const frag = document.createDocumentFragment();
    let buf = '';
    const flush = () => { if (buf) frag.appendChild(document.createTextNode(buf)); buf = ''; };
    for (const p of parts) {
      if (p === 'WBR') { flush(); frag.appendChild(document.createElement('wbr')); }
      else if (typeof p === 'string') buf += p;
      else { flush(); const s = document.createElement('span'); s.className = 'nb'; s.textContent = p.keep; frag.appendChild(s); }
    }
    flush();
    return frag;
  }

  /** 和文の要素の中のテキストを置き換える。kbd と code の中は触らず、kbd の並びの間の空白は切らない。 */
  function run(rootEl) {
    const walker = document.createTreeWalker(rootEl, NodeFilter.SHOW_TEXT, {
      acceptNode: (n) => (n.parentElement.closest('kbd, code, script, style, .nb') || !n.data.trim()
        ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
    });
    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);
    for (const n of nodes) n.replaceWith(wrap(n.data));
    // キーの組み合わせ（<kbd>Ctrl</kbd> <kbd>Alt</kbd> …）の間の空白は改行しない空白にする
    rootEl.querySelectorAll('kbd + kbd').forEach((k) => {
      const sp = k.previousSibling;
      if (sp && sp.nodeType === 3 && /^\s+$/.test(sp.data)) sp.data = ' ';
    });
  }

  document.querySelectorAll('[data-l="ja"]').forEach((el) => { if (!el.parentElement.closest('[data-l="ja"]')) run(el); });
  document.documentElement.classList.add('ja-wrap');
  window.SnapJaWrap = { wrap };
})();
