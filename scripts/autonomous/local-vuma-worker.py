#!/usr/bin/env python3

import argparse
import datetime
import json
import os
import re
import shlex
import subprocess
import sys
import urllib.request
from pathlib import Path


OLLAMA_URL = "http://127.0.0.1:11434/api/chat"
MAX_TURNS = 24
MAX_TOOL_CALLS = 24
MAX_CHECKS = 4

LOCAL_MODE = os.environ.get(
    "VUMA_LOCAL_MODE",
    "task",
).strip().lower()

if LOCAL_MODE not in {"task", "slice"}:
    LOCAL_MODE = "task"

finished = False
tool_calls_used = 0
checks_used = 0
read_paths = set()
check_results = []

# The Python wrapper, NOT the small model, owns task decomposition.
slice_plan = []
current_slice_id = None
current_slice_text = ""

last_error_fingerprint = None
same_error_count = 0


def fail(msg, code=1):
    print(f"LOCAL_WORKER_ERROR: {msg}", file=sys.stderr)
    raise SystemExit(code)


parser = argparse.ArgumentParser()
parser.add_argument("--repo", required=True)
parser.add_argument("--model", required=True)
parser.add_argument("--prompt", required=True)
args = parser.parse_args()

repo = Path(args.repo).resolve()

if not (repo / ".git").exists():
    fail(f"not a git repository: {repo}")

model = args.model
if model.startswith("ollama/"):
    model = model[len("ollama/"):]

prompt = args.prompt

task_match = re.search(r"CURRENT TASK:\s*(TASK-[0-9A-Za-z-]+)", prompt)
path_match = re.search(r"TASK FILE:\s*(.+)", prompt)
stage_match = re.search(r"CURRENT STAGE:\s*Stage\s+([0-9A-Za-z]+)", prompt)

if not task_match or not path_match:
    fail("unable to determine task from supervisor prompt")

task_id = task_match.group(1)
stage = stage_match.group(1) if stage_match else "unknown"

task_path = Path(path_match.group(1).strip())
if not task_path.is_absolute():
    task_path = repo / task_path

task_path = task_path.resolve()

try:
    task_path.relative_to(repo)
except ValueError:
    fail("task file is outside repository")

current_path = repo / "docs/CURRENT.md"

if not task_path.exists():
    fail(f"task file does not exist: {task_path}")


def git(*parts, timeout=120, check=False):
    env = os.environ.copy()
    env["GIT_TERMINAL_PROMPT"] = "0"
    env["GCM_INTERACTIVE"] = "Never"

    result = subprocess.run(
        ["git", "-C", str(repo), *parts],
        text=True,
        capture_output=True,
        timeout=timeout,
        env=env,
    )

    if check and result.returncode != 0:
        raise RuntimeError(
            (result.stdout + "\n" + result.stderr).strip()
        )

    return result


# Local worker must start from a clean tree.
initial = git("status", "--porcelain")

if initial.stdout.strip():
    fail(
        "local worker requires a clean worktree:\n"
        + initial.stdout.strip()
    )


def safe_path(raw):
    p = Path(raw)

    if not p.is_absolute():
        p = repo / p

    p = p.resolve()

    try:
        p.relative_to(repo)
    except ValueError:
        raise ValueError("path outside repository")

    return p


def relative(p):
    return str(p.relative_to(repo))


def limit_text(text, size=7000):
    if len(text) <= size:
        return text

    half = size // 2
    return (
        text[:half]
        + "\n\n...[OUTPUT TRUNCATED]...\n\n"
        + text[-half:]
    )


