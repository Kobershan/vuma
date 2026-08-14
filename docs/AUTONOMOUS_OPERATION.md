# AUTONOMOUS OPERATION — running Vuma Retail builds unattended

This document exists because permission prompts and clarifying questions were burning sessions. Follow
it once and Claude Code will run stages overnight without stopping for you.

## 1. Why you were still getting prompts

Five separate causes, all fixed by the setup below.

| Cause | Fix |
|---|---|
| **`.claude/` is a protected directory.** Writes there prompt in every mode except `bypassPermissions`, and even then editing settings is a known friction point. | `CLAUDE.md` now forbids Claude from touching `.claude/**`. Nothing in the build needs to. |
| **`bypassPermissions` cannot be entered mid-session.** Pressing Shift+Tab won't get you there. | It must be set at launch: `--dangerously-skip-permissions`, or `permissions.defaultMode` in settings **before** the session starts. |
| **Some permission settings are only honoured from user scope**, not from a repo's `.claude/settings.json`. A checked-in project file cannot always grant a session its mode. | Copy `deploy/claude-user-settings.json` into `~/.claude/settings.json` as well. Belt and braces. |
| **A one-time warning dialog** appears the first time you start an interactive session in bypass mode, and background/headless runs are refused until you have accepted it once. | Run `claude --dangerously-skip-permissions` interactively once, accept, exit. Then scripts work. |
| **`AskUserQuestion` still runs in bypass mode** — it isn't a permission prompt, it's a tool. Claude calling it stops and waits for you indefinitely. | It is now in the `deny` list. Claude physically cannot ask you a question. |

Two more that will bite depending on your setup:

- **Running as root or under `sudo` on Linux/WSL**: bypass mode refuses to start. Run as a normal user.
- **Claude Code on the web** ignores `bypassPermissions` from settings files entirely. Overnight runs must be the **local CLI**, not the web version.

## 2. One-time setup

```powershell
# 1. Copy user-scope settings (merge if the file already exists)
mkdir -Force "$env:USERPROFILE\.claude"
Copy-Item deploy\claude-user-settings.json "$env:USERPROFILE\.claude\settings.json"

# 2. Accept the bypass warning once, interactively
claude --dangerously-skip-permissions
#    ...accept the dialog, then type /exit

# 3. Confirm the repo is clean and committed
git status
```

That's it. From then on the runner script works unattended.

## 3. Running overnight

```powershell
.\scripts\run-autonomous.ps1                 # runs until every stage is DONE or it stalls
.\scripts\run-autonomous.ps1 -MaxStages 3    # stop after three stages
```

The script runs **one stage per Claude invocation** in headless mode. That matters: each stage gets a
fresh context window, so quality does not decay across a long night the way it does in one enormous
session. Between stages it verifies real progress was made (`docs/PROGRESS.md` changed **and** a commit
landed **and** the build is green) and stops cleanly if not, rather than burning eight hours going in
circles.

Everything is logged to `logs/autonomous/<timestamp>/stage-NN.log`. Read the summary in the morning.

## 4. What to check in the morning

```powershell
git log --oneline -20                 # what shipped
Get-Content docs\PROGRESS.md          # where it stopped and why
Get-Content logs\autonomous\*\summary.md
dotnet build -c Release; dotnet test  # confirm green yourself
```

If a stage is marked `BLOCKED`, `PROGRESS.md` says exactly what stopped it. Fix that one thing and
re-run the script; it picks up where it left off.

## 5. Rules that keep it autonomous

These are in `CLAUDE.md` as well, because that is what Claude actually reads:

- **Never ask a question.** Decide, record the decision as an ADR, continue.
- **Never edit `.claude/**`.** It is a protected path and it will stop the run.
- **Never leave the repo broken.** Commit at green checkpoints. A run that ends mid-refactor wastes the
  whole night.
- **Compact when context gets long** rather than pushing on with degraded judgement. Long unattended
  runs lose quality before they lose context, so a stage that is going badly should stop, write an
  honest handoff, and let the next invocation start fresh.
- **If something genuinely cannot be done** (needs a paid account, needs physical hardware), stub it
  behind an interface, log it under "Deferred" in `PROGRESS.md`, and keep moving. Do not stall.

## 6. A note on what bypass mode gives up

`bypassPermissions` removes the safety checks, including protection against a bad instruction hidden in
a file Claude reads. Run it on a machine you can afford to lose: a dev box, a VM, or a container that
holds nothing but this repository and no production credentials. Never point an unattended run at
anything that can reach a live store's database.
