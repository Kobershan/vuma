#!/usr/bin/env bash
set -Eeuo pipefail

# One isolated codex exec process is created per task. The filesystem and git are the only handoff between workers.
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO=$(cd -- "$SCRIPT_DIR/../.." && pwd)
readonly REPO
readonly CURRENT="$REPO/docs/CURRENT.md"
readonly WORKER_GUIDE="$REPO/docs/automation/CODEX-WORKER.md"
readonly LOG_DIR="$REPO/docs/automation/logs"
readonly ACTIVE_MARKER="$LOG_DIR/.active-task"
readonly TIMEOUT_SECONDS="${VUMA_WORKER_TIMEOUT_SECONDS:-3600}"
MODE=continuous

die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
timestamp() { date -u '+%Y-%m-%dT%H:%M:%SZ'; }
log() { printf '[%s] %s\n' "$(timestamp)" "$*"; }

[[ "$TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] || die "VUMA_WORKER_TIMEOUT_SECONDS must be a positive integer"
command -v codex >/dev/null 2>&1 || die "codex is not installed or not on PATH"
command -v timeout >/dev/null 2>&1 || die "timeout is not installed or not on PATH"

case "${1:-}" in
  --dry-run) MODE=dry-run ;;
  --once) MODE=once ;;
  "") ;;
  *) die "usage: $0 [--dry-run|--once]" ;;
esac
[[ -f "$CURRENT" && -f "$WORKER_GUIDE" ]] || die "automation context is incomplete"
git -C "$REPO" rev-parse --is-inside-work-tree >/dev/null || die "not a git repository"
[[ "$(git -C "$REPO" rev-parse --show-toplevel)" == "$REPO" ]] || die "unexpected repository root"
[[ "$(git -C "$REPO" branch --show-current)" == main ]] || die "expected branch main"
git -C "$REPO" rev-parse --verify '@{u}' >/dev/null 2>&1 || die "main has no upstream"

mkdir -p "$LOG_DIR"
LOCK="$LOG_DIR/.run.lock"
if ! mkdir "$LOCK" 2>/dev/null; then die "another supervisor is already running"; fi
trap 'rmdir "$LOCK" 2>/dev/null || true' EXIT

stage=$(sed -nE 's/^CURRENT STAGE: Stage ([0-9]+[[:alpha:]]*).*/\1/p' "$CURRENT" | head -1)
[[ -n "$stage" ]] || die "CURRENT.md has no numerical CURRENT STAGE"
stage_index=$(find "$REPO/docs/tasks" -maxdepth 1 -type f -iname "STAGE-${stage}-INDEX.md" -print -quit)
[[ -n "$stage_index" ]] || die "no canonical task index for Stage $stage"

declare -a task_ids task_paths task_deps task_statuses
while IFS='|' read -r _ id _ _ deps _ _; do
  id=$(sed -E 's/.*\[([^]]+)\].*/\1/' <<<"$id" | xargs)
  [[ "$id" =~ ^TASK-[0-9A-Za-z-]+$ ]] || continue
  deps=$(xargs <<<"$deps")
  path=$(find "$REPO/docs/tasks" -maxdepth 1 -type f -iname "${id}-*.md" -print -quit)
  [[ -n "$path" ]] || die "canonical task $id has no task file"
  status=$(awk '/^## Status$/{found=1; next} found && NF {print; exit}' "$path" | tr -d '[:space:]')
  [[ "$status" =~ ^(NOT_STARTED|READY|IN_PROGRESS|BLOCKED|COMPLETE|NEEDS_VERIFICATION)$ ]] || die "$id has invalid status: $status"
  task_ids+=("$id"); task_paths+=("$path"); task_deps+=("$deps"); task_statuses+=("$status")
done < <(awk '/^\|/ && /TASK-/ {print}' "$stage_index")
[[ "${#task_ids[@]}" -gt 0 ]] || die "no canonical tasks found in $stage_index"

