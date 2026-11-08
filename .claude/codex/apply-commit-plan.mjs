// 使い方: node .claude/codex/apply-commit-plan.mjs <worktree> <last.json> [--dry-run]
// Codex が返したコミット計画のとおり、項目ごとに git add と git commit をする。
// 複数の項目が触るファイル (shared_files) があれば、何もせずに止める (手で git add -p で分ける)。
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const [worktree, planPath, flag] = process.argv.slice(2);
const dryRun = flag === '--dry-run';
const plan = JSON.parse(readFileSync(planPath, 'utf8'));
const git = (...args) => execFileSync('git', ['-C', worktree, ...args], { encoding: 'utf8' });
const trailer = '\n\nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\n';

if (plan.shared_files.length) {
  console.error('shared_files があるので止める:', plan.shared_files.map((f) => f.path).join(', '));
  process.exit(2);
}
// shared_files は計画の自己申告なので、items の files そのものから複数の項目に属するパスを数える
const owners = new Map();
for (const item of plan.items) {
  for (const path of item.files) owners.set(path, [...(owners.get(path) ?? []), item.id]);
}
const duplicated = [...owners].filter(([, ids]) => ids.length > 1);
if (duplicated.length) {
  console.error('複数の項目に属するファイルがあるので止める:',
    duplicated.map(([path, ids]) => `${path} (${ids.join(', ')})`).join(', '));
  process.exit(6);
}
// すでにステージされた変更があると、git commit が項目の範囲を越えてまとめてコミットする
const staged = git('diff', '--cached', '--name-only').split('\n').filter(Boolean);
if (staged.length) {
  console.error('ステージ済みの変更があるので止める (git restore --staged で戻す):', staged.join(', '));
  process.exit(4);
}
const changed = new Set(git('status', '--porcelain', '-uall').split('\n').filter(Boolean).map((l) => l.slice(3)));
const planned = plan.items.flatMap((item) => item.files);
const missing = [...changed].filter((path) => !planned.includes(path));
if (missing.length) {
  console.error('計画に無い変更があるので止める:', missing.join(', '));
  process.exit(3);
}
for (const item of plan.items) {
  console.log(`${item.id}: ${item.message} (${item.files.length} files)`);
  if (dryRun) continue;
  git('add', '--', ...item.files);
  git('commit', '-q', '-m', item.message + trailer);
  const committed = git('diff-tree', '--no-commit-id', '--name-only', '-r', 'HEAD').split('\n').filter(Boolean);
  const stray = committed.filter((path) => !item.files.includes(path));
  if (stray.length) {
    console.error(`${item.id}: 項目の外のファイルが入った。止める:`, stray.join(', '));
    process.exit(5);
  }
}

