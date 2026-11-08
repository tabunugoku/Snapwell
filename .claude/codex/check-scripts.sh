#!/usr/bin/env bash
# 使い方: bash .claude/codex/check-scripts.sh
# apply-commit-plan.mjs と run-agy-exec.sh の回帰確認。一時フォルダの中だけで動かし、実際の agy は呼ばない。
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
T="$(mktemp -d)"
trap 'rm -rf "$T"' EXIT
fail=0
ok() { echo "ok   $1"; }
ng() { echo "FAIL $1"; fail=1; }

# --- apply-commit-plan: ステージ済みの変更は何も変えずに止める ---
R="$T/repo"
git init -q "$R" && git -C "$R" config user.email t@example.com && git -C "$R" config user.name t
echo base > "$R/base.txt" && git -C "$R" add base.txt && git -C "$R" commit -q -m base
echo a > "$R/a.txt"; echo b > "$R/b.txt"
git -C "$R" add b.txt
cat > "$T/plan.json" <<'JSON'
{"shared_files":[],"items":[{"id":"A","message":"add a","files":["a.txt"]},{"id":"B","message":"add b","files":["b.txt"]}]}
JSON
before="$(git -C "$R" rev-parse HEAD)"
node "$HERE/apply-commit-plan.mjs" "$R" "$T/plan.json" >/dev/null 2>&1; code=$?
{ [ $code -eq 4 ] && [ "$(git -C "$R" rev-parse HEAD)" = "$before" ]; } \
  && ok "staged later item: stopped without committing" || ng "staged later item (exit=$code)"

# shared_files が空でも、複数の項目に同じファイルがある計画は、dry-run も含めて何も変えずに止める
echo c > "$R/common.txt"; echo a2 > "$R/a2.txt"; echo b2 > "$R/b2.txt"
cat > "$T/dup.json" <<'JSON'
{"shared_files":[],"items":[{"id":"A","message":"a","files":["common.txt","a2.txt"]},{"id":"B","message":"b","files":["common.txt","b2.txt"]}]}
JSON
git -C "$R" restore --staged b.txt
before="$(git -C "$R" rev-parse HEAD)"
node "$HERE/apply-commit-plan.mjs" "$R" "$T/dup.json" --dry-run >/dev/null 2>&1; dcode=$?
node "$HERE/apply-commit-plan.mjs" "$R" "$T/dup.json" >/dev/null 2>&1; code=$?
{ [ $dcode -eq 6 ] && [ $code -eq 6 ] && [ "$(git -C "$R" rev-parse HEAD)" = "$before" ] \
  && [ -z "$(git -C "$R" diff --cached --name-only)" ]; } \
  && ok "duplicate path across items: rejected (dry-run too)" || ng "duplicate path (dry-run=$dcode, run=$code)"
rm -f "$R/common.txt" "$R/a2.txt" "$R/b2.txt"

# ステージが空なら、項目ごとに自分のファイルだけがコミットされる
node "$HERE/apply-commit-plan.mjs" "$R" "$T/plan.json" >/dev/null 2>&1; code=$?
{ [ $code -eq 0 ] && [ "$(git -C "$R" show --name-only --format= HEAD~1)" = "a.txt" ] \
  && [ "$(git -C "$R" show --name-only --format= HEAD)" = "b.txt" ]; } \
  && ok "clean index: one file per commit" || ng "clean index (exit=$code)"

# --- run-agy-exec: 偽の agy で、相対の出力先と model の省略を確かめる ---
FAKE="$T/fake-agy.sh"
cat > "$FAKE" <<'SH'
#!/usr/bin/env bash
# 受け取った -p と --model を args.txt に残し、最小の成功結果を返す
prompt=""; model=""
while [ $# -gt 0 ]; do
  case "$1" in -p) prompt="$2"; shift 2;; --model) model="$2"; shift 2;; *) shift;; esac
done
printf '%s\n%s\n' "$model" "$prompt" > "$ARGS_FILE"
echo '{"status":"SUCCESS","structured_output":{"items":[]}}'
SH
chmod +x "$FAKE"
echo "TASK-MARKER" > "$T/p.md"
mkdir -p "$T/caller" "$T/work"
export ARGS_FILE="$T/args.txt"
(cd "$T/caller" && AGY="$FAKE" bash "$HERE/run-agy-exec.sh" "$T/work" "$T/p.md" out) >/dev/null 2>&1
[ "$(sed -n 1p "$ARGS_FILE")" = "gemini-3.8-flash-high" ] \
  && ok "model omitted: Flash is the default" || ng "model default: $(sed -n 1p "$ARGS_FILE")"
grep -q "TASK-MARKER" "$ARGS_FILE" \
  && ok "relative output folder: prompt reaches agy" || ng "relative output folder: prompt missing"

# 古い prompt.md が残っていても、今回のプロンプトが渡る
echo "OLD-PROMPT" > "$T/caller/out/prompt.md"
(cd "$T/caller" && AGY="$FAKE" bash "$HERE/run-agy-exec.sh" "$T/work" "$T/p.md" out) >/dev/null 2>&1
{ grep -q "TASK-MARKER" "$ARGS_FILE" && ! grep -q "OLD-PROMPT" "$ARGS_FILE"; } \
  && ok "stale prompt.md is not used" || ng "stale prompt.md was used"

# プロンプトの元ファイルが無ければ、agy を呼ばずに止める
rm -f "$ARGS_FILE"
(cd "$T/caller" && AGY="$FAKE" bash "$HERE/run-agy-exec.sh" "$T/work" "$T/missing.md" out) >/dev/null 2>&1; code=$?
{ [ $code -ne 0 ] && [ ! -e "$ARGS_FILE" ]; } && ok "missing prompt: aborted" || ng "missing prompt (exit=$code)"

exit $fail
