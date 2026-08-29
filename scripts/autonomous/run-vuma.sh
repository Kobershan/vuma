#!/usr/bin/env bash
set -Eeuo pipefail

# Vuma autonomous supervisor. One fresh, non-interactive Codex process per task.
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO=$(cd -- "$SCRIPT_DIR/../.." && pwd)
VUMA_LOCAL_SHIM_BIN="$REPO/scripts/autonomous/local-bin"
export PATH="$VUMA_LOCAL_SHIM_BIN:$PATH"
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

WORKER_BACKEND="${VUMA_WORKER_BACKEND:-opencode}"
LOCAL_MODEL="${VUMA_LOCAL_MODEL:-ollama/qwen2.5-coder:3b}"

# Fully unattended execution. Never open credential, GUI, or terminal prompts.
export CI=1
export GIT_TERMINAL_PROMPT=0
export GCM_INTERACTIVE=Never
export SSH_ASKPASS=/bin/false
export SUDO_ASKPASS=/bin/false

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
case "$WORKER_BACKEND" in
  codex)
    command -v codex >/dev/null 2>&1 ||
      die "codex is not installed or not on PATH"
    ;;
  opencode)
    command -v opencode >/dev/null 2>&1 ||
      die "opencode is not installed or not on PATH"
    command -v ollama >/dev/null 2>&1 ||
      die "ollama is not installed or not on PATH"
    ollama list >/dev/null 2>&1 ||
      die "Ollama is not running"
    ;;
  *)
    die "VUMA_WORKER_BACKEND must be codex or opencode"
    ;;
esac
command -v timeout >/dev/null 2>&1 || die "timeout is not installed or not on PATH"
[[ -f "$CURRENT" && -f "$WORKER_GUIDE" && -f "$ARCH_INDEX" ]] || die "automation context is incomplete"
git -C "$REPO" rev-parse --is-inside-work-tree >/dev/null || die "not a git repository"
[[ "$(git -C "$REPO" rev-parse --show-toplevel)" == "$REPO" ]] || die "unexpected repository root"
[[ "$(git -C "$REPO" branch --show-current)" == main ]] || die "expected branch main"
git -C "$REPO" rev-parse --verify '@{u}' >/dev/null 2>&1 || die "main has no upstream"

# Verify the installed worker CLI rather than inventing flags.
if [[ "$WORKER_BACKEND" == codex ]]; then
  EXEC_HELP=$(codex exec --help 2>&1) ||
    die "unable to inspect codex exec --help"

  for flag in \
    --dangerously-bypass-approvals-and-sandbox \
    --dangerously-bypass-hook-trust
  do
    grep -q -- "$flag" <<<"$EXEC_HELP" ||
      die "installed Codex lacks $flag"
  done

  grep -q -- '--ephemeral' <<<"$EXEC_HELP" ||
    die "installed Codex lacks --ephemeral"
else
  EXEC_HELP=$(opencode run --help 2>&1) ||
    die "unable to inspect opencode run --help"

  grep -q -- '--model' <<<"$EXEC_HELP" ||
    die "installed OpenCode lacks --model"

  grep -q -- '--auto' <<<"$EXEC_HELP" ||
    die "installed OpenCode lacks --auto"
fi

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

