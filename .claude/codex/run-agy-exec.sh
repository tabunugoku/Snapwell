#!/usr/bin/env bash
# 使い方: .claude/codex/run-agy-exec.sh <worktree> <prompt.md> <出力フォルダ> [model]
# Antigravity CLI (agy) のヘッドレスで Gemini に実装させる。run-codex-exec.sh と同じく
# Claude Code の Bash の run_in_background で起動し、出力フォルダの last.json にコミット計画を書く。
# 許可は ~/.gemini/antigravity-cli/settings.json の permissions.allow の command(regex:dotnet build .*) だけ。
# 許可の外のコマンドを出すと、その時点で何も返さずに終わる (agy-preamble.md で避けさせる)。
set -u
W="$1"; P="$2"; OUT="$3"; MODEL="${4:-gemini-4-argon-high}"
HERE="$(cd "$(dirname "$0")" && pwd)"
AGY="${AGY:-$(cygpath "$LOCALAPPDATA")/agy/bin/agy.exe}"
mkdir -p "$OUT"
cat "$HERE/agy-preamble.md" "$P" > "$OUT/prompt.md"
# プロンプトはコマンドラインの引数で渡すので、Windows の上限 (32767 字) に収める
if [ "$(wc -m < "$OUT/prompt.md")" -gt 30000 ]; then
  echo "prompt too long: $(wc -m < "$OUT/prompt.md") chars" >&2; exit 2
fi
start=$(date +%s)
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# accept-edits でファイルの編集だけを自動で許可する。--sandbox で端末の操作を制限する
(cd "$W" && "$AGY" -p "$(cat "$OUT/prompt.md")" --model "$MODEL" --mode accept-edits --sandbox \
  --output-format json --json-schema "$HERE/commit-plan.schema.json" --print-timeout 60m) \
  > "$OUT/result.json" 2> "$OUT/stderr.log"
code=$?
# structured_output を last.json に取り出す。拒否されたコマンドや結果が無いときは失敗として返す
node -e '
const fs = require("fs");
const [resultPath, lastPath] = process.argv.slice(1);
let r;
try { r = JSON.parse(fs.readFileSync(resultPath, "utf8")); } catch { console.log("result=unparsable"); process.exit(4); }
const denied = (r.denied_actions || []).map((a) => a.display_name || a.action);
console.log(`status=${r.status} model_sec=${Math.round(r.duration_seconds || 0)} tokens=${r.usage?.total_tokens ?? "?"}`);
if (denied.length) console.log(`denied=${denied.join(",")}`);
if (r.error) console.log(`error=${JSON.stringify(r.error)}`);
if (!r.structured_output) { console.log("last.json=missing"); process.exit(5); }
fs.writeFileSync(lastPath, JSON.stringify(r.structured_output, null, 2));
if (r.status !== "SUCCESS" || denied.length) process.exit(6);
' "$OUT/result.json" "$OUT/last.json"
ncode=$?
[ $code -eq 0 ] && code=$ncode
echo "exit=$code sec=$(( $(date +%s) - start ))"
# worktree を参照して残ったプロセスの数 (0 であるはず)
powershell.exe -NoProfile -Command "(Get-CimInstance Win32_Process | Where-Object { @('agy.exe','dotnet.exe','MSBuild.exe','VBCSCompiler.exe','testhost.exe') -contains \$_.Name -and \$_.CommandLine -like '*$(basename "$W")*' }).Count" | tr -d '\r' | sed 's/^/leftover=/'
exit $code
