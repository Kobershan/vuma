# Zenith Retail

A modular retail ERP with POS at its centre — a Windows-installed application running against an
in-store physical server, replicating to a cloud tier for backup, multi-store roll-up and mobile
access. Licensed as a monthly SaaS subscription.

The store keeps trading when the internet does not. That constraint drives most of the architecture.

## Shape of the system

```
Store premises            Cloud                       Mobile
─────────────────         ─────────────────           ─────────────────
POS terminals (WPF)   →   Zenith Cloud API        →   Zenith Admin
+ SQLite offline cache    PostgreSQL replica          (Kotlin/Compose)
        ↓                 S3 backup vault
Zenith Store Server
ASP.NET Core + PostgreSQL
(Windows Service)
```

- **Store server is the source of truth for the store**; cloud is the source of truth for the
  tenant roll-up.
- **API-first.** Nothing exists in the desktop UI that is not reachable through the versioned REST
  API — which is how the Android app and the public storefront/loyalty APIs get everything for free.
- **Offline-first at the till.** Sales complete against local SQLite and replay on reconnect.

## Stack

.NET 9 / C# 13 · WPF + MVVM · ASP.NET Core 9 Minimal APIs · PostgreSQL 16 · EF Core 9 + Npgsql ·
SQLite terminal cache · SignalR · Serilog + OpenTelemetry · QuestPDF · xUnit + Testcontainers ·
Kotlin 2.x + Jetpack Compose · Velopack + WiX v4.

Full table with rationale: [CLAUDE.md §4](CLAUDE.md). Substitutions require an ADR.

## Repository

```
CLAUDE.md            the build contract — read it before touching anything
docs/                PROGRESS (state), DECISIONS (ADRs), ROADMAP, LICENSING, TESTING, API specs
docs/stages/         one document per stage, 00 → 31
.claude/agents/      specialist review subagents (see docs/AGENTS.md)
src/                 Domain ← Application ← Infrastructure ← hosts
android/             Zenith Admin
tests/               unit, integration, sync, UI
deploy/ scripts/     docker-compose, WiX, Velopack, DR and seed scripts
```

## How this repo is built

Zenith is built stage-by-stage by autonomous Claude Code sessions. Each session reads `CLAUDE.md`
and `docs/PROGRESS.md`, executes the next incomplete stage to completion, runs its exit checklist,
and writes a handoff for the next session. Stages are dependency-ordered — Finance (07) precedes
Inventory (08) precedes POS (09), because those modules post to the ledger.

Current state, blockers and what happens next: [docs/PROGRESS.md](docs/PROGRESS.md).
Stage index: [docs/ROADMAP.md](docs/ROADMAP.md).

## Development status

**Pre-alpha — Stage 00 (foundation).** Not usable, not installable, no release. The documentation
set is ahead of the code by design: the specification is the contract the code is written against.

## Building

Requires the .NET 9 SDK. Docker is needed for the integration tests (Testcontainers/PostgreSQL), and
**Windows is required** for the WPF desktop projects and the FlaUI UI tests — those cannot build on
Linux or macOS. The cross-platform projects (`Domain`, `Application`, `Contracts`, and the ASP.NET
Core hosts) build anywhere.

```bash
dotnet build -c Release
dotnet test
```

## Licence

Proprietary. All rights reserved.
