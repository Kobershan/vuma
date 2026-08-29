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
MAX_TURNS = 12
MAX_TOOL_CALLS = 24
MAX_CHECKS = 3

finished = False
tool_calls_used = 0
checks_used = 0
read_paths = set()
check_results = []


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


def search(query, path=".", regex=False):
    p = safe_path(path)

    cmd = [
        "rg",
        "-n",
        "--no-heading",
        "--color",
        "never",
        "--glob",
        "!.git/**",
        "--glob",
        "!bin/**",
        "--glob",
        "!obj/**",
        "--glob",
        "!docs/automation/logs/**",
    ]

    if not regex:
        cmd.append("--fixed-strings")

    cmd.extend(["--", query, str(p)])

    try:
        result = subprocess.run(
            cmd,
            cwd=repo,
            text=True,
            capture_output=True,
            timeout=20,
        )
    except FileNotFoundError:
        return "ERROR: rg/ripgrep is not installed"
    except subprocess.TimeoutExpired:
        return "ERROR: search timed out"

    lines = result.stdout.splitlines()[:35]

    return limit_text(
        "\n".join(lines) if lines else "NO MATCHES",
        6000,
    )


def replace_text(path, old, new):
    p = safe_path(path)

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

    if status == "IN_PROGRESS" and not next_slice:
        return "ERROR: IN_PROGRESS requires next_slice"

    if status == "COMPLETE" and not check_results:
        return (
            "ERROR: COMPLETE requires at least one automated "
            "check in this invocation"
        )

    task_text = task_path.read_text(errors="replace")
    task_text = set_task_status(task_text, status)

    stamp = datetime.datetime.now(
        datetime.timezone.utc
    ).strftime("%Y-%m-%d %H:%M UTC")

    progress = (
        f"\n- {stamp} LOCAL-SLICE: {summary}"
    )

    if next_slice:
        progress += f" Next: {next_slice}"

    progress += "\n"

    if "## Local worker progress" not in task_text:
        task_text = (
            task_text.rstrip()
            + "\n\n## Local worker progress\n"
            + progress
        )
    else:
        task_text = task_text.rstrip() + progress

    task_path.write_text(task_text)
    update_current(summary, next_slice, status)

    diff_check = git("diff", "--check")

    if diff_check.returncode != 0:
        return (
            "ERROR: git diff --check failed:\n"
            + diff_check.stdout
            + diff_check.stderr
        )

    changed = git("status", "--porcelain").stdout.strip()

    if not changed:
        return "ERROR: finish produced no repository changes"

    git("add", "-A", check=True)

    scope = f"stage-{stage.lower()}"

    if status == "COMPLETE":
        message = f"feat({scope}): complete {task_id}"
    else:
        message = f"feat({scope}): local slice for {task_id}"

    commit = git(
        "commit",
        "-m",
        message,
        timeout=120,
    )

    if commit.returncode != 0:
        git("reset")
        return (
            "ERROR: commit failed:\n"
            + commit.stdout
            + commit.stderr
        )

    push = git("push", timeout=180)

    pushed = push.returncode == 0

    finished = True

    return json.dumps(
        {
            "ok": True,
            "status": status,
            "commit": git(
                "rev-parse",
                "HEAD",
            ).stdout.strip(),
            "pushed": pushed,
            "push_output": limit_text(
                push.stdout + push.stderr,
                1500,
            ),
        }
    )


