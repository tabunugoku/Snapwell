/* ホームページの言語の判定。副作用のない関数だけを持つ (描画前に <head> で読み込み、Node のテストからも同じファイルを読む)。
 * 言語の値は 'en' | 'ja' | 'zh-hans' | 'zh-hant'。
 *   ja・ja-*                               → ja
 *   zh-Hant*、zh-TW・zh-HK・zh-MO           → zh-hant
 *   zh-Hans*、zh-CN・zh-SG、地域のない zh    → zh-hans
 *   en・en-*                               → en
 * 文字 (Hans / Hant) の指定は地域より先に見る (zh-Hant-CN は繁体字)。
 * 地域が上の表にない zh-XX (zh-MY など) は簡体字にする。
 * 優先順 (navigator.languages) の先頭から見て、最初に対応する言語を採る。どれにも当たらなければ英語。 */
(function (root) {
  'use strict';
  const LANGS = ['en', 'ja', 'zh-hans', 'zh-hant'];

  /** 言語タグ 1 つを、対応する言語の値にする。対応しなければ null。 */
  function match(tag) {
    const t = String(tag == null ? '' : tag).trim().toLowerCase().replace(/_/g, '-');
    if (/^ja(-|$)/.test(t)) return 'ja';
    if (/^en(-|$)/.test(t)) return 'en';
    if (/^zh(-|$)/.test(t)) {
      const parts = t.split('-').slice(1);
      if (parts.includes('hant')) return 'zh-hant';
      if (parts.includes('hans')) return 'zh-hans';
      const region = parts.find((p) => /^[a-z]{2}$/.test(p));
      return region === 'tw' || region === 'hk' || region === 'mo' ? 'zh-hant' : 'zh-hans';
    }
    return null;
  }

  /** 優先順の一覧から言語を決める。 */
  function detect(tags) {
    const list = Array.isArray(tags) ? tags : (tags && typeof tags.length === 'number' ? Array.from(tags) : []);
    for (const tag of list) {
      const m = match(tag);
      if (m) return m;
    }
    return 'en';
  }

  /** 保存した選択が有効ならそれ、なければブラウザーの言語 (languages があればその優先順、なければ language) で決める。 */
  function choose(stored, languages, language) {
    if (LANGS.includes(stored)) return stored;
    const list = languages && languages.length ? languages : [language];
    return detect(list);
  }

  const api = { LANGS, match, detect, choose };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.SnapLang = api;
})(typeof window !== 'undefined' ? window : globalThis);