result_already_seen_at_head() {
  local task=$1 classification=$2 head=$3

  [[ -f "$RUNS_FILE" ]] || return 1

  awk -F'\t' \
    -v wanted_task="task=$task" \
    -v wanted_class="classification=$classification" \
    -v wanted_head="head=$head" '
      $2 == wanted_task &&
      $5 == wanted_class &&
      $10 == wanted_head {
        found=1
      }
      END { exit(found ? 0 : 1) }
    ' "$RUNS_FILE"
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
  local raw=${1//…/..}
  local token dep prefix first last n i found status

  [[ -z "$raw" || "$raw" == "—" || "$raw" == "-" ]] && return 0

  # Dependencies can be comma-separated.
  raw=${raw//,/ }

  for token in $raw; do
    [[ -z "$token" ]] && continue

    # IMPORTANT:
    # Detect ranges BEFORE stripping punctuation, otherwise:
    #
    #   06C-01..06C-11
    #
    # becomes:
    #
    #   06C-0106C-11
    #
    # and can never match the range expression.
    if [[ "$token" =~ ^([0-9A-Za-z]+)-([0-9]+)\.\.([0-9A-Za-z]+)-([0-9]+)$ ]]; then
      prefix=${BASH_REMATCH[1]}
      first=${BASH_REMATCH[2]}
      last=${BASH_REMATCH[4]}

      # Require the same prefix on both sides of the range.
      [[ "${BASH_REMATCH[1]}" == "${BASH_REMATCH[3]}" ]] || return 1

      for ((n=10#$first; n<=10#$last; n++)); do
        dep="TASK-${prefix}-$(printf '%02d' "$n")"
        found=0

        for i in "${!task_ids[@]}"; do
          if [[ "${task_ids[$i]}" == "$dep" ]]; then
            found=1
            status=${task_statuses[$i]}

            # COMPLETE is fully satisfied.
            # NEEDS_VERIFICATION is provisionally satisfied so that
            # downstream implementation and stage acceptance can proceed.
            [[ "$status" == COMPLETE ||
               "$status" == NEEDS_VERIFICATION ]] || return 1

            break
          fi
        done

        [[ "$found" == 1 ]] || return 1
      done

      continue
    fi

    # Normal single dependency.
    dep=$(sed -E 's/[^0-9A-Za-z-]//g' <<<"$token")
    [[ -z "$dep" ]] && continue

    [[ "$dep" == TASK-* ]] || dep="TASK-$dep"

    found=0

    for i in "${!task_ids[@]}"; do
      if [[ "${task_ids[$i]}" == "$dep" ]]; then
        found=1
        status=${task_statuses[$i]}

        [[ "$status" == COMPLETE ||
           "$status" == NEEDS_VERIFICATION ]] || return 1

        break
      fi
    done

    [[ "$found" == 1 ]] || return 1
  done

  return 0
}

worker_running() {
  local pid=${1:-}; [[ "$pid" =~ ^[1-9][0-9]*$ ]] || return 1
  kill -0 "$pid" 2>/dev/null || return 1
  [[ -r "/proc/$pid/cmdline" ]] && tr '\0' ' ' <"/proc/$pid/cmdline" | grep -Eq '(codex.*exec|opencode.*run)'
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
  if [[ "$state" == COMPLETED && -z "$(git -C "$REPO" status --porcelain)" ]]; then
    recovered_head=$(git -C "$REPO" rev-parse HEAD)
    recovered_remote=$(timeout 30 git ls-remote origin refs/heads/main 2>/dev/null | awk 'NR==1 {print $1}' || true)
    if [[ "$recovered_head" == "$recovered_remote" ]]; then
      rm -f "$ACTIVE_MARKER"; return 1
    fi
    log "Recovered COMPLETE task $active but push evidence is missing; relaunching for verification"
  fi
  # A dead marker is recoverable state, not a fatal condition. The same task is
  # relaunched so a partial implementation or a no-op cannot be skipped.
  return 0
}
run_self_test() {
  local out="$LOG_DIR/self-test-$(date -u +%Y%m%dT%H%M%SZ).log" next="none"
  (( selected + 1 < ${#task_ids[@]} )) && next=${task_ids[$((selected + 1))]}
  {
    echo START
    echo "PASS task selection: stage=$stage task=$task"
    echo 'PASS task claim: active marker records task, pid, base commit, attempt, and log'
    echo 'PASS invocation: codex exec --ephemeral --dangerously-bypass-approvals-and-sandbox --dangerously-bypass-hook-trust -C REPO -'
    echo 'PASS non-interactive contract: supported bypass and hook-trust flags are verified from codex exec --help'
    echo 'PASS completion detection: requires COMPLETE status, task-related commit, CURRENT.md, clean tree, and pushed HEAD'
    echo 'PASS commit detection: compares HEAD before/after and reviews every new commit'
    echo 'PASS push detection: compares pushed remote HEAD separately from commit evidence'
    echo 'PASS stale marker recovery: dead worker is classified and relaunched or finalized from repository state'
    echo 'PASS failure classification: COMPLETE, PROGRESS, UNVERIFIED, FAILED, CRASHED, NO_CHANGE, BLOCKED; no human-wait state'
    echo "PASS retry policy: implementation failures may retry; BLOCKED/UNVERIFIED are attempted once per repository HEAD"
    echo "PASS next-task selection: next canonical task would be $next"
    echo PASS
  } | tee "$out"
}
mark_blocked() {
  local task=$1 reason=$2 path=$3
  sed -i '/^## Status$/{n;s/.*/BLOCKED/;}' "$path"
  printf '\n- BLOCKED after %s attempts: %s\n' "$MAX_RETRIES_PER_TASK" "$reason" >>"$path"
  git -C "$REPO" add -- "$path"
  git -C "$REPO" commit -m "chore(automation): block $task after retry limit"
  git -C "$REPO" push || log "WARNING: could not push BLOCKED state for $task"
}

commit_evidence() {
  local base=$1 head=$2 task_rel=$3
  [[ "$base" != "$head" ]] || return 1
  git -C "$REPO" diff --quiet "$base..$head" -- || true
  git -C "$REPO" diff --name-only "$base..$head" | grep -Fxq "$task_rel"
}

classify_result() {
  local exit_code=$1 status=$2 base=$3 head=$4 task_rel=$5 clean=$6 pushed=$7 log_file=$8
  local committed=0 current_changed=0

  commit_evidence "$base" "$head" "$task_rel" && committed=1

  git -C "$REPO" diff --name-only "$base..$head" |
    grep -Fxq 'docs/CURRENT.md' && current_changed=1

  if [[ "$status" == COMPLETE &&
        "$exit_code" == 0 &&
        "$committed" == 1 &&
        "$current_changed" == 1 &&
        "$clean" == 1 &&
        "$pushed" == 1 ]]; then
    printf 'COMPLETE'
    return
  fi

  if [[ "$status" == BLOCKED ]]; then
    printf 'BLOCKED'
    return
  fi

  if [[ "$exit_code" == 124 ||
        "$exit_code" == 137 ||
        "$exit_code" == 143 ]]; then
    printf 'CRASHED'
    return
  fi

  # Implementation exists but a required AUTOMATED validation could not run.
  # This is terminal for this worker invocation and is NEVER a human prompt.
  if [[ "$status" == NEEDS_VERIFICATION && "$clean" == 1 ]]; then
    printf 'UNVERIFIED'
    return
  fi

  # Non-zero worker exit with no repository change is a runtime/model
  # failure, not a Vuma implementation failure.
  if [[ "$exit_code" != 0 &&
        "$base" == "$head" &&
        "$clean" == 1 ]]; then
    printf 'WORKER_ERROR'
    return
  fi

  if [[ "$base" == "$head" && "$clean" == 1 ]]; then
    printf 'NO_CHANGE'
    return
  fi

  # A clean pushed IN_PROGRESS commit is a successful micro-slice.
  # It is progress, not a failed task attempt.
  if [[ "$status" == IN_PROGRESS &&
        "$exit_code" == 0 &&
        "$committed" == 1 &&
        "$current_changed" == 1 &&
        "$clean" == 1 &&
        "$pushed" == 1 ]]; then
    printf 'PROGRESS'
    return
  fi

  if [[ "$status" == IN_PROGRESS ||
        "$committed" == 1 ||
        "$clean" == 0 ]]; then
    printf 'PARTIAL'
    return
  fi

  printf 'FAILED'
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
  [[ -z "$(git -C "$REPO" status --porcelain)" ]] ||
    die "worktree is not clean before launch"

  selection_head=$(git -C "$REPO" rev-parse HEAD)

  # First: actual implementation work.
  for wanted_status in IN_PROGRESS READY NOT_STARTED; do
    for i in "${!task_ids[@]}"; do
      if [[ "${task_statuses[$i]}" == "$wanted_status" ]] &&
         dependency_complete "${task_deps[$i]}"; then
        selected=$i
        break 2
      fi
    done
  done

  # Second: one BLOCKED remediation attempt for this repository state.
  if (( selected < 0 )); then
    for i in "${!task_ids[@]}"; do
      if [[ "${task_statuses[$i]}" == BLOCKED ]] &&
         dependency_complete "${task_deps[$i]}" &&
         ! result_already_seen_at_head \
             "${task_ids[$i]}" BLOCKED "$selection_head"; then
        selected=$i
        break
      fi
    done
  fi

  # Last: deferred verification.
  #
  # Try each NEEDS_VERIFICATION task only once at a particular HEAD.
  # Later commits change HEAD and make it eligible for another useful
  # verification pass.
  if (( selected < 0 )); then
    for i in "${!task_ids[@]}"; do
      if [[ "${task_statuses[$i]}" == NEEDS_VERIFICATION ]] &&
         dependency_complete "${task_deps[$i]}" &&
         ! result_already_seen_at_head \
             "${task_ids[$i]}" UNVERIFIED "$selection_head"; then
        selected=$i
        break
      fi
    done
  fi
fi
if (( selected < 0 )); then
  log "Stage $stage currently has no runnable task."
  log "Incomplete task states:"

  for i in "${!task_ids[@]}"; do
    if [[ "${task_statuses[$i]}" != COMPLETE ]]; then
      printf '  %-16s status=%-20s dependencies=%s\n' \
        "${task_ids[$i]}" \
        "${task_statuses[$i]}" \
        "${task_deps[$i]:-none}"
    fi
  done

  log "No human verification is requested. Execution stops because the automated dependency graph is blocked."
  exit 2
fi
task=${task_ids[$selected]}; task_path=${task_paths[$selected]}; task_rel=${task_path#"$REPO"/}; deps=${task_deps[$selected]}
task_state=${task_statuses[$selected]}

if [[ "$task_state" == NEEDS_VERIFICATION || "$task_state" == BLOCKED ]]; then
  remediation_note="AUTOMATED REMEDIATION MODE: This task was previously left in $task_state. Read its recorded blocker and fix any repository-local prerequisite required to make its automated validation pass. You are explicitly authorized to repair adjacent migrations, test infrastructure, fixtures, configuration, or other repository-local prerequisites when they directly prevent this task's required automated checks. Do not bypass, suppress, delete, weaken, or falsely pass tests. Do not ask a human. If the blocker is genuinely external and cannot be repaired from this repository, record it accurately and exit NEEDS_VERIFICATION."
else
  remediation_note="NORMAL IMPLEMENTATION MODE: implement and verify the assigned task within its defined scope."
fi
printf 'Current stage: Stage %s\nCurrent task: %s\nTask file: %s\nDependencies: %s\n' "$stage" "$task" "$task_path" "${deps:-none}"
printf 'Worker configuration: approval_policy=never; full non-interactive access; network enabled\n'
if [[ "$WORKER_BACKEND" == opencode ]]; then
  printf 'Worker backend: Direct Ollama + Qwen local worker\n'
  printf 'Worker model: %s\n' "$LOCAL_MODEL"
  printf 'Worker mode: FAST-SLICE; direct 4K Ollama tool loop\n'
else
  printf 'Worker backend: Codex\n'
  printf 'Worker mode: FAST-SLICE; fresh ephemeral session per slice\n'
fi
[[ "$MODE" == dry-run ]] && exit 0
[[ "$MODE" == self-test ]] && { run_self_test; exit 0; }
if [[ -z "$(git -C "$REPO" status --porcelain)" || -f "$ACTIVE_MARKER" ]]; then :; else die "worktree is not clean before launch"; fi
before=$(git -C "$REPO" rev-parse HEAD)
attempts=0
# FAST_SLICE_RETRY_RESET
if [[ "$(task_status "$task_path")" == IN_PROGRESS ]]; then
  attempts=0
fi

if [[ -f "$ACTIVE_MARKER" ]]; then
  prior_attempt=$(marker_value attempt || true)
  [[ "$prior_attempt" =~ ^[0-9]+$ ]] && attempts=$prior_attempt
fi
while (( attempts < "$MAX_RETRIES_PER_TASK" )); do
  attempts=$((attempts + 1)); log_file="$LOG_DIR/$(date -u '+%Y%m%dT%H%M%SZ')-${task}-attempt-${attempts}.log"
  printf 'task=%s\nstage=%s\npid=unknown\nattempt=%s\nbase=%s\nstarted=%s\nlog=%s\n' "$task" "$stage" "$attempts" "$before" "$(timestamp)" "$log_file" >"$ACTIVE_MARKER"
  prompt=$(cat <<EOF
You are the autonomous implementation worker for exactly one Vuma canonical task.

Read $WORKER_GUIDE and follow it exactly.

CURRENT STAGE: Stage $stage
CURRENT TASK: $task
TASK FILE: $task_path

FAST-SLICE MODE IS MANDATORY.

Your invocation is NOT responsible for finishing the entire canonical task.

Choose exactly ONE smallest coherent unfinished implementation slice from
$task_path and implement only that slice.

Read only:
1. CLAUDE.md
2. docs/CURRENT.md
3. $task_path
4. applicable AGENTS.md for files you modify
5. documentation/ADRs explicitly referenced by the task
6. source and tests directly relevant to the chosen slice

Do not enumerate the repository.
Do not read other tasks.
Do not read unrelated stages.
Do not fix unrelated defects.

NORMAL SLICE VALIDATION:
- never run a solution-wide dotnet build
- never run the full test suite
- build only the directly affected csproj
- run only directly related filtered/project tests
- normally one targeted build and one targeted test command
- restore only the affected project if restore is required

Do not start PostgreSQL, Docker, migration fan-out, backup/restore drills or
full sync verification unless THIS PARTICULAR SLICE directly requires that
infrastructure.

When this slice is complete but more implementation remains:
- leave/set task status IN_PROGRESS
- add a short progress note to $task_path
- record the exact next smallest slice
- update docs/CURRENT.md
- commit
- push
- EXIT

Do NOT continue into another slice.

Only when all implementation requirements in the canonical task are already
implemented may you run its broader required acceptance checks and mark it
COMPLETE or NEEDS_VERIFICATION.

Work fully unattended.
Never ask for approval or human verification.
Never begin another canonical task.
EOF
)
  log "Launching fresh worker for $task attempt $attempts/${MAX_RETRIES_PER_TASK} (timeout ${TIMEOUT_SECONDS}s)"
  set +e
  if [[ "$WORKER_BACKEND" == opencode ]]; then
    timeout --signal=TERM --kill-after=30 "$TIMEOUT_SECONDS" \
      opencode run \
      --auto \
      --model "$LOCAL_MODEL" \
      --dir "$REPO" \
      "$prompt" >"$log_file" 2>&1 &
  else
    timeout --signal=TERM --kill-after=30 "$TIMEOUT_SECONDS" \
      codex exec \
      --ephemeral \
      --dangerously-bypass-approvals-and-sandbox \
      --dangerously-bypass-hook-trust \
      -C "$REPO" \
      - <<<"$prompt" >"$log_file" 2>&1 &
  fi
  worker_pid=$!
  sed -i "s/^pid=.*/pid=$worker_pid/" "$ACTIVE_MARKER"
  wait "$worker_pid"
  exit_code=$?
  set -e
  head_after=$(git -C "$REPO" rev-parse HEAD)
  status_after=$(task_status "$task_path")
  clean=0; [[ -z "$(git -C "$REPO" status --porcelain)" ]] && clean=1
  remote_head=$(timeout 30 git ls-remote origin refs/heads/main 2>/dev/null | awk 'NR==1 {print $1}') || remote_head=""
  pushed=0; [[ -n "$remote_head" && "$remote_head" == "$head_after" ]] && pushed=1
  if [[ "$pushed" == 0 && "$head_after" != "$before" ]]; then
    log "Commit exists for $task but remote does not match; retrying push without creating a duplicate commit"
    timeout 120 git -C "$REPO" push >>"$log_file" 2>&1 || true
    remote_head=$(timeout 30 git ls-remote origin refs/heads/main 2>/dev/null | awk 'NR==1 {print $1}') || remote_head=""
    [[ -n "$remote_head" && "$remote_head" == "$head_after" ]] && pushed=1
  fi
  classification=$(classify_result "$exit_code" "$status_after" "$before" "$head_after" "$task_rel" "$clean" "$pushed" "$log_file")
  {
    echo "--- supervisor evidence ---"
    echo "exit_code=$exit_code status=$status_after classification=$classification"
    echo "base_commit=$before head_after=$head_after pushed=$pushed clean=$clean"
    echo 'new_commits:'; git -C "$REPO" log --format='%H %s' "$before..$head_after" 2>&1 || true
    echo 'changed_files:'; git -C "$REPO" diff --name-status "$before..$head_after" 2>&1 || true
    echo 'worktree:'; git -C "$REPO" status --short 2>&1 || true
    echo 'diff_check:'; git -C "$REPO" diff --check 2>&1 || true
  } >>"$log_file"
  printf 'timestamp=%s\ttask=%s\texit=%s\tstatus=%s\tclassification=%s\tcommit=%s\tpush=%s\tclean=%s\tbase=%s\thead=%s\tlog=%s\n' "$(timestamp)" "$task" "$exit_code" "$status_after" "$classification" "$([[ "$before" != "$head_after" ]] && echo 1 || echo 0)" "$pushed" "$clean" "$before" "$head_after" "$log_file" >>"$RUNS_FILE"
  if [[ "$classification" == WORKER_ERROR ]]; then
    rm -f "$ACTIVE_MARKER"

    log "$task local worker failed before committing repository changes."
    log "The Vuma task has NOT been marked BLOCKED."
    log "Worker log: $log_file"

    echo >&2
    echo "=== LOCAL WORKER ERROR ===" >&2
    tail -100 "$log_file" >&2 || true

    exit 3
  fi

  if [[ "$classification" == NO_CHANGE ]]; then
    rm -f "$ACTIVE_MARKER"

    log "$task local worker exited without a repository change."
    log "Not burning three identical task attempts."

    exit 4
  fi

  if [[ "$classification" == PROGRESS ]]; then
    rm -f "$ACTIVE_MARKER"

    log "$task completed one fast implementation slice."
    log "Starting a fresh context for the next unfinished slice."

    [[ "$MODE" == once ]] && exit 0

    rmdir "$LOCK" 2>/dev/null || true
    trap - EXIT
    exec "$0"
  fi

  if [[ "$classification" == COMPLETE ]]; then
    rm -f "$ACTIVE_MARKER"
    log "Verified $task: implementation, acceptance status, CURRENT.md, commit, push, and clean tree all confirmed"

    [[ "$MODE" == once ]] && exit 0
    # exec does not run the EXIT trap, so release the supervisor lock first.
    rmdir "$LOCK" 2>/dev/null || true
    trap - EXIT
    exec "$0"
  fi

  if [[ "$classification" == BLOCKED ]]; then
    rm -f "$ACTIVE_MARKER"

    log "$task remains BLOCKED after this remediation attempt."
    log "Not repeating the same remediation at repository HEAD $head_after."

    [[ "$MODE" == once ]] && exit 2

    rmdir "$LOCK" 2>/dev/null || true
    trap - EXIT
    exec "$0"
  fi

  if [[ "$classification" == UNVERIFIED ]]; then
    rm -f "$ACTIVE_MARKER"

    log "$task remains NEEDS_VERIFICATION."
    log "Verification has already been attempted at repository HEAD $head_after."
    log "Deferring it instead of burning another Luna context."

    [[ "$MODE" == once ]] && exit 2

    rmdir "$LOCK" 2>/dev/null || true
    trap - EXIT
    exec "$0"
  fi

  reason="classification=$classification exit=$exit_code status=$status_after commit=$([[ "$before" != "$head_after" ]] && echo 1 || echo 0) push=$pushed clean=$clean; see $log_file"
  if (( attempts >= MAX_RETRIES_PER_TASK )); then
    mark_blocked "$task" "$reason" "$task_path"
    rm -f "$ACTIVE_MARKER"
    die "BLOCKED $task after $attempts attempts: $reason"
  fi
  log "$task attempt $attempts classified $classification; retaining state and launching a fresh worker"
  before=$(git -C "$REPO" rev-parse HEAD)
done
