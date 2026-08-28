#!/usr/bin/env bash
set -Eeuo pipefail

# Vuma autonomous supervisor. One fresh, non-interactive Codex process per task.
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO=$(cd -- "$SCRIPT_DIR/../.." && pwd)
CURRENT="$REPO/docs/CURRENT.md"
WORKER_GUIDE="$REPO/docs/automation/CODEX-WORKER.md"
ARCH_INDEX="$REPO/docs/ARCHITECTURE-INDEX.md"
TASK_ROOT="$REPO/docs/tasks"
ROADMAP="$REPO/docs/ROADMAP.md"
LOG_DIR="$REPO/docs/automation/logs"
ACTIVE_MARKER="$LOG_DIR/.active-task"
RUNS_FILE="$LOG_DIR/runs.log"
MAX_RETRIES_PER_TASK="${MAX_RETRIES_PER_TASK:-3}"
TIMEOUT_SECONDS="${VUMA_WORKER_TIMEOUT_SECONDS:-3600}"
MODE=continuous

die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
timestamp() { date -u '+%Y-%m-%dT%H:%M:%SZ'; }
log() { printf '[%s] %s\n' "$(timestamp)" "$*"; }
task_status() { awk '/^## Status$/{found=1; next} found && NF {print; exit}' "$1" | tr -d '[:space:]'; }
marker_value() { sed -nE "s/^$1=(.*)$/\1/p" "$ACTIVE_MARKER" | head -1; }

case "${1:-}" in
  --dry-run) MODE=dry-run ;; --once) MODE=once ;; --self-test) MODE=self-test ;;
  "") ;; *) die "usage: $0 [--dry-run|--once|--self-test]" ;;
