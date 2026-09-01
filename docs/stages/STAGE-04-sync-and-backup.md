# STAGE 04 — Sync + cloud backup foundation: outbox/inbox, HLC, restore

**Status:** DONE (2026-08-10) · **Depends on:** 03 · **Reference reading:** `CLAUDE.md` §3 (R1, R2, R4, R6, R8), §4 (messaging pattern, logging row), §7 (rules 2, 3, 6, 7, 8, 9), §8, `docs/DECISIONS.md` ADR-003, ADR-006, ADR-007, ADR-010, ADR-012, ADR-022, ADR-028, ADR-035, `docs/CONVENTIONS.md` §2, §5, §6, `docs/TESTING.md` §1–§2, `docs/API_STANDARDS.md` §5, §8–§9, `docs/SECURITY.md`, `docs/SYNC_AND_BACKUP.md` (written by this stage)

## Task index
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) for project and boundary rules, then load only the references named by the eventual task.

**Architecture checklist:** WHAT/WHY come from this stage's Objective; affected layers/components come from its Deliverables; data, API, security, multi-company, synchronization, licensing, and testing rules come from the linked authority documents. Missing answers are **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs in the header apply; a new ADR is required only for a new decision. Nothing outside stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 04-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in docs/tasks/README.md.

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-04-001 | Complete sync, backup, and restore foundation | Stage 03 | COMPLETE |

## Objective

Stage 01 put `sync_state` on every row and `[Replicated(scope, policy)]` on every entity, and nothing
read either. Stage 03 reserved pipeline slot 300 and left it empty. This stage fills both, and turns
`VumaRetail.Sync` and `VumaRetail.CloudApi` from scaffolds into the machinery that R1, R2 and R4
depend on.

Four things become true here and stay true for the rest of the build:

1. **A change and its replication commit together or not at all.** ADR-006's outbox row is written
   inside the transaction that produced the change, by a pipeline behaviour, from the EF change
   tracker — so no module ever writes an outbox row by hand and no module can forget to.
2. **Delivery is at-least-once; effect is exactly-once.** The receiver deduplicates on
   `(source_node, operation_id)` before it applies anything, so a retried batch, a resumed upload and
   a replayed queue are all the same thing.
3. **Divergence is settled by a policy the entity declared, or it is escalated to a person.** ADR-007's
   hybrid logical clock orders events without a global clock; the `[Replicated]` policy decides who
   wins; `ManualReview` means a row in a queue rather than a silent loss.
4. **A store can be rebuilt from the cloud.** Snapshot → encrypt → vault → restore → verify, exercised
   end to end by an automated drill in this stage's own test suite, against real PostgreSQL. Stage 31
   hardens it into the full DR exercise R4 names; this stage makes it real.

## Deliverables

**Domain — `sync` and `backup`**
- `HlcStamp` — the ADR-007 hybrid logical clock stamp: physical milliseconds, a logical counter and
  the node that issued it. Totally ordered, round-trips through a sortable string, and comparable
  across nodes whose wall clocks disagree.
- `OutboxMessage` — one replicated change, with its operation id, source node, HLC stamp, target
  scope, payload and delivery state. Append-only in spirit; state advances Pending → InFlight →
  Dispatched, or → Failed with the attempt count and last error.
- `InboxMessage` — the receiver's dedup ledger, unique on `(tenant, source_node, operation_id)`.
- `SyncCursor` — how far a peer has been acknowledged, per direction. The thing that makes a resumed
  transfer resume rather than restart.
- `ConflictEntry` — a divergence the entity's policy could not settle, with both versions kept, for
  the review queue ADR-007 promises.
- `BackupSnapshot` — one snapshot run: kind, object key, byte size, SHA-256, status, and the verify
  and restore stamps that make R4 auditable rather than assumed.

**Application — ports**
- `IHybridClock` — `Next()` and `Observe(remote)`. The only source of HLC stamps.
- `INodeIdentity` — who this node is (`store:{id}`, `cloud`, `terminal:{id}`) and what kind it is.
- `IReplicationRegistry` — reads `[Replicated]` once per CLR type and caches it, so the outbox
  behaviour does not reflect per row on a till doing ten sales a minute.