TOOLS = [
    {
        "type": "function",
        "function": {
            "name": "read_file",
            "description": "Read a small line range from one repository file.",
            "parameters": {
                "type": "object",
                "required": ["path"],
                "properties": {
                    "path": {"type": "string"},
                    "start_line": {"type": "integer"},
                    "max_lines": {"type": "integer"},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "search",
            "description": "Targeted ripgrep search inside the repository.",
            "parameters": {
                "type": "object",
                "required": ["query"],
                "properties": {
                    "query": {"type": "string"},
                    "path": {"type": "string"},
                    "regex": {"type": "boolean"},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "replace_text",
            "description": "Replace one exact text occurrence in a file.",
            "parameters": {
                "type": "object",
                "required": ["path", "old", "new"],
                "properties": {
                    "path": {"type": "string"},
                    "old": {"type": "string"},
                    "new": {"type": "string"},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "write_file",
            "description": "Create a text file or rewrite a file already read.",
            "parameters": {
                "type": "object",
                "required": ["path", "content"],
                "properties": {
                    "path": {"type": "string"},
                    "content": {"type": "string"},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "run_check",
            "description": "Run one targeted build/test/restore or git diff/status check.",
            "parameters": {
                "type": "object",
                "required": ["command"],
                "properties": {
                    "command": {"type": "string"},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "finish",
            "description": "Finish this micro-slice, update task/current, commit and push.",
            "parameters": {
                "type": "object",
                "required": ["status", "summary"],
                "properties": {
                    "status": {
                        "type": "string",
                        "enum": [
                            "IN_PROGRESS",
                            "COMPLETE",
                            "NEEDS_VERIFICATION",
                        ],
                    },
                    "summary": {"type": "string"},
                    "next_slice": {"type": "string"},
                },
            },
        },
    },
]


FUNCTIONS = {
    "read_file": read_file,
    "search": search,
    "replace_text": replace_text,
    "write_file": write_file,
    "run_check": run_check,
    "finish": finish,
}


def parse_text_tool_call(content):
    """
    Qwen2.5-Coder sometimes emits a valid tool request as plain JSON text
    instead of Ollama's structured message.tool_calls field.

    Accept forms such as:

      {"name":"read_file","arguments":{"path":"..."}}

    and:

      {"function":{"name":"read_file","arguments":{...}}}

    Also tolerate fenced ```json blocks.
    """
    if not content:
        return []

    text = content.strip()

    if text.startswith("```"):
        lines = text.splitlines()

        if lines and lines[0].startswith("```"):
            lines = lines[1:]

        if lines and lines[-1].strip() == "```":
            lines = lines[:-1]

        text = "\n".join(lines).strip()

    # Occasionally the model writes a little prose before/after the object.
    first = text.find("{")
    last = text.rfind("}")

    if first >= 0 and last > first:
        text = text[first:last + 1]

    try:
        obj = json.loads(text)
    except Exception:
        return []

    if not isinstance(obj, dict):
        return []

    # Simple Qwen form:
    # {"name":"read_file","arguments":{...}}
    if isinstance(obj.get("name"), str):
        name = obj["name"]
        arguments = obj.get("arguments", {})

        if name in FUNCTIONS and isinstance(arguments, dict):
            return [
                {
                    "function": {
                        "name": name,
                        "arguments": arguments,
                    }
                }
            ]

    # OpenAI/Ollama-like nested form.
    fn = obj.get("function")

    if isinstance(fn, dict) and isinstance(fn.get("name"), str):
        name = fn["name"]
        arguments = fn.get("arguments", {})

        if isinstance(arguments, str):
            try:
                arguments = json.loads(arguments)
            except Exception:
                arguments = {}

        if name in FUNCTIONS and isinstance(arguments, dict):
            return [
                {
                    "function": {
                        "name": name,
                        "arguments": arguments,
                    }
                }
            ]

    return []



task_text = task_path.read_text(errors="replace")
current_text = (
    current_path.read_text(errors="replace")
    if current_path.exists()
    else ""
)

# Keep initial context deliberately small.
task_excerpt = task_text[:7200]
current_excerpt = current_text[:1600]

system = """\
You are the Vuma local autonomous coding worker.

You have exactly ONE canonical task and must perform exactly ONE small
coherent micro-slice per invocation.

Use tools. Do not merely explain what should be done.

Rules:
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

user = f"""\
STAGE: {stage}
TASK: {task_id}
TASK FILE: {relative(task_path)}

TASK CONTENT:
{task_excerpt}

CURRENT EXCERPT:
{current_excerpt}

Choose the smallest unfinished slice now.
Use the tools to inspect only what is necessary, implement/verify it,
run narrow validation, then call finish.
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
        "tools": TOOLS,
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

            messages.append(
                {
                    "role": "tool",
                    "tool_name": fn or "unknown",
                    "content": str(result),
                }
            )

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
