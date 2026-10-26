// 効果音のタイミング表を作る: node table.mjs [<cues.json>]  → audio/TIMING.md
// cues.json は render.mjs が台本 (film.js) の時刻と物理の事象から書く。鍵が無くても作れる。
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const file = resolve(process.argv[2] || resolve(here, 'cues.json'));
const cfg = JSON.parse(readFileSync(resolve(here, 'sounds.json'), 'utf8'));
const { duration, fps, cues } = JSON.parse(readFileSync(file, 'utf8'));
const rows = [];
for (const c of cues) {
  const m = cfg.cue_map[c.name];
  if (!m) continue;
  const t = c.t + (m.offset || 0);
  rows.push(`| ${t.toFixed(2)} | ${c.name}${c.k !== undefined ? ` ${c.k}` : ''}${c.preset ? ` (${c.preset})` : ''} | ${m.sound} | ${m.gain} |`);
}
const md = `# 効果音のタイミング表

自動生成: \`node table.mjs\`。元は render.mjs が書く cues.json (動画 ${duration} 秒、${fps} fps)。
音の指示 (prompt) と長さは \`sounds.json\`。時刻は動画の頭からの秒で、offset を含む。

| 秒 | きっかけ | 効果音 | 音量 (dB) |
|---|---|---|---|
${rows.join('\n')}
`;
writeFileSync(resolve(here, 'TIMING.md'), md);
console.log(`${rows.length} 行、TIMING.md に書いた`);