def read_file(path, start_line=1, max_lines=120):
    p = safe_path(path)

    if p == task_path:
        return (
            "TASK FILE ALREADY PROVIDED IN THE INITIAL PROMPT. "
            "Do not reread it. Inspect implementation/tests for "
            "CURRENT MICRO-SLICE instead."
        )

    if p == current_path:
        return (
            "docs/CURRENT.md ALREADY PROVIDED IN THE INITIAL PROMPT. "
            "Do not reread it. Inspect implementation/tests instead."
        )

    if not p.exists() or not p.is_file():
        return f"ERROR: file not found: {relative(p)}"

    max_lines = min(max(int(max_lines), 1), 160)
    start_line = max(int(start_line), 1)

    try:
        lines = p.read_text(errors="replace").splitlines()
    except Exception as e:
        return f"ERROR: {e}"

    read_paths.add(p)

    selected = lines[start_line - 1:start_line - 1 + max_lines]

    output = []
    for n, line in enumerate(selected, start=start_line):
        output.append(f"{n}: {line}")

    return limit_text("\n".join(output), 7500)


def repository_text_files(root):
    allowed_suffixes = {
        ".cs",
        ".csproj",
        ".props",
        ".targets",
        ".json",
        ".md",
        ".yml",
        ".yaml",
        ".xml",
        ".sql",
        ".sh",
        ".py",
    }

    excluded_dirs = {
        ".git",
        "bin",
        "obj",
        "node_modules",
        ".idea",
        ".vs",
        "__pycache__",
        "logs",
    }

    if root.is_file():
        return [root]

    result = []

    for base, dirs, files in os.walk(root):
        dirs[:] = [
            d
            for d in dirs
            if d not in excluded_dirs
        ]

        base_path = Path(base)

        # Never search historical automation logs.
        try:
            rel = base_path.relative_to(repo)
            rel_text = str(rel)
        except ValueError:
            continue

        if rel_text.startswith("docs/automation/logs"):
            dirs[:] = []
            continue

        for name in files:
            fp = base_path / name

            if fp.suffix.lower() not in allowed_suffixes:
                continue

            try:
                if fp.stat().st_size > 400_000:
                    continue
            except OSError:
                continue

            result.append(fp)

    return result


SEARCH_STOPWORDS = {
    "the",
    "a",
    "an",
    "and",
    "or",
    "to",
    "of",
    "for",
    "in",
    "on",
    "with",
    "that",
    "this",
    "is",
    "are",
    "be",
    "been",
    "one",
    "only",
    "existing",
    "find",
    "test",
    "tests",
    "prove",
    "proving",
    "relevant",
    "narrow",
    "narrowest",
}


def search_terms(query):
    words = re.findall(
        r"[A-Za-z][A-Za-z0-9_-]{2,}",
        query.lower(),
    )

    terms = []

    for word in words:
        if word in SEARCH_STOPWORDS:
            continue

        if word not in terms:
            terms.append(word)

    return terms[:10]


def search(query, path=".", regex=False):
    p = safe_path(path)

    files = repository_text_files(p)

    if not files:
        return "NO SEARCHABLE FILES"

    matches = []

    if regex:
        try:
            pattern = re.compile(
                query,
                re.IGNORECASE,
            )
        except re.error as e:
            return f"ERROR: invalid regex: {e}"

        for fp in files:
            try:
                lines = fp.read_text(
                    errors="replace"
                ).splitlines()
            except Exception:
                continue

            for number, line in enumerate(lines, 1):
                if pattern.search(line):
                    try:
                        rel = relative(fp)
                    except Exception:
                        continue

                    matches.append(
                        (
                            1,
                            rel,
                            number,
                            line.strip(),
                        )
                    )

                    if len(matches) >= 60:
                        break

        if not matches:
            return "NO MATCHES"

    else:
        terms = search_terms(query)

        if not terms:
            return (
                "ERROR: search query contains no useful keywords"
            )

        for fp in files:
            try:
                rel = relative(fp)
                rel_lower = rel.lower()

                lines = fp.read_text(
                    errors="replace"
                ).splitlines()
            except Exception:
                continue

            filename_score = sum(
                3
                for term in terms
                if term in rel_lower
            )

            for number, line in enumerate(lines, 1):
                lower = line.lower()

                present = [
                    term
                    for term in terms
                    if term in lower
                ]

                if not present:
                    continue

                # Reward lines matching multiple concepts.
                score = (
                    filename_score
                    + len(present) * 4
                )

                # Tests are usually what an acceptance slice needs.
                if rel.startswith("tests/"):
                    score += 4

                if "test" in rel_lower:
                    score += 2

                matches.append(
                    (
                        score,
                        rel,
                        number,
                        line.strip(),
                    )
                )

        if not matches:
            return (
                "NO MATCHES for keywords: "
                + ", ".join(terms)
            )

        matches.sort(
            key=lambda x: (
                -x[0],
                x[1],
                x[2],
            )
        )

    output = []

    seen = set()

    for score, rel, number, line in matches:
        key = (rel, number)

        if key in seen:
            continue

        seen.add(key)

        output.append(
            f"{rel}:{number}: {line}"
        )

        if len(output) >= 35:
            break

    return limit_text(
        "\n".join(output),
        7000,
    )


