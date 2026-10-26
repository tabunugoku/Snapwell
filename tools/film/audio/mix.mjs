// 効果音を重ねる: node mix.mjs [--video <無音の mp4 と webm のあるフォルダ。既定 site/video>] [--cues <既定 audio/cues.json>] [--out <出力フォルダ>]
// render.mjs が書いた cues.json の時刻に、sfx/ の効果音を置いて 1 本の音にし、無音の映像へ足す (映像は再エンコードしない)。
// 足りない効果音があれば何もせず、generate-sfx.mjs を先に走らせるよう案内して終わる。
import { readFileSync, existsSync, mkdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? process.argv[i + 1] : d; };
const videoDir = resolve(arg('video', resolve(here, '../../../site/video'))); // 既定は無音版 (site/video)
const cuesFile = resolve(arg('cues', resolve(here, 'cues.json'))); // 台本の時刻。映像を作り直したら out/cues.json をここへ写す
const out = resolve(arg('out', resolve(here, '../out/with-sound')));
const cfg = JSON.parse(readFileSync(resolve(here, 'sounds.json'), 'utf8'));
const { duration, cues } = JSON.parse(readFileSync(cuesFile, 'utf8'));

const hits = [];
cues.forEach((c, i) => {
  const m = cfg.cue_map[c.name];
  if (!m) return;
  // 同じ音が続くときの単調さを抑えるため、きっかけごとに決まった ±1 dB の揺らぎを足す
  const wob = (((i * 7) % 5) - 2) * 0.5;
  hits.push({ file: resolve(here, 'sfx', `${m.sound}.mp3`), t: c.t + (m.offset || 0), gain: m.gain + wob });
});
const missing = [...new Set(hits.map((h) => h.file))].filter((f) => !existsSync(f));
if (missing.length) {
  console.error('効果音が足りない: ' + missing.map((f) => f.split(/[\\/]/).pop()).join(', '));
  console.error('先に ELEVENLABS_API_KEY を設定して node generate-sfx.mjs を実行する (README を参照)。');
  process.exit(2);
}
mkdirSync(out, { recursive: true });
const inputs = hits.flatMap((h) => ['-i', h.file]);
const chains = hits.map((h, i) => `[${i}:a]adelay=${Math.round(h.t * 1000)}:all=1,volume=${h.gain.toFixed(1)}dB[a${i}]`);
const mixIn = hits.map((_, i) => `[a${i}]`).join('');
const graph = `${chains.join(';')};${mixIn}amix=inputs=${hits.length}:normalize=0:duration=longest,alimiter=limit=0.89,atrim=0:${duration},asetpts=N/SR/TB[aout]`;
const run = (a) => {
  const r = spawnSync('ffmpeg', a, { stdio: 'inherit' });
  if (r.status !== 0) throw new Error('ffmpeg が失敗した');
};
run(['-y', ...inputs, '-filter_complex', graph, '-map', '[aout]', '-ar', '44100', resolve(out, 'mix.wav')]);
run(['-y', '-i', resolve(videoDir, 'snapwell.mp4'), '-i', resolve(out, 'mix.wav'), '-map', '0:v', '-map', '1:a', '-c:v', 'copy', '-c:a', 'aac', '-b:a', '160k', '-movflags', '+faststart', '-shortest', resolve(out, 'snapwell.mp4')]);
run(['-y', '-i', resolve(videoDir, 'snapwell.webm'), '-i', resolve(out, 'mix.wav'), '-map', '0:v', '-map', '1:a', '-c:v', 'copy', '-c:a', 'libopus', '-b:a', '128k', '-shortest', resolve(out, 'snapwell.webm')]);
console.log('done', out);
