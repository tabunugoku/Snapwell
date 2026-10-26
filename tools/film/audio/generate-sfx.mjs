// 効果音を ElevenLabs の Sound Effects API で作る: node generate-sfx.mjs [--dry-run] [--force] [--only whoosh,tick]
// 鍵は環境変数 ELEVENLABS_API_KEY から読む (リポジトリにも標準出力にも出さない)。鍵が無いときは何も送らず、手順を示して終わる。
// 出力: audio/sfx/<名前>.mp3 (Git には入れない)。作ってあるものは --force が無ければ飛ばす。
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const flag = (n) => args.includes(`--${n}`);
const only = (() => { const i = args.indexOf('--only'); return i >= 0 ? args[i + 1].split(',') : null; })();
const cfg = JSON.parse(readFileSync(resolve(here, 'sounds.json'), 'utf8'));
const outDir = resolve(here, 'sfx');
const key = process.env.ELEVENLABS_API_KEY;

const list = Object.entries(cfg.sounds).filter(([n]) => !only || only.includes(n));
const seconds = list.reduce((s, [, v]) => s + v.duration, 0);
console.log(`${list.length} 件、指定の長さの合計 ${seconds.toFixed(1)} 秒 (料金の目安: 1 秒あたり 40 クレジット、およそ ${Math.round(seconds * 40)} クレジット)`);
if (flag('dry-run')) {
  for (const [n, v] of list) console.log(`- ${n} (${v.duration}s): ${v.prompt}`);
  process.exit(0);
}
if (!key) {
  console.error('環境変数 ELEVENLABS_API_KEY が無いので、何も送らずに終わる。');
  console.error('PowerShell なら: $env:ELEVENLABS_API_KEY = "<鍵>"; node generate-sfx.mjs');
  process.exit(2);
}
mkdirSync(outDir, { recursive: true });
for (const [name, v] of list) {
  const file = resolve(outDir, `${name}.mp3`);
  if (existsSync(file) && !flag('force')) { console.log(`skip ${name} (作ってある)`); continue; }
  const url = `https://api.elevenlabs.io/v1/sound-generation?output_format=${encodeURIComponent(cfg.output_format)}`;
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'xi-api-key': key, 'Content-Type': 'application/json' },
    body: JSON.stringify({ text: v.prompt, duration_seconds: v.duration, prompt_influence: v.influence ?? 0.3, model_id: cfg.model_id }),
  });
  if (!res.ok) {
    console.error(`失敗 ${name}: HTTP ${res.status} ${(await res.text()).slice(0, 300)}`);
    process.exit(1);
  }
  writeFileSync(file, Buffer.from(await res.arrayBuffer()));
  console.log(`ok ${name}`);
}
console.log('done。mix.mjs で動画に重ねる');