def replace_text(path, old, new):
    p = safe_path(path)

    if p in {task_path, current_path}:
        return (
            "ERROR: task status/progress metadata is managed exclusively "
            "by finish(). Do not edit the task file or docs/CURRENT.md. "
            "Work on implementation/tests or run the acceptance check."
        )

    if not p.exists():
        return "ERROR: target file does not exist"

    if not old:
        return "ERROR: old text must not be empty"

    if len(new) > 20000:
        return "ERROR: replacement too large"

    text = p.read_text(errors="replace")
    count = text.count(old)

    if count == 0:
        return "ERROR: exact old text not found"

    if count > 1:
        return (
            f"ERROR: old text occurs {count} times; "
            "provide a more specific replacement"
        )

    p.write_text(text.replace(old, new, 1))

    return f"OK: replaced text in {relative(p)}"


def write_file(path, content):
    p = safe_path(path)

    if p in {task_path, current_path}:
        return (
            "ERROR: task status/progress metadata is managed exclusively "
            "by finish(). Do not write the task file or docs/CURRENT.md."
        )

    if len(content) > 22000:
        return "ERROR: file content too large for one micro-slice"

    if p.exists() and p not in read_paths:
        return (
            "ERROR: existing file must be read_file'd "
            "before it may be overwritten"
        )

    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content)

    return f"OK: wrote {relative(p)}"


def allowed_check(command):
    try:
        argv = shlex.split(command)
    except ValueError:
        return False, "invalid shell syntax"

    if not argv:
        return False, "empty command"

    # shell=False is used below, so chaining/redirection cannot execute.
    if argv[0] == "git":
        permitted = {
            ("git", "status"),
            ("git", "diff"),
        }

        if len(argv) >= 2 and (argv[0], argv[1]) in permitted:
            return True, argv

        return False, "only git status/diff are allowed"

    if argv[0] == "dotnet":
        if len(argv) < 2:
            return False, "missing dotnet action"

        action = argv[1]

        if action not in {"build", "test", "restore"}:
            return False, "only dotnet build/test/restore are allowed"

        # Every dotnet invocation must target a project, not the solution.
        if not any(x.endswith(".csproj") for x in argv):
            return False, "dotnet command must target one .csproj"

        if any(
            x.endswith(".sln") or x.endswith(".slnx")
            for x in argv
        ):
            return False, "solution-wide commands are forbidden"

        return True, argv

    if argv[0] in {
        "./scripts/pg-test.sh",
        "scripts/pg-test.sh",
    }:
        if len(argv) == 2 and argv[1] in {
            "start",
            "stop",
            "status",
        }:
            return True, argv

        return False, "pg-test allows only start/stop/status"

    return False, "command is not on the local-worker allowlist"


