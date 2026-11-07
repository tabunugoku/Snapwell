// ホームページの言語の判定 (site/js/lang.js) のテスト。実行: node --test tools/site/lang.test.js
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { detect, choose, match, LANGS } = require('../../site/js/lang.js');

test('先頭の言語から、最初に対応するものを採る', () => {
  const cases = [
    [['ja-JP'], 'ja'],
    [['ja'], 'ja'],
    [['zh-CN'], 'zh-hans'],
    [['zh-SG'], 'zh-hans'],
    [['zh-Hans'], 'zh-hans'],
    [['zh-Hans-CN'], 'zh-hans'],
    [['zh-TW'], 'zh-hant'],
    [['zh-HK'], 'zh-hant'],
    [['zh-MO'], 'zh-hant'],
    [['zh-Hant'], 'zh-hant'],
    [['zh-Hant-TW'], 'zh-hant'],
    [['zh'], 'zh-hans'],
    [['fr'], 'en'],
    [['fr', 'ja'], 'ja'],
    [['en-US', 'zh-TW'], 'en'],
    [['de', 'zh-HK', 'ja'], 'zh-hant'],
    [[], 'en'],
  ];
  for (const [tags, want] of cases) assert.equal(detect(tags), want, JSON.stringify(tags));
});

test('文字の指定は地域より先に見る', () => {
  assert.equal(match('zh-Hant-CN'), 'zh-hant');
  assert.equal(match('zh-Hans-TW'), 'zh-hans');
  assert.equal(match('zh-cmn-Hans-CN'), 'zh-hans');
  assert.equal(match('zh-yue-HK'), 'zh-hant');
});

test('大文字小文字・下線・未登録の地域・対応しない言語', () => {
  assert.equal(match('ZH-tw'), 'zh-hant');
  assert.equal(match('zh_TW'), 'zh-hant');
  assert.equal(match('zh-MY'), 'zh-hans');
  assert.equal(match('ko-KR'), null);
  assert.equal(match('jam'), null); // ja で始まるだけの別の言語
  assert.equal(match('zhx'), null);
  assert.equal(match(''), null);
  assert.equal(match(undefined), null);
});

test('保存した選択が優先される。壊れた値は無視する', () => {
  for (const l of LANGS) assert.equal(choose(l, ['ja-JP'], 'ja-JP'), l);
  assert.equal(choose('zh-hant', ['ja-JP'], 'ja-JP'), 'zh-hant');
  assert.equal(choose('xx', ['zh-CN'], 'zh-CN'), 'zh-hans');
  assert.equal(choose(null, ['zh-HK'], 'zh-HK'), 'zh-hant');
  assert.equal(choose('', undefined, 'ja'), 'ja'); // languages がなければ language
  assert.equal(choose(null, [], 'zh-TW'), 'zh-hant');
  assert.equal(choose(null, undefined, undefined), 'en');
});

test('languages は配列でなくても (array-like) 読める', () => {
  assert.equal(detect({ 0: 'fr', 1: 'zh-TW', length: 2 }), 'zh-hant');
});