- `IOutboxRepository`, `IInboxRepository`, `ISyncCursorRepository`, `IConflictRepository`.
- `ISyncTransport` — ship a batch to a peer, get an ack. The seam the store→cloud HTTP client and the
  in-process test loopback both sit behind.
- `IBackupVault` — S3-compatible object storage (ADR-022's provider-interface shape).
- `IBackupEngine` — take and restore a database snapshot.
- `ISnapshotCipher` — encrypt at rest, because a snapshot is every row a tenant has.
- `IBackupService` — the orchestration R4 is measured on.

**Contracts — the wire**
- `SyncBatchRequest` / `SyncOperationDto` / `SyncBatchResponse` / `SyncOperationOutcome`,
  `SyncStatusResponse`, `ConflictResponse`, `BackupSnapshotResponse`.

**`VumaRetail.Sync` — the protocol, no provider in it**
- `HybridLogicalClock` — ADR-007, including the two cases that matter: the wall clock going backwards
  and a remote stamp from the future.
- `ConflictResolver` — the five `ConflictPolicy` values, resolved as a table, with node kind as an
  input for `StoreWins`/`CloudWins`.
- `SyncReceiver` — dedupe, order, resolve, apply, acknowledge. Idempotent by construction.
- `OutboxDispatcherService` — the `IHostedService` that drains the outbox in batches with capped
  exponential backoff, and never blocks a sale to do it.
- `SyncPermissions` and `BackupPermissions` — the module declarations ADR-013 requires.
- Commands and queries: resolve a conflict, run a snapshot, verify a snapshot, read sync status,
  read the conflict queue, list snapshots.

**Infrastructure — the adapters**
- Five EF configurations and the `SyncAndBackup` migration, reversible.
- Four repositories.
- `OutboxBehaviour` in pipeline slot 300 — reads the change tracker after the handler and before the
  commit, and writes one outbox row per replicated change.
- `ReplicationRegistry`, `NodeIdentity`.
- `HttpSyncTransport` — store → cloud over the versioned API.
- `PostgresBackupEngine` — `pg_dump`/`pg_restore` in the custom format.
- `AesGcmSnapshotCipher`, `FileSystemBackupVault`, `S3BackupVault`, `BackupService`.

**Hosts**
- `SyncEndpoints` and `BackupEndpoints` on the versioned API, served by both the store server (for
  terminals) and the cloud (for stores).
- `VumaRetail.CloudApi` wired for real: `AddVumaWeb` / `AddVumaPersistence` / `AddVumaSync`, with
  `Vuma:Host:TenantId` deliberately empty so every request must carry its own tenant.
- The OTLP sink `CLAUDE.md` §4 names, added to `VumaLogging` and off unless configured.

**Docs and scripts**
- `docs/SYNC_AND_BACKUP.md` — the replication registry, the protocol, the conflict matrix, the backup
  and restore procedure.
- `scripts/backup.sh`, `scripts/restore.sh`, `scripts/dr-drill.sh`.

## Business rules

- **The outbox row lands in the same transaction as the change.** Slot 300 sits inside slot 200. A
  crash between the two would leave a store silently diverged from the cloud, which is the failure
  ADR-006 exists to make impossible.
- **A module never writes an outbox row.** The behaviour derives them from the change tracker and the
  `[Replicated]` declaration. A module that could opt in could also forget to.
- **`ReplicationScope.NodeLocal` produces nothing.** The outbox, the inbox and the cursors are
  themselves `NodeLocal`, or the system would replicate its own replication for ever.
- **The receiver deduplicates before it applies.** `(tenant, source_node, operation_id)` is a unique
  index, not a check-then-act — two concurrent deliveries of one batch race, and the database is the
  only thing that can settle that.
- **A replayed batch is a success, not an error.** At-least-once transports retry; a receiver that
  answered `409` to a duplicate would turn every retry into a support call.
- **Conflict policy comes from the entity, never from the caller.** A sender that could name its own
  policy could overwrite anything.
- **`ManualReview` keeps both versions.** Escalation that discards the loser is not escalation.
- **The sync receiver is the one place that crosses the tenant filter**, through
  `BypassTenantFilter(reason)`, and re-scopes to the batch's tenant before applying anything.
- **Backup is exempt from read-only** (`ReadOnlyExemption.Backup`, ADR-028). A tenant's data
  protection never depends on their subscription status.
- **A snapshot is encrypted before it leaves the machine** and its SHA-256 is recorded, so a restore
  can prove it got back what it stored.
- **A restore is verified, not assumed.** The drill restores into a fresh database and compares row
  counts and checksums; a snapshot nobody has ever restored is not a backup.
- **Sync never blocks a sale (R1).** The dispatcher is a background service; a transport failure
  backs off and retries, and the till never learns about it.

## Tests / acceptance

- `HlcStamp`: total ordering across physical, counter and node id; round-trips through its sortable
  string form; sorts lexically in the same order it compares
- `HybridLogicalClock`: monotonic under a stalled clock; monotonic under a clock that goes
  *backwards*; `Observe` of a future remote stamp adopts it and stays ahead; concurrent `Next()`
  never repeats
- `ConflictResolver`: table-driven over all five policies × both node kinds × remote-newer /
  local-newer / equal
- `ReplicationRegistry`: reports the declared scope and policy; caches; every persisted entity
  resolves
- Outbox: a command that changes a replicated entity writes exactly one outbox row, in the same
  transaction; a handler that throws writes none; a `NodeLocal` entity writes none; a soft delete
  writes a `Delete` operation; the payload round-trips
- Inbox: applying a batch twice applies it once; the second answers `Duplicate`; two concurrent
  deliveries of the same operation leave one row
- Receiver: `LastWriterWins` takes the newer stamp; `StoreWins`/`CloudWins` respect node kind;
  `AppendOnly` never overwrites; `ManualReview` writes a conflict entry and leaves the local row
  alone; the cursor advances only on success
- Backup, against real PostgreSQL: snapshot → encrypt → vault → decrypt → restore into an empty
  database → the restored database has the same tables and the same row counts. This is the drill.
- Backup: a tampered snapshot fails its checksum and refuses to restore; a wrong key fails to decrypt
- API: `POST /api/v1/sync/batches` accepts a batch, is idempotent on replay, `403` without the
  permission, `422` on a malformed operation; the conflict queue and status endpoints answer
- Architecture, each proven by a deliberate violation: `BypassTenantFilter` call sites are a closed
  list; an `IImmutableRecord` entity may not declare a conflict policy that overwrites it

## Exit checklist

- [x] `dotnet build -c Release` — **0 warnings, 0 errors** solution-wide
- [x] `scripts/test.sh` — **420 passed, 0 failed** (189 unit, 25 architecture, 206 integration),
      identical across two consecutive runs after the review fixes
- [x] Domain + Application line coverage **96.4%** (1188 of 1232) — the §8 bar is 80%.
      `VumaRetail.Sync` is 79.2%, most of the remainder being the hosted-service loop
- [x] `SyncAndBackup` migration applied and **reversible** — up → down to `Identity` → up again, run
      locally exactly as CI's `migrate-check` does it. The re-apply succeeding is itself the proof the
      `Down` dropped the tables; the two empty schemas survive, which `EnsureSchema` has no reverse for
- [x] All four new routes in `/openapi/v1.json`, asserted against the live document
- [x] `sync` and `backup` permissions in the catalogue; nine new permissions, three high-risk
- [x] Every new command classified. `ReceiveSyncBatchCommand` claims the **existing**
      `ReadOnlyExemption.OfflineFlush` and the two backup commands claim `Backup` — the carve-out set
      is unchanged at three, and `CommandClassificationTests` still passes
- [x] `docs/SYNC_AND_BACKUP.md` written; its registry table checked against the actual `[Replicated]`
      attributes
- [x] `scripts/seed.sh` still builds the demo tenant — exercised by `dr-drill.sh`, which seeds before
      it snapshots
- [x] `scripts/dr-drill.sh` **run and passing**: 3 users, 3 roles, 2 stores restored into a database
      that had never had a schema, with the vault object confirmed to hold no readable tenant data
- [x] Two new architecture rules, **proven to fail on a deliberate violation** (see below)
- [x] `docs/PROGRESS.md` updated, ADR-046 to ADR-049 appended, committed and pushed

### Enforcement proven, not assumed

`AuditEntry` was given `ConflictPolicy.LastWriterWins`, and `SyncReceiver` was given a
`BypassTenantFilter` call. Both rules fired before the changes were reverted:

| Rule | Reported as |
|---|---|
| An immutable record never declares an overwriting conflict policy | `AuditEntry → LastWriterWins` |
| The tenant filter is bypassed only where it is meant to be | `src/VumaRetail.Sync/Protocol/SyncReceiver.cs:71 — using IDisposable crossTenant = tenant.BypassTenantFilter("deliberate violation");` |

The second rule found four **pre-existing** call sites on its first run — terminal certificate
lookup, refresh-token exchange, the design-time factory and the demo seeder. All four are legitimate
(a credential arrives carrying no tenant and is itself what identifies one), so they are now a named
closed list with the reason written down, which is what the rule is for.

### Two real defects the review found

Both came from the `sync-and-offline` agent, and both were confirmed before being fixed.

- **The outbox serialised each row *before* the audit stamp was applied.** `AuditInterceptor` runs at
  `SaveChanges`, which is after pipeline slot 300 — so every replicated row left its origin node with
  `created_by = ""` and `created_at = 0001-01-01`, and because those columns are written once and
  never again, every other tier stored the blanks permanently. R6 held only on the machine where the
  change was made. Confirmed by dumping a real payload before fixing it. `AuditStamper` now carries
  the stamping, is shared by the behaviour and the interceptor, and is idempotent per save. ADR-046.
- **One malformed operation took down a whole batch, permanently.** The receiver had no per-operation
  fault isolation, and the dispatcher re-selects the same oldest-stamped rows on every pass — so one
  bad payload would stop a store replicating for ever, with a queue depth that climbs and never falls
  as the only symptom. `DomainException` is now caught per operation and becomes a `Rejected` for that
  operation alone. ADR-047.

**The test that should have caught the first one was passing.** It asserted
`onStore.CreatedBy == onCloud.CreatedBy` while both harness nodes acted as the same principal — so it
compared two blanks and called it a match. The harness now gives each node a distinct principal and
the assertion names the expected value outright.

### Deviations from the plan, and why

- **`Infrastructure` now references `Sync`.** `VumaRetail.Sync` holds the protocol expressed against
  Application ports and nothing else — no EF, no HTTP, no S3 — so it sits below Infrastructure rather
  than beside it, and Infrastructure supplies its adapters. ADR-048.
- **`Entity` gained a `SyncStamp` column.** ADR-007 says every replicated row carries an HLC stamp
  and nothing carried one. `CLAUDE.md` §7 rule 3's column list does not name it; the ADR requires it.
  ADR-049.
- **A `backup` schema, separate from `sync`.** Replication and backup answer different questions, and
  a store with no cloud peer still takes backups. Sharing a schema would tie them together in the one
  place where separating them matters — a disaster recovery.
- **`DeleteRoleCommand` was added to Stage 02's identity module.** No command in the system soft-deleted
  anything, so §7 rule 8's rewrite had no path through a command and the replicated-delete path had no
  source to test against. Extending Stage 02 rather than working around it, as `CLAUDE.md` §1 requires.
- **The `stage-verifier` agent could not complete.** It stalled on its runner's watchdog partway
  through the test suite. Every box above was verified directly instead, and the commands are named so
  they can be re-run. `architecture-guard` and `sync-and-offline` both completed; the first reported
  PASS with zero violations, the second produced the two defects above.

## Explicitly deferred

- **The terminal SQLite cache and the terminal → store leg.** ADR-003's local cache belongs with the
  POS client in Stage 09; the protocol built here is tier-agnostic and the store server already
  serves the same receiver endpoint to a terminal that the cloud serves to a store.
- **mTLS on the sync channel.** The receiver authorises on a permission today. Which Kestrel port
  demands a client certificate is Stage 31 installer work, as `PROGRESS.md` §5 already records.
- **Real S3 credentials.** `S3BackupVault` is written against the AWS SDK and works with any
  S3-compatible endpoint; nothing in CI has a bucket, so `FileSystemBackupVault` is the tested
  default and the S3 path is logged under "Deferred — needs real credentials".
- **Continuous PITR / WAL archiving.** ADR-002 names it as available; snapshots are the R4 floor and
  the DR drill in Stage 31 decides whether WAL shipping is added on top.
- **The full DR drill.** Stage 31 owns it. This stage owns the restore path it exercises.