def run_check(command):
    global checks_used

    if checks_used >= MAX_CHECKS:
        return (
            "ERROR: micro-slice check budget exhausted. "
            "Finish this slice."
        )

    ok, parsed = allowed_check(command)

    if not ok:
        return f"ERROR: {parsed}"

    checks_used += 1

    try:
        result = subprocess.run(
            parsed,
            cwd=repo,
            text=True,
            capture_output=True,
            timeout=360,
            env={
                **os.environ,
                "CI": "1",
                "GIT_TERMINAL_PROMPT": "0",
                "GCM_INTERACTIVE": "Never",
            },
        )

        output = (
            f"exit_code={result.returncode}\n"
            + result.stdout
            + "\n"
            + result.stderr
        ).strip()

        check_results.append(
            {
                "command": command,
                "exit": result.returncode,
            }
        )

        return limit_text(output, 7500)

    except subprocess.TimeoutExpired:
        check_results.append(
            {
                "command": command,
                "exit": 124,
            }
        )
        return "exit_code=124\nERROR: command timed out"


def set_task_status(text, status):
    pattern = re.compile(
        r"(^## Status\s*\n+)([A-Z_]+)",
        re.MULTILINE,
    )

    if pattern.search(text):
        return pattern.sub(
            lambda m: m.group(1) + status,
            text,
            count=1,
        )

    return text + f"\n\n## Status\n{status}\n"


def update_current(summary, next_slice, status):
    start = "<!-- LOCAL_WORKER_STATE_START -->"
    end = "<!-- LOCAL_WORKER_STATE_END -->"

    summary = " ".join(summary.split())[:500]
    next_slice = " ".join(next_slice.split())[:500]

    block = f"""\
{start}
## Local worker state
- Task: {task_id}
- Stage: {stage}
- Status: {status}
- Last slice: {summary}
- Next slice: {next_slice or "none"}
{end}"""

    text = current_path.read_text(errors="replace")

    if start in text and end in text:
        text = re.sub(
            re.escape(start) + r".*?" + re.escape(end),
            block,
            text,
            flags=re.DOTALL,
        )
    else:
        text = text.rstrip() + "\n\n" + block + "\n"

    current_path.write_text(text)


def finish(status, summary, next_slice=""):
    global finished

    allowed = {
        "IN_PROGRESS",
        "COMPLETE",
        "NEEDS_VERIFICATION",
    }

    if status not in allowed:
        return (
            "ERROR: status must be IN_PROGRESS, COMPLETE, "
            "or NEEDS_VERIFICATION"
        )

    summary = summary.strip()
    next_slice = next_slice.strip()

    if not summary:
        return "ERROR: summary is required"

    # The model does not choose the next micro-slice.
    # The deterministic wrapper does.
    if status == "IN_PROGRESS":
        if LOCAL_MODE == "task":
            next_slice = (
                "Resume the same canonical task from its remaining "
                "unfinished requirements using a fresh context."
            )

        elif (
            current_slice_id is not None
            and current_slice_id < len(slice_plan)
        ):
            next_slice = (
                f"Slice {current_slice_id + 1}: "
                + slice_plan[current_slice_id]
            )

        else:
            next_slice = (
                "Reconcile remaining task evidence and requirements."
            )

    # COMPLETE is forbidden before the final planned slice.
    if (
        LOCAL_MODE == "slice"
        and status == "COMPLETE"
        and current_slice_id is not None
        and current_slice_id < len(slice_plan)
    ):
        return (
            "ERROR: remaining deterministic micro-slices exist. "
            "Use IN_PROGRESS for this completed slice."
        )

    if status == "COMPLETE" and not check_results:
        return (
            "ERROR: COMPLETE requires at least one automated "
            "check in this invocation"
        )

    # Do not allow the model to manufacture progress simply by asking
    # finish() to edit task metadata. A slice must either modify a real
    # implementation/test file or execute an automated check.
    before_finish = git("status", "--porcelain").stdout.splitlines()

    meaningful_changes = []

    task_rel = relative(task_path)
    current_rel = relative(current_path)

    for line in before_finish:
        if len(line) < 4:
            continue

        changed_path = line[3:].strip()

        if changed_path not in {task_rel, current_rel}:
            meaningful_changes.append(changed_path)

    if (
        status == "IN_PROGRESS"
        and not meaningful_changes
        and not check_results
    ):
        return (
            "ERROR: no implementation change or automated check was "
            "completed. Do real work before calling finish()."
        )

    task_text = task_path.read_text(errors="replace")

