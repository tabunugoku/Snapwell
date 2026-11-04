// 効果音と BGM を重ねる: node mix.mjs --lang en|ja [--video <無音の mp4 と webm のあるフォルダ。既定 out/<lang>>] [--cues <既定 out/<lang>/cues.json>] [--out <既定 out/with-sound/<lang>>] [--no-bgm]
// render.mjs が書いた cues.json の時刻に、sfx/ の効果音を置き、audio/bgm/bgm.mp3 があれば効果音の鳴る間だけ下げて (サイドチェーン) 重ね、無音の映像へ足す (映像は再エンコードしない)。
// 音は言語に依らないので、同じ sfx と bgm を ja・en の両方に使う。足りない効果音があれば何もせず、generate-sfx.mjs を先に走らせるよう案内して終わる。
import { readFileSync, existsSync, mkdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? process.argv[i + 1] : d; };
const lang = arg('lang', 'en');
const film = resolve(here, '..');
const videoDir = resolve(arg('video', resolve(film, 'out', lang)));
const cuesFile = resolve(arg('cues', resolve(videoDir, 'cues.json')));
const out = resolve(arg('out', resolve(film, 'out', 'with-sound', lang)));
const cfg = JSON.parse(readFileSync(resolve(here, 'sounds.json'), 'utf8'));
const bgmCfg = JSON.parse(readFileSync(resolve(here, 'bgm.json'), 'utf8'));
const bgmFile = resolve(here, 'bgm', 'bgm.mp3');
const useBgm = !process.argv.includes('--no-bgm') && existsSync(bgmFile);
const { duration, cues } = JSON.parse(readFileSync(cuesFile, 'utf8'));

const hits = [];
cues.forEach((c, i) => {
  const m = cfg.cue_map[c.name];
  if (!m) return;
  // 同じ音が続くときの単調さを抑えるため、きっかけごとに決まった ±1 dB の揺らぎを足す
  const wob = (((i * 7) % 5) - 2) * 0.5;
  hits.push({ file: resolve(here, 'sfx', `${m.sound}.mp3`), t: c.t + (m.offset || 0), gain: m.gain + wob });
});
// 生成された音は大きさがまちまちなので、各効果音をまず最大値 0 dB にそろえ、そのうえで cue_map の gain を掛ける
const peakOf = (file) => {
  const r = spawnSync('ffmpeg', ['-i', file, '-af', 'volumedetect', '-f', 'null', '-'], { encoding: 'utf8' });
  const m = /max_volume: (-?[\d.]+) dB/.exec(r.stderr || '');
  return m ? Number(m[1]) : 0;
};
const peaks = new Map();
for (const h of hits) {
  if (!existsSync(h.file)) continue;
  if (!peaks.has(h.file)) peaks.set(h.file, peakOf(h.file));
  h.gain += Math.min(24, -peaks.get(h.file));
}
const missing = [...new Set(hits.map((h) => h.file))].filter((f) => !existsSync(f));
if (missing.length) {
  console.error('効果音が足りない: ' + missing.map((f) => f.split(/[\/]/).pop()).join(', '));
  console.error('先に ELEVENLABS_API_KEY を設定して node generate-sfx.mjs を実行する (README を参照)。');
  process.exit(2);
}
mkdirSync(out, { recursive: true });
const run = (a) => {
  const r = spawnSync('ffmpeg', a, { stdio: 'inherit' });
  if (r.status !== 0) throw new Error('ffmpeg が失敗した');
};
const inputs = hits.flatMap((h) => ['-i', h.file]);
const chains = hits.map((h, i) => `[${i}:a]adelay=${Math.round(h.t * 1000)}:all=1,volume=${h.gain.toFixed(1)}dB[a${i}]`);
const mixIn = hits.map((_, i) => `[a${i}]`).join('');
const sfxGraph = `${chains.join(';')};${mixIn}amix=inputs=${hits.length}:normalize=0:duration=longest,apad,atrim=0:${duration},asetpts=N/SR/TB`;
if (!useBgm) {
  run(['-y', ...inputs, '-filter_complex', `${sfxGraph},alimiter=limit=0.89[aout]`, '-map', '[aout]', '-ar', '44100', resolve(out, 'mix.wav')]);
} else {
  // BGM: 長さをそろえ、頭にフェードイン、末尾にフェードアウト、音量をそろえる。効果音が鳴る間は 4 dB 前後下げる (サイドチェーン)
  const b = bgmCfg.mix;
  const bgmIdx = hits.length;
  const graph = `${sfxGraph},asplit=2[sfxA][sfxB];`
    + `[${bgmIdx}:a]atrim=0:${duration},asetpts=N/SR/TB,loudnorm=I=${b.lufs}:TP=-3:LRA=7,`
    + `afade=t=in:d=${b.fadeIn},afade=t=out:st=${(duration - b.fadeOut).toFixed(2)}:d=${b.fadeOut}[bgm0];`
    + `[bgm0][sfxA]sidechaincompress=threshold=${b.duckThreshold}:ratio=${b.duckRatio}:attack=15:release=350[bgm1];`
    + `[sfxB][bgm1]amix=inputs=2:normalize=0:duration=longest,alimiter=limit=0.89,atrim=0:${duration}[aout]`;
  run(['-y', ...inputs, '-i', bgmFile, '-filter_complex', graph, '-map', '[aout]', '-ar', '44100', resolve(out, 'mix.wav')]);
}
const v = (ext) => resolve(videoDir, `snapwell-${lang}.${ext}`);
const o = (ext) => resolve(out, `snapwell-${lang}.${ext}`);
run(['-y', '-i', v('mp4'), '-i', resolve(out, 'mix.wav'), '-map', '0:v', '-map', '1:a', '-c:v', 'copy', '-c:a', 'aac', '-b:a', '160k', '-movflags', '+faststart', '-shortest', o('mp4')]);
run(['-y', '-i', v('webm'), '-i', resolve(out, 'mix.wav'), '-map', '0:v', '-map', '1:a', '-c:v', 'copy', '-c:a', 'libopus', '-b:a', '128k', '-shortest', o('webm')]);
console.log(`done ${out} (BGM: ${useBgm ? 'あり' : 'なし'})`);