dependency_complete() {
  local raw=${1//…/..} dep idx found
  [[ -z "$raw" || "$raw" == "—" || "$raw" == "-" ]] && return 0
  raw=${raw//,/ }
  for dep in $raw; do
    dep=$(sed -E 's/[^0-9A-Za-z-]//g' <<<"$dep")
    [[ -z "$dep" ]] && continue
    if [[ "$dep" =~ ^([0-9]+[A-Za-z]*)-([0-9]+)..([0-9]+[A-Za-z]*)-([0-9]+)$ ]]; then
      local prefix=${BASH_REMATCH[1]} first=${BASH_REMATCH[2]} last=${BASH_REMATCH[5]} number
      for ((number=10#$first; number<=10#$last; number++)); do
        dependency_complete "$prefix-$(printf '%02d' "$number")" || return 1
      done
      continue
    fi
    [[ "$dep" == TASK-* ]] || dep="TASK-$dep"
    found=0
    for idx in "${!task_ids[@]}"; do
      if [[ "${task_ids[$idx]}" == "$dep" ]]; then
        found=1
        [[ "${task_statuses[$idx]}" == COMPLETE ]] || return 1
      fi
    done
    [[ "$found" == 1 ]] || return 1
  done
}

selected=-1
active_task=""
if [[ -f "$ACTIVE_MARKER" ]]; then
  active_task=$(sed -nE 's/^task=([^[:space:]]+).*/\1/p' "$ACTIVE_MARKER" | head -1)
  [[ "$active_task" =~ ^TASK-[0-9A-Za-z-]+$ ]] || die "invalid active-task marker"
  for i in "${!task_ids[@]}"; do
    if [[ "${task_ids[$i]}" == "$active_task" ]]; then selected=$i; break; fi
  done
  (( selected >= 0 )) || die "active task $active_task is not present in $stage_index"
  if [[ "${task_statuses[$selected]}" == COMPLETE ]]; then
    [[ -z "$(git -C "$REPO" status --porcelain)" ]] || die "completed active task has uncommitted changes"
    rm -f "$ACTIVE_MARKER"
    active_task=""
    selected=-1
  else
    [[ -n "$(git -C "$REPO" status --porcelain)" ]] || die "active task marker exists but the worktree is clean"
  fi
fi
if (( selected < 0 )); then
  [[ -z "$(git -C "$REPO" status --porcelain)" ]] || die "worktree is not clean before launch"
  for i in "${!task_ids[@]}"; do
    if [[ "${task_statuses[$i]}" == READY || "${task_statuses[$i]}" == NOT_STARTED ]] && dependency_complete "${task_deps[$i]}"; then selected=$i; break; fi
  done
fi
if (( selected < 0 )); then
  incomplete=0
  for status in "${task_statuses[@]}"; do [[ "$status" != COMPLETE ]] && incomplete=1; done
  if (( incomplete == 0 )); then log "Stage $stage tasks are complete; stage closure must be verified by its closure task."; exit 0; fi
  die "no eligible READY task in Stage $stage (inspect dependencies/statuses)"
fi

task="${task_ids[$selected]}"; task_path="${task_paths[$selected]}"; deps="${task_deps[$selected]}"

codex_config_dir=${CODEX_HOME:-$HOME/.codex}
model_setting=$(sed -nE 's/^model[[:space:]]*=[[:space:]]*//p' "$codex_config_dir/config.toml" 2>/dev/null | head -1 || true)
prompt=$(cat <<EOF
Read $WORKER_GUIDE and follow it exactly.

Assigned exactly one task: $task
Task document: $task_path
Stage: Stage $stage
Dependencies from the canonical index: ${deps:-none}

Do not select or start another task. Work only in $REPO. Complete, document, commit,
and push this task, then exit. The supervisor will verify your result.
EOF
)

printf 'Current stage: Stage %s\nCurrent task: %s\nDependencies: %s\nNext task: %s\n' "$stage" "$task" "${deps:-none}" "$task"
printf 'Worker configuration: approval_policy=never; sandbox=workspace-write; network_access=true; model=%s\n' "${model_setting:-Codex configured default (not overridden)}"
printf 'Worker command: codex --ask-for-approval never --sandbox workspace-write exec --ephemeral --cd %q -c sandbox_workspace_write.network_access=true (prompt via stdin)\n' "$REPO"
[[ "$MODE" == dry-run ]] && exit 0

before=$(git -C "$REPO" rev-parse HEAD)
log_file="$LOG_DIR/$(date -u '+%Y%m%dT%H%M%SZ')-${task}.log"
printf 'task=%s\nstage=%s\nstarted=%s\nlog=%s\n' "$task" "$stage" "$(timestamp)" "$log_file" > "$ACTIVE_MARKER"
log "Launching fresh worker for $task (timeout ${TIMEOUT_SECONDS}s)"
set +e
printf '%s\n' "$prompt" | timeout --signal=TERM --kill-after=30 "$TIMEOUT_SECONDS" codex --ask-for-approval never --sandbox workspace-write exec --ephemeral --cd "$REPO" -c 'sandbox_workspace_write.network_access=true' >"$log_file" 2>&1
exit_code=$?
set -e
status_after=$(awk '/^## Status$/{found=1; next} found && NF {print; exit}' "$task_path" | tr -d '[:space:]')
head_after=$(git -C "$REPO" rev-parse HEAD)
push_result=FAIL
if [[ "$head_after" != "$before" ]] && [[ "$(git -C "$REPO" rev-parse HEAD)" == "$(git -C "$REPO" rev-parse '@{u}')" ]]; then push_result=PASS; fi
completion_result=FAIL
[[ "$exit_code" == 0 && "$status_after" == COMPLETE && "$head_after" != "$before" && "$push_result" == PASS && -z "$(git -C "$REPO" status --porcelain)" ]] && completion_result=PASS
{
  printf 'timestamp=%s\nstage=%s\ntask=%s\nworker_process=codex exec (fresh, ephemeral)\nexit_code=%s\ncommit=%s\npush_result=%s\ntask_status=%s\ncompletion_result=%s\n' "$(timestamp)" "$stage" "$task" "$exit_code" "$head_after" "$push_result" "$status_after" "$completion_result"
  [[ "$exit_code" == 124 ]] && echo 'failure_reason=worker timeout'
  [[ "$exit_code" != 0 ]] && echo "failure_reason=worker exited non-zero; see $log_file"
  [[ "$status_after" != COMPLETE ]] && echo "failure_reason=task status is $status_after"
  [[ -n "$(git -C "$REPO" status --porcelain)" ]] && echo 'failure_reason=worktree is not clean'
} >> "$LOG_DIR/runs.log"
[[ "$completion_result" == PASS ]] || die "worker failed verification for $task; see $log_file"
rm -f "$ACTIVE_MARKER"
log "Verified $task commit $head_after pushed and worktree clean"
[[ "$MODE" == once ]] && exit 0
exec "$0"