if LOCAL_MODE == "task":
    slice_plan = [
        (
            "Complete the entire assigned canonical task. "
            "Use the task MD as the authoritative implementation plan. "
            "Inspect only relevant source/tests, make the required changes, "
            "run the required targeted automated checks, and finish the "
            "canonical task. If the full task genuinely cannot be completed "
            "in this invocation, finish IN_PROGRESS with a concise summary; "
            "the next fresh invocation will receive the same canonical task."
        )
    ]

    current_slice_id = 1
    current_slice_text = slice_plan[0]

else:
    slice_plan = build_slice_plan(task_text)
    completed = completed_slice_ids(task_text)

    pending_ids = [
        i
        for i in range(1, len(slice_plan) + 1)
        if i not in completed
    ]

    if pending_ids:
        current_slice_id = pending_ids[0]
        current_slice_text = slice_plan[
            current_slice_id - 1
        ]
    else:
        current_slice_id = (
            max(completed) + 1
            if completed
            else 1
        )

        current_slice_text = (
            "Final evidence reconciliation only. "
            "Inspect existing evidence and run one remaining "
            "required narrow automated check."
        )

        while len(slice_plan) < current_slice_id:
            slice_plan.append(current_slice_text)

print(
    f"[local-worker] mode={LOCAL_MODE}",
    flush=True,
)

if LOCAL_MODE == "task":
    print(
        f"[local-worker] canonical task: {task_id}",
        flush=True,
    )
else:
    print(
        f"[local-worker] selected slice "
        f"{current_slice_id}/{len(slice_plan)}: "
        f"{current_slice_text}",
        flush=True,
    )

# Canonical TASK mode deliberately gives repository discovery back to
# the model. Python does not pre-plan or pre-select source files.
if LOCAL_MODE == "task":
    candidate_files = []

elif "discover_candidate_files" in globals():
    candidate_files = discover_candidate_files(
        current_slice_text
    )

else:
    candidate_files = []

if candidate_files:
    print(
        "[local-worker] likely relevant files:",
        flush=True,
    )

    for candidate, score in candidate_files:
        print(
            f"  score={score:>3} {candidate}",
            flush=True,
        )


current_text = (
    current_path.read_text(errors="replace")
    if current_path.exists()
    else ""
)

# Keep initial context deliberately small.
# Load the canonical task document before constructing the Ollama prompt.
task_text = task_path.read_text(errors="replace")

task_excerpt = task_text[:7200]
current_excerpt = current_text[:1600]

