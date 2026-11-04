// BGM の候補を ElevenLabs Music API で作る: node generate-bgm.mjs [--dry-run] [--force] [--only a,b]
// 鍵は環境変数 ELEVENLABS_API_KEY から読む (リポジトリにも標準出力にも出さない)。出力: audio/bgm/candidates/<名前>.mp3 (Git に入れない)。
// 料金: API は米ドル課金 (1 分 0.15 ドル。2026-11-04 に https://elevenlabs.io/pricing/api で確認)。36 秒の候補 3 本で約 0.27 ドル。
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const flag = (n) => args.includes(`--${n}`);
const only = (() => { const i = args.indexOf('--only'); return i >= 0 ? args[i + 1].split(',') : null; })();
const cfg = JSON.parse(readFileSync(resolve(here, 'bgm.json'), 'utf8'));
const outDir = resolve(here, 'bgm', 'candidates');
function userKey() {
  if (process.platform !== 'win32') return undefined;
  const r = spawnSync('powershell', ['-NoProfile', '-Command', "[Environment]::GetEnvironmentVariable('ELEVENLABS_API_KEY','User')"], { encoding: 'utf8' });
  return r.status === 0 ? r.stdout.trim() || undefined : undefined;
}
const key = process.env.ELEVENLABS_API_KEY || (flag('dry-run') ? undefined : userKey());
const list = Object.entries(cfg.candidates).filter(([n]) => !only || only.includes(n));
const minutes = (list.length * cfg.length_ms) / 60000;
console.log(`${list.length} 本、${cfg.length_ms / 1000} 秒ずつ (目安 ${(minutes * 0.15).toFixed(2)} ドル)`);
if (flag('dry-run')) { for (const [n, p] of list) console.log(`- ${n}: ${p}`); process.exit(0); }
if (!key) { console.error('環境変数 ELEVENLABS_API_KEY が無いので、何も送らずに終わる。'); process.exit(2); }
mkdirSync(outDir, { recursive: true });
for (const [name, prompt] of list) {
  const file = resolve(outDir, `${name}.mp3`);
  if (existsSync(file) && !flag('force')) { console.log(`skip ${name}`); continue; }
  const res = await fetch(`https://api.elevenlabs.io/v1/music?output_format=${encodeURIComponent(cfg.output_format)}`, {
    method: 'POST',
    headers: { 'xi-api-key': key, 'Content-Type': 'application/json' },
    body: JSON.stringify({ prompt, music_length_ms: cfg.length_ms, model_id: cfg.model_id, force_instrumental: true }),
  });
  if (!res.ok) { console.error(`失敗 ${name}: HTTP ${res.status} ${(await res.text()).slice(0, 300)}`); process.exit(1); }
  writeFileSync(file, Buffer.from(await res.arrayBuffer()));
  console.log(`ok ${name}`);
}