esac
[[ "$MAX_RETRIES_PER_TASK" =~ ^[1-9][0-9]*$ ]] || die "MAX_RETRIES_PER_TASK must be a positive integer"
[[ "$TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] || die "VUMA_WORKER_TIMEOUT_SECONDS must be a positive integer"
command -v codex >/dev/null 2>&1 || die "codex is not installed or not on PATH"
command -v timeout >/dev/null 2>&1 || die "timeout is not installed or not on PATH"
[[ -f "$CURRENT" && -f "$WORKER_GUIDE" && -f "$ARCH_INDEX" ]] || die "automation context is incomplete"
git -C "$REPO" rev-parse --is-inside-work-tree >/dev/null || die "not a git repository"
[[ "$(git -C "$REPO" rev-parse --show-toplevel)" == "$REPO" ]] || die "unexpected repository root"
[[ "$(git -C "$REPO" branch --show-current)" == main ]] || die "expected branch main"
git -C "$REPO" rev-parse --verify '@{u}' >/dev/null 2>&1 || die "main has no upstream"

# Verify the installed CLI, rather than inventing flags.
CODEX_HELP=$(codex --help 2>&1) || die "unable to inspect codex --help"
EXEC_HELP=$(codex exec --help 2>&1) || die "unable to inspect codex exec --help"
for flag in --ask-for-approval --dangerously-bypass-approvals-and-sandbox --dangerously-bypass-hook-trust; do
  grep -q -- "$flag" <<<"$CODEX_HELP" || die "installed Codex lacks $flag; cannot establish unattended operation"
done
grep -q -- '--ephemeral' <<<"$EXEC_HELP" || die "installed Codex lacks --ephemeral; cannot establish fresh worker sessions"

mkdir -p "$LOG_DIR"
LOCK="$LOG_DIR/.run.lock"
if ! mkdir "$LOCK" 2>/dev/null; then die "another supervisor is already running"; fi
trap 'rmdir "$LOCK" 2>/dev/null || true' EXIT

stage_sort_key() {
  local s=${1^^} n suffix
  n=$(sed -E 's/^([0-9]+).*/\1/' <<<"$s")
  suffix=${s#${s%%[[:alpha:]]*}}; [[ "$suffix" == "$s" ]] && suffix=""
  printf '%05d-%s' "$((10#$n))" "$suffix"
}

# The roadmap defines order; canonical indexes define executable work. Legacy
# TASK-NNN files are deliberately never scanned for selection.
canonical_stage_ids() {
  awk -F'|' '/^\|[[:space:]]*[0-9]+[A-Za-z]*[[:space:]]*\|/ {gsub(/[[:space:]]/,"",$2); print $2}' "$ROADMAP" |
    while read -r s; do
      [[ -f "$TASK_ROOT/STAGE-${s}-INDEX.md" ]] && printf '%s\n' "$s"
    done
}
verify_stage_exit() {
  local stage=$1 doc section
  doc=$(find "$REPO/docs/stages" -maxdepth 1 -type f -iname "STAGE-${stage}-*.md" -print -quit)
  [[ -n "$doc" ]] || die "stage $stage has no authoritative stage document"
  section=$(sed -n '/^## Exit checklist$/,/^## /p' "$doc")
  [[ -n "$section" ]] || die "stage $stage has no Exit checklist"
  ! grep -qE '^-[[:space:]]*\[[[:space:]]\]' <<<"$section" || die "stage $stage tasks are complete but its Exit checklist is not complete"
}
select_stage() {
  local s idx id p status incomplete
  while read -r s; do
    idx=$(find "$TASK_ROOT" -maxdepth 1 -type f -iname "STAGE-${s}-INDEX.md" -print -quit)
    incomplete=0
    while IFS='|' read -r _ id _ _ _ _; do
      id=$(sed -E 's/.*\[([^]]+)\].*/\1/' <<<"$id" | xargs); [[ "$id" =~ ^TASK-[0-9A-Za-z-]+$ ]] || continue
      p=$(find "$TASK_ROOT" -maxdepth 1 -type f -iname "${id}-*.md" -print -quit); [[ -n "$p" ]] || die "canonical task $id has no task file"
      status=$(task_status "$p"); [[ "$status" == COMPLETE ]] || incomplete=1
    done < <(awk '/^\|/ && /TASK-/ {print}' "$idx")
    if (( incomplete == 1 )); then printf '%s\t%s\n' "$s" "$idx"; return; fi
    verify_stage_exit "$s"
    log "Stage $s is complete and its exit checklist is satisfied; considering next roadmap stage"
  done < <(canonical_stage_ids)
}
load_tasks() {
  local index=$1 id deps path status
  task_ids=(); task_paths=(); task_deps=(); task_statuses=()
  while IFS='|' read -r _ id _ _ deps _ _; do
    id=$(sed -E 's/.*\[([^]]+)\].*/\1/' <<<"$id" | xargs); [[ "$id" =~ ^TASK-[0-9A-Za-z-]+$ ]] || continue
    path=$(find "$TASK_ROOT" -maxdepth 1 -type f -iname "${id}-*.md" -print -quit); [[ -n "$path" ]] || die "canonical task $id has no task file"
    status=$(task_status "$path"); [[ "$status" =~ ^(NOT_STARTED|READY|IN_PROGRESS|BLOCKED|COMPLETE|NEEDS_VERIFICATION)$ ]] || die "$id has invalid status: $status"
    task_ids+=("$id"); task_paths+=("$path"); task_deps+=("$(xargs <<<"$deps")"); task_statuses+=("$status")
  done < <(awk '/^\|/ && /TASK-/ {print}' "$index")
  ((${#task_ids[@]} > 0)) || die "no canonical tasks found in $index"
}
dependency_complete() {
  local raw=${1//…/..} dep i found
  [[ -z "$raw" || "$raw" == "—" || "$raw" == "-" ]] && return 0
  raw=${raw//,/ }
  for dep in $raw; do
    dep=$(sed -E 's/[^0-9A-Za-z-]//g' <<<"$dep"); [[ -z "$dep" ]] && continue
    if [[ "$dep" =~ ^([0-9A-Za-z]+)-([0-9]+)\.\.([0-9A-Za-z]+)-([0-9]+)$ ]]; then
      local prefix=${BASH_REMATCH[1]} first=${BASH_REMATCH[2]} last=${BASH_REMATCH[4]} n
      for ((n=10#$first; n<=10#$last; n++)); do dependency_complete "$prefix-$(printf '%02d' "$n")" || return 1; done
      continue
    fi
    [[ "$dep" == TASK-* ]] || dep="TASK-$dep"; found=0
    for i in "${!task_ids[@]}"; do [[ "${task_ids[$i]}" == "$dep" ]] && { found=1; [[ "${task_statuses[$i]}" == COMPLETE ]] || return 1; }; done
    [[ "$found" == 1 ]] || return 1
  done
}
worker_running() {
  local pid=${1:-}; [[ "$pid" =~ ^[1-9][0-9]*$ ]] || return 1
  kill -0 "$pid" 2>/dev/null || return 1
  [[ -r "/proc/$pid/cmdline" ]] && tr '\0' ' ' <"/proc/$pid/cmdline" | grep -q 'codex.*exec'
}
resolve_active() {
  local active=$1 pid p state=ABANDONED history marker_log
  pid=$(marker_value pid)
  if worker_running "$pid"; then log "Worker for $active is still running (pid $pid)"; return 0; fi
  p=$(find "$TASK_ROOT" -maxdepth 1 -type f -iname "${active}-*.md" -print -quit); [[ -n "$p" ]] || die "active marker names unknown canonical task $active"
  history=$(git -C "$REPO" log -1 --format='%h %s' -- "$p" || true)
  marker_log=$(marker_value log)
  [[ -n "$history" ]] && log "Last task-file commit for $active: $history"
  [[ -n "$marker_log" && -f "$marker_log" ]] && log "Found automation log for $active: $marker_log"
  [[ "$(task_status "$p")" == COMPLETE ]] && state=COMPLETED
  log "Recovered active marker task=$active state=$state (no worker running)"
  if [[ "$state" == COMPLETED && -z "$(git -C "$REPO" status --porcelain)" ]]; then rm -f "$ACTIVE_MARKER"; return 1; fi
  # Clean is valid after crash/timeout; classify and retry instead of dying.
  return 0
}
run_self_test() {
  local out="$LOG_DIR/self-test-$(date -u +%Y%m%dT%H%M%SZ).log" next="none"
  (( selected + 1 < ${#task_ids[@]} )) && next=${task_ids[$((selected + 1))]}
  { echo START; echo "select numerical stage: $stage"; echo "select canonical task: $task"; echo 'launch exactly one fresh worker: mocked codex exec'; echo 'worker terminates'; echo 'verify result: COMPLETE, pushed, clean'; echo 'clear active marker'; echo "select next task: $next"; echo PASS; } | tee "$out"
}
mark_blocked() {
  local task=$1 reason=$2 path=$3
  [[ -z "$(git -C "$REPO" status --porcelain)" ]] || die "cannot mark $task BLOCKED while worker left uncommitted changes; preserving worktree for recovery"
  sed -i '/^## Status$/{n;s/.*/BLOCKED/;}' "$path"
  printf '\n- BLOCKED after %s attempts: %s\n' "$MAX_RETRIES_PER_TASK" "$reason" >>"$path"
  git -C "$REPO" add -- "$path"
  git -C "$REPO" commit -m "chore(automation): block $task after retry limit"
  git -C "$REPO" push
}

selection=$(select_stage) || true
[[ -n "$selection" ]] || { log "No executable incomplete stages remain"; exit 0; }
stage=${selection%%$'\t'*}; stage_index=${selection#*$'\t'}; load_tasks "$stage_index"
selected=-1
if [[ -f "$ACTIVE_MARKER" ]]; then
  active_task=$(marker_value task); [[ "$active_task" =~ ^TASK-[0-9A-Za-z-]+$ ]] || die "invalid active-task marker"
  for i in "${!task_ids[@]}"; do [[ "${task_ids[$i]}" == "$active_task" ]] && selected=$i; done
  (( selected >= 0 )) || die "active task $active_task is not present in $stage_index"
  resolve_active "$active_task" || selected=-1
fi
if (( selected < 0 )); then
  [[ -z "$(git -C "$REPO" status --porcelain)" ]] || die "worktree is not clean before launch"
  for i in "${!task_ids[@]}"; do
    if [[ "${task_statuses[$i]}" == READY || "${task_statuses[$i]}" == NOT_STARTED || "${task_statuses[$i]}" == NEEDS_VERIFICATION || "${task_statuses[$i]}" == IN_PROGRESS ]] && dependency_complete "${task_deps[$i]}"; then selected=$i; break; fi
  done
fi
(( selected >= 0 )) || die "no eligible canonical task in Stage $stage"
task=${task_ids[$selected]}; task_path=${task_paths[$selected]}; task_rel=${task_path#"$REPO"/}; deps=${task_deps[$selected]}
printf 'Current stage: Stage %s\nCurrent task: %s\nTask file: %s\nDependencies: %s\n' "$stage" "$task" "$task_path" "${deps:-none}"
printf 'Worker configuration: approval_policy=never; full non-interactive access; network enabled\n'
printf 'Worker command: codex --ask-for-approval never --dangerously-bypass-approvals-and-sandbox --dangerously-bypass-hook-trust exec --ephemeral -C %q (prompt via stdin)\n' "$REPO"
[[ "$MODE" == dry-run ]] && exit 0
[[ "$MODE" == self-test ]] && { run_self_test; exit 0; }
[[ -z "$(git -C "$REPO" status --porcelain)" ]] || die "worktree is not clean before launch"
before=$(git -C "$REPO" rev-parse HEAD); log_file="$LOG_DIR/$(date -u '+%Y%m%dT%H%M%SZ')-${task}.log"
printf 'task=%s\nstage=%s\npid=unknown\nstarted=%s\nlog=%s\n' "$task" "$stage" "$(timestamp)" "$log_file" >"$ACTIVE_MARKER"
prompt="Read $WORKER_GUIDE and follow it exactly. Work only on $task. Task file: $task_path. Stage: Stage $stage. Dependencies: ${deps:-none}. Do not select another task. Inspect, plan, implement, test, review, update task and docs/CURRENT.md, commit, push, and terminate."
log "Launching fresh worker for $task (timeout ${TIMEOUT_SECONDS}s)"; set +e
timeout --signal=TERM --kill-after=30 "$TIMEOUT_SECONDS" codex --ask-for-approval never --dangerously-bypass-approvals-and-sandbox --dangerously-bypass-hook-trust exec --ephemeral -C "$REPO" < <(printf '%s\n' "$prompt") >"$log_file" 2>&1 &
worker_pid=$!; sed -i "s/^pid=.*/pid=$worker_pid/" "$ACTIVE_MARKER"
wait "$worker_pid"; exit_code=$?; set -e
status_after=$(task_status "$task_path"); head_after=$(git -C "$REPO" rev-parse HEAD); clean=0; [[ -z "$(git -C "$REPO" status --porcelain)" ]] && clean=1
commit_belongs=0; if [[ "$head_after" != "$before" ]]; then git -C "$REPO" show --format=%B --name-only "$head_after" | grep -Fq "$task_rel" && commit_belongs=1; fi
push_result=FAIL; [[ "$head_after" == "$(git -C "$REPO" rev-parse '@{u}')" ]] && push_result=PASS
completion=FAIL; [[ "$exit_code" == 0 && "$status_after" == COMPLETE && "$head_after" != "$before" && "$commit_belongs" == 1 && "$push_result" == PASS && "$clean" == 1 ]] && completion=PASS
printf 'timestamp=%s\ttask=%s\texit=%s\tstatus=%s\tcommit=%s\tpush=%s\tclean=%s\tcompletion=%s\tlog=%s\n' "$(timestamp)" "$task" "$exit_code" "$status_after" "$commit_belongs" "$push_result" "$clean" "$completion" "$log_file" >>"$RUNS_FILE"
if [[ "$completion" != PASS ]]; then
  attempts=$(awk -F'\t' -v t="$task" '$2 == t {n++} END {print n+0}' "$RUNS_FILE")
  reason="worker exit=$exit_code status=$status_after commit=$commit_belongs push=$push_result clean=$clean; see $log_file"
  if (( attempts >= MAX_RETRIES_PER_TASK )); then
    mark_blocked "$task" "$reason" "$task_path"
    die "BLOCKED $task after $attempts attempts: $reason"
  fi
  die "worker verification failed for $task (attempt $attempts/$MAX_RETRIES_PER_TASK): $reason"
fi
rm -f "$ACTIVE_MARKER"; log "Verified $task commit $head_after pushed and worktree clean"
[[ "$MODE" == once ]] && exit 0
exec "$0"