system = """\
You are the Vuma local autonomous coding worker.

You have exactly ONE canonical Vuma task.

In TASK mode, execute the entire canonical task described by the task MD.
The task document is your implementation plan.

Do not invent another planning system.
Do not select another task.

Use tools. Do not merely explain what should be done.

Rules:
- NEVER install packages or dependencies.
- NEVER request apt, sudo, pip, npm, cargo, brew or package installation.
- If a tool reports an unavailable utility, choose another advertised tool
  or use the capability exposed by the wrapper.
- NEVER manually edit the assigned task file.
- NEVER manually edit docs/CURRENT.md.
- NEVER change NOT_STARTED/IN_PROGRESS/COMPLETE using replace_text.
- finish() alone owns task status, task progress and CURRENT metadata.
- The supervisor has already selected the canonical task.
- The assigned task MD is authoritative.
- Work through the task's Scope, Implementation Requirements,
  Acceptance Criteria, Tests Required and Definition of Done.
- Do not start another canonical task.
- Do not invent unrelated requirements.
- Perform real implementation/testing before calling finish().
- Never inspect the whole repository.
- Use targeted search only.
- Normally touch no more than 2-6 implementation/test files.
- Never work on another canonical task.
- Do not make unrelated improvements.
- Prefer exact replace_text edits.
- Read an existing file before write_file overwrites it.
- Normal slice: at most one affected-project build and one targeted test.
- Never run a solution-wide build or test.
- dotnet commands must target one .csproj.
- Acceptance/review/closure tasks should verify one small requirement per
  invocation, not the entire stage at once.
- If more work remains, call finish with IN_PROGRESS and name the next slice.
- COMPLETE means all task requirements are implemented and required automated
  checks for the task have passed.
- NEEDS_VERIFICATION is only for implementation complete but genuinely
  unavailable automated infrastructure/prerequisite.
- Never fake a passing check.
- Never ask a human anything.
- Always call finish exactly once after this micro-slice is done.
"""

candidate_text = "\n".join(
    f"- {path} (relevance {score})"
    for path, score in candidate_files
)

if not candidate_text:
    candidate_text = "- none discovered automatically"

user = f"""\
STAGE: {stage}
TASK: {task_id}
TASK FILE: {relative(task_path)}

LIKELY RELEVANT FILES:
{candidate_text}

TASK CONTENT:
{task_excerpt}

CURRENT EXCERPT:
{current_excerpt}

EXECUTION MODE: {LOCAL_MODE.upper()}

{"COMPLETE THIS ENTIRE CANONICAL TASK." if LOCAL_MODE == "task" else f"CURRENT MICRO-SLICE {current_slice_id}/{len(slice_plan)}: {current_slice_text}"}

The TASK CONTENT above is authoritative.

Do the work now.

Do not merely propose a plan.
Do not choose another canonical task.
Do not manually edit task status.
Do not manually edit docs/CURRENT.md.

LIKELY RELEVANT FILES above were selected automatically by the
Python wrapper. Start with those files.

Your first useful action should normally be:
- read_file on the most relevant candidate test file, OR
- search using 2-5 SHORT KEYWORDS if candidates are insufficient.

IMPORTANT SEARCH RULE:
Do NOT send a sentence to search().
Good:
  "company isolation tenant"
  "active company"
  "CompanyContext"
Bad:
  "find the test proving that one company cannot write another company"

Then implement or execute the narrow validation.

finish() is intentionally unavailable until you have either:
- changed a real implementation/test file, or
- executed a real automated check.

After completing this one slice, call finish().
"""


messages = [
    {"role": "system", "content": system},
    {"role": "user", "content": user},
]


def ollama_chat(msgs):
    payload = {
        "model": model,
        "messages": msgs,
        "stream": False,
        "tools": tools_for_turn(),
        "keep_alive": "30m",
        "options": {
            "num_ctx": 4096,
            "temperature": 0.1,
            "num_predict": 700,
        },
    }

    data = json.dumps(payload).encode()

    req = urllib.request.Request(
        OLLAMA_URL,
        data=data,
        headers={"Content-Type": "application/json"},
    )

    with urllib.request.urlopen(req, timeout=240) as response:
        return json.loads(response.read().decode())


def compact_history(msgs):
    if len(msgs) <= 10:
        return msgs

    tail = msgs[-7:]

    # Do not begin compacted history with an orphan tool response.
    while tail and tail[0].get("role") == "tool":
        tail.pop(0)

    return msgs[:2] + tail


no_tool_responses = 0

