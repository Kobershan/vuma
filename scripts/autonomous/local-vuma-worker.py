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
        if (
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
        status == "COMPLETE"
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
    task_text = set_task_status(task_text, status)

    stamp = datetime.datetime.now(
        datetime.timezone.utc
    ).strftime("%Y-%m-%d %H:%M UTC")

    slice_marker = (
        str(current_slice_id)
        if current_slice_id is not None
        else "0"
    )

    progress = (
        f"\n- {stamp} LOCAL-SLICE-DONE "
        f"{slice_marker}: {summary}"
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



def section_text(text, heading):
    pattern = re.compile(
        rf"^## {re.escape(heading)}\s*$\n(.*?)(?=^## |\Z)",
        re.MULTILINE | re.DOTALL,
    )

    match = pattern.search(text)

    if not match:
        return ""

    return match.group(1).strip()


def clean_requirement(text):
    text = re.sub(r"^[-*]\s*", "", text.strip())
    text = re.sub(r"\s+", " ", text)
    return text.strip(" .")


def split_requirements(text, split_commas=False):
    if not text:
        return []

    items = []

    # First split actual bullets/lines and semicolon-separated requirements.
    parts = re.split(r"\n+|;\s*", text)

    for part in parts:
        part = clean_requirement(part)

        if not part:
            continue

        if split_commas:
            subparts = re.split(
                r",\s*|\s+and\s+",
                part,
                flags=re.IGNORECASE,
            )

            for sub in subparts:
                sub = clean_requirement(sub)

                if len(sub) >= 4:
                    items.append(sub)
        else:
            items.append(part)

    return items


def completed_slice_ids(text):
    return {
        int(x)
        for x in re.findall(
            r"LOCAL-SLICE-DONE\s+(\d+)",
            text,
        )
    }


def build_slice_plan(text):
    """
    Convert a canonical Vuma task into small deterministic work packets.

    The model never decides what the next packet is.
    """

    # --------------------------------------------------------
    # Stage 06c acceptance has a deliberately explicit plan.
    # This task is broad enough that a 3B model must NOT infer
    # the matrix itself.
    # --------------------------------------------------------

    if task_id == "TASK-06C-12":
        return [
            (
                "Isolation acceptance. Find the narrowest existing Stage 06c "
                "unit/integration tests proving one company cannot read or "
                "write another company's data. If coverage is missing, add "
                "only the smallest focused test. Run only the relevant "
                "test-project/filter."
            ),
            (
                "Active-company filtering acceptance. Find or add the "
                "narrowest test proving inactive/deactivated companies are "
                "excluded from ordinary company operations and fan-out reads. "
                "Run only the relevant filtered/project test."
            ),
            (
                "No-cross-company-write acceptance. Find or add the narrowest "
                "architecture/unit test proving one request/handler cannot "
                "write through two company database contexts. Run only that "
                "targeted validation."
            ),
            (
                "Three-company fixture acceptance. Inspect the existing "
                "Stage 06c fixture/seed support. Verify three deterministic "
                "companies can be created independently and fixture setup is "
                "rerunnable. Add only narrowly missing fixture coverage and "
                "run the relevant test."
            ),
            (
                "Company API acceptance. Verify the narrow API tests for "
                "company selection, tenant/company access and active-company "
                "filtering. Add only missing focused coverage and run the "
                "relevant API/integration filter."
            ),
            (
                "Backup/restore and sync boundary acceptance. Find the "
                "Stage 06c per-company backup/restore or sync tests and run "
                "the narrowest relevant filter. If narrowly missing, add one "
                "focused test. Do not run unrelated backup suites."
            ),
            (
                "PostgreSQL Stage 06c acceptance. Use the documented "
                "disposable PostgreSQL test mechanism if required, and run "
                "only the narrow Stage 06c/company persistence or migration "
                "filter. If infrastructure genuinely cannot run, capture the "
                "exact automated failure rather than changing unrelated code."
            ),
            (
                "Architecture acceptance. Run the narrowest architecture "
                "validation covering Stage 06c multi-company boundaries. "
                "Only use the complete architecture test project if there is "
                "no usable Stage 06c filter."
            ),
            (
                "Acceptance evidence reconciliation. Review the LOCAL-SLICE "
                "evidence already recorded for this task and the task's "
                "Acceptance Criteria / Tests Required. Run only any remaining "
                "required automated check. Mark COMPLETE only when every "
                "required acceptance area has evidence; otherwise use "
                "NEEDS_VERIFICATION only for a genuinely unavailable required "
                "automated prerequisite."
            ),
        ]

    # --------------------------------------------------------
    # Generic planner for later canonical tasks.
    # --------------------------------------------------------

    plan = []

    implementation = split_requirements(
        section_text(text, "Implementation Requirements")
    )

    acceptance = split_requirements(
        section_text(text, "Acceptance Criteria")
    )

    tests = split_requirements(
        section_text(text, "Tests Required"),
        split_commas=True,
    )

    for item in implementation[:6]:
        plan.append(
            "Implementation requirement: "
            + item
            + ". Implement only the smallest coherent portion needed for "
              "this requirement and run one narrow validation."
        )

    for item in acceptance[:4]:
        plan.append(
            "Acceptance requirement: "
            + item
            + ". Verify this requirement only. Add narrowly missing test "
              "coverage if necessary and run the targeted check."
        )

    for item in tests[:4]:
        # Avoid turning phrases such as "full applicable suite" into a
        # solution-wide build during an ordinary micro-slice.
        if "full applicable suite" in item.lower():
            plan.append(
                "Test requirement: identify and run the smallest targeted "
                "subset of the applicable suite that validates this task. "
                "Do not run a solution-wide test command in this slice."
            )
        else:
            plan.append(
                "Test requirement: "
                + item
                + ". Run only the narrow project/filter relevant to the "
                  "assigned canonical task."
            )

    if not plan:
        objective = clean_requirement(
            section_text(text, "Objective")
        )

        if objective:
            plan.append(
                "Implement or verify this objective in one small coherent "
                f"slice: {objective}"
            )
        else:
            plan.append(
                "Inspect the assigned task and implement one smallest "
                "coherent unfinished requirement."
            )

    # Keep local plans bounded.
    return plan[:12]


def meaningful_work_exists():
    result = git("status", "--porcelain")
    lines = result.stdout.splitlines()

    ignored = {
        relative(task_path),
        relative(current_path),
    }

    for line in lines:
        if len(line) < 4:
            continue

        changed = line[3:].strip()

        # Handle "old -> new" rename format.
        if " -> " in changed:
            changed = changed.split(" -> ", 1)[1]

        if changed not in ignored:
            return True

    return False


def finish_allowed():
    # A model may not finish before it has either:
    # - produced a real source/test change, or
    # - executed at least one actual automated check.
    return meaningful_work_exists() or bool(check_results)


def tools_for_turn():
    if finish_allowed():
        return TOOLS

    # Do not even advertise finish() until real work happened.
    return [
        tool
        for tool in TOOLS
        if (
            tool.get("function", {}).get("name")
            != "finish"
        )
    ]


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

slice_plan = build_slice_plan(task_text)
completed = completed_slice_ids(task_text)

pending_ids = [
    i
    for i in range(1, len(slice_plan) + 1)
    if i not in completed
]

if pending_ids:
    current_slice_id = pending_ids[0]
    current_slice_text = slice_plan[current_slice_id - 1]
else:
    # All predefined packets have evidence but the canonical task is
    # still not terminal. Give it a tightly scoped reconciliation pass.
    current_slice_id = (
        max(completed)
        + 1
        if completed
        else 1
    )

    current_slice_text = (
        "Final evidence reconciliation only. Inspect the assigned task's "
        "existing LOCAL-SLICE-DONE records and required acceptance/tests. "
        "Run one remaining required narrow automated check. Do not add new "
        "product behavior. Mark COMPLETE only if all requirements genuinely "
        "have automated evidence."
    )

    # Make this the final known item so COMPLETE is permitted.
    while len(slice_plan) < current_slice_id:
        slice_plan.append(current_slice_text)

print(
    f"[local-worker] selected slice "
    f"{current_slice_id}/{len(slice_plan)}: "
    f"{current_slice_text}",
    flush=True,
)

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
- NEVER manually edit the assigned task file.
- NEVER manually edit docs/CURRENT.md.
- NEVER change NOT_STARTED/IN_PROGRESS/COMPLETE using replace_text.
- finish() alone owns task status, task progress and CURRENT metadata.
- The Python wrapper has ALREADY selected the exact micro-slice.
- Do NOT choose, redesign, broaden or replace that micro-slice.
- First perform actual implementation or the actual automated check required
  by CURRENT MICRO-SLICE.
- For an acceptance task, verify ONLY CURRENT MICRO-SLICE.
- Do not mark progress before performing that requirement.
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

CURRENT MICRO-SLICE {current_slice_id}/{len(slice_plan)}:
{current_slice_text}

Do EXACTLY CURRENT MICRO-SLICE.

Do not choose another requirement.
Do not manually edit task status.
Do not manually edit docs/CURRENT.md.

Your first useful action should normally be:
- targeted search for the relevant implementation/test symbol, OR
- read the directly relevant source/test file if already known.

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