try:
    for turn in range(1, MAX_TURNS + 1):
        if finished:
            break

        print(
            f"[local-worker] model={model} "
            f"turn={turn}/{MAX_TURNS}",
            flush=True,
        )

        response = ollama_chat(messages)
        msg = response.get("message") or {}

        assistant = {
            "role": "assistant",
            "content": msg.get("content") or "",
        }

        calls = msg.get("tool_calls") or []

        # Qwen2.5-Coder may express the tool call as plain JSON in
        # message.content rather than Ollama's structured tool_calls field.
        # Convert that JSON into the normal internal representation.
        if not calls:
            calls = parse_text_tool_call(
                msg.get("content") or ""
            )

            if calls:
                print(
                    "[local-worker] recovered JSON text tool call",
                    flush=True,
                )

        if calls:
            assistant["tool_calls"] = calls

        messages.append(assistant)

        if not calls:
            no_tool_responses += 1

            content = (msg.get("content") or "").strip()
            if content:
                print(
                    "[local-worker] model text:",
                    limit_text(content, 1000),
                    flush=True,
                )

            if no_tool_responses >= 2:
                raise RuntimeError(
                    "model returned no tool calls twice"
                )

            messages.append(
                {
                    "role": "user",
                    "content": (
                        "Do not explain. Continue the task using tools. "
                        "You must call finish when this slice is done."
                    ),
                }
            )

            messages = compact_history(messages)
            continue

        no_tool_responses = 0

        for call in calls:
            tool_calls_used += 1

            if tool_calls_used > MAX_TOOL_CALLS:
                raise RuntimeError("tool-call budget exceeded")

            fn = (call.get("function") or {}).get("name")
            fn_args = (call.get("function") or {}).get(
                "arguments",
                {},
            )

            if isinstance(fn_args, str):
                try:
                    fn_args = json.loads(fn_args)
                except json.JSONDecodeError:
                    fn_args = {}

            print(
                f"[local-worker] tool={fn}",
                flush=True,
            )

            if fn not in FUNCTIONS:
                result = f"ERROR: unknown tool {fn}"

            elif fn == "finish" and not finish_allowed():
                result = (
                    "ERROR: finish is unavailable because no real "
                    "implementation/test change or automated check has "
                    "occurred. Perform CURRENT MICRO-SLICE first."
                )

            else:
                try:
                    result = FUNCTIONS[fn](**fn_args)
                except Exception as e:
                    result = f"ERROR: {type(e).__name__}: {e}"

            print(
                "[local-worker] result:",
                limit_text(str(result), 1200),
                flush=True,
            )

            fingerprint = json.dumps(
                {
                    "tool": fn,
                    "arguments": fn_args,
                },
                sort_keys=True,
                default=str,
            )

            if str(result).startswith("ERROR:"):
                if fingerprint == last_error_fingerprint:
                    same_error_count += 1
                else:
                    last_error_fingerprint = fingerprint
                    same_error_count = 1
            else:
                last_error_fingerprint = None
                same_error_count = 0

            messages.append(
                {
                    "role": "tool",
                    "tool_name": fn or "unknown",
                    "content": str(result),
                }
            )

            if same_error_count >= 2:
                messages.append(
                    {
                        "role": "user",
                        "content": (
                            "STOP repeating that tool call. It is invalid. "
                            "Do not edit task status or CURRENT manually. "
                            "Choose an actual unfinished requirement, inspect "
                            "the relevant implementation/test, run or implement "
                            "that requirement, then call finish()."
                        ),
                    }
                )

                last_error_fingerprint = None
                same_error_count = 0

            if fn == "finish" and finished:
                break

        messages = compact_history(messages)

    if not finished:
        raise RuntimeError(
            "worker ended without a successful finish call"
        )

except Exception as e:
    print(
        f"LOCAL_WORKER_ERROR: {type(e).__name__}: {e}",
        file=sys.stderr,
    )

    # Starting tree was clean, so rollback only this invocation's
    # uncommitted modifications.
    git("reset", "--hard", "HEAD")
    git("clean", "-fd")

    raise SystemExit(6)

print(
    f"[local-worker] {task_id} micro-slice complete",
    flush=True,
)
