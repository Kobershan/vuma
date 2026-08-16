# SYNC AND BACKUP — Vuma Retail

> The replication protocol (ADR-006, ADR-007) and the cloud backup path (R4). Written by Stage 04.
> **Every stage that adds a replicated entity updates §3.** That table is the registry the sync engine
> reads at runtime, and an entity missing from it is an entity that has quietly stopped replicating.

---

## 1. The chain

```
  Terminal  ──HTTPS/LAN──▶  Store server  ──mTLS + JWT──▶  Cloud
  (SQLite)                  (PostgreSQL)                   (PostgreSQL)
  ADR-003                   source of truth                source of truth
                            for the store (R2)             for the tenant (R2)
```

Three tiers, **one protocol**. A terminal posts a batch to its store server at exactly the endpoint a
store server posts one to the cloud, and the same receiver code runs at both ends. What differs is
each node's `NodeKind`, which is the only thing the authority conflict policies read.

The terminal leg is specified here and **is still not built**. Stage 09 shipped the server half of
POS — the domain, the API and the hardware layer — but no terminal-local SQLite cache and no outbound
queue: `Microsoft.Data.Sqlite` appears nowhere in `src/`, and `VumaRetail.Desktop` is unstarted. This
paragraph previously said the leg was "built in Stage 09, with the POS client that needs it"; the
client is what did not arrive, so the leg went with it. It belongs to whoever builds the desktop POS
client (Stage 08b plus `VumaRetail.Desktop`), and until then **the three-tier claim in this document
is a specification, not a description**. The store↔cloud leg is built and tested.

### Nodes

Every node has a stable id, configured once by the installer at `Vuma:Sync:Node:NodeId`:

| Kind | Id | Notes |
|---|---|---|
| `Terminal` | `terminal:{id}` | A till or back-office machine (ADR-003) |
| `Store` | `store:{id}` | The in-store server. One per shop |
| `Cloud` | `cloud` | The cloud tier |

It cannot be derived. A machine fingerprint would not survive a restore onto new hardware; a database
row would not survive a database restore; a default would collide the moment there were two stores.
The host refuses to start without one, because the node id is the final tie-break in every HLC stamp
(§2) and two nodes sharing one produce stamps that cannot be ordered.

---

## 2. Ordering: the hybrid logical clock (ADR-007)

Every replicated row carries a stamp in `sync_stamp`, and every operation carries the same stamp.

```
{physicalMilliseconds:D14}:{counter:D6}:{nodeId}
      01754000000123      :   000042    : store:jhb01
```

Compared physical, then counter, then node id — which makes the order **total**, not merely partial.
That matters: two nodes that independently stamp the same millisecond still agree on which came
first, without talking to each other. A last-writer-wins rule over a partial order resolves
differently depending on which node evaluates it, which is a system that converges on two different
answers and calls itself converged.

The string form is zero-padded so **an ordinal sort matches the comparison**. The outbox is drained
with `ORDER BY operation_stamp` and a cursor is one indexed column; drop the padding and nothing
throws, sync just starts shipping in the wrong order.

Two cases the clock exists for, and neither is exotic:

| Situation | Plain timestamps | HLC |
|---|---|---|
| NTP corrects a drifted store server **backwards** | The node re-issues values it has already used; last-writer-wins silently reverts every edit made during the overlap | Physical time stops moving, the counter carries the order, stamps keep increasing |
| A peer's change arrives stamped **ahead** of anything this node can issue | This node keeps stamping *behind* a change it has already applied, so its own next edit loses to it — the change comes back from the dead on the next sync | `Observe` pulls this node past the remote stamp once, and the anomaly ends |

**A stamp is not a timestamp and no business rule may read it as one.** When something happened is
`occurred_at`, from `IClock`.

---

## 3. The replication registry

Declared on the entity with `[Replicated(scope, conflictPolicy)]`, in the same diff that creates it.
An architecture test fails the build on an entity without one — `NodeLocal` is a valid answer and
silence is not.

### Scopes

| Scope | Meaning |
|---|---|
| `NodeLocal` | Never leaves the node that wrote it |
| `StoreLocal` | Terminal ↔ store server only |
| `StoreToCloud` | Store → cloud, one way. Trading data |
| `CloudToStore` | Cloud → store, one way. Tenant configuration, pricing policy, licences |
| `Bidirectional` | Originates at either tier and converges. The expensive case |

### Conflict policies

| Policy | Settled by | Use for |
|---|---|---|
| `LastWriterWins` | The higher HLC stamp | Rows where the latest edit is simply the right one |
| `StoreWins` | Authority, not order — whichever node is the store | Anything the store operates and the cloud observes |
| `CloudWins` | Authority — whichever node is the cloud | Tenant configuration, pricing policy, licensing |
| `AppendOnly` | Nothing is overwritten; entries accumulate | Stock ledger, loyalty ledger, journals, the audit trail (ADR-005) |
| `ManualReview` | A person, from a queue holding **both** versions | Genuine business conflicts — two stores editing one supplier |

The awkward case, and the one a spot-check misses: an authority policy evaluated **on** the
authoritative node. `StoreWins` on the store server means the *local* row wins, so an inbound change
from the cloud is refused. Both directions have to be answered, and the resolver's tests enumerate
all five policies × both tiers × all three stamp orderings rather than sampling them.

### The registry as built (Stage 04, extended Stage 04b, 06, 07, 08 and 09)

| Entity | Schema | Scope | Policy | Why |
|---|---|---|---|---|
| `Tenant` | `platform` | `CloudToStore` | `CloudWins` | Tenant configuration is head office's |
| `Store` | `platform` | `Bidirectional` | `CloudWins` | A shop edits its own details; head office overrules |
| `AuditEntry` | `platform` | `StoreToCloud` | `AppendOnly` | Immutable (R6) — see the rule below |
| `User` | `identity` | `Bidirectional` | `CloudWins` | Staff are created at either end |
| `Role` | `identity` | `CloudToStore` | `CloudWins` | Head office decides what a role grants |
| `RolePermission` | `identity` | `CloudToStore` | `CloudWins` | Follows its role |
| `UserRoleAssignment` | `identity` | `CloudToStore` | `CloudWins` | Follows its role |
| `Terminal` | `identity` | `StoreToCloud` | `StoreWins` | A till belongs to the shop it is standing in |
| `RefreshToken` | `identity` | `NodeLocal` | — | A session on one node is not a session on another |
| `OutboxMessage` | `sync` | `NodeLocal` | — | Replicating the outbox would replicate replication, for ever |
| `InboxMessage` | `sync` | `NodeLocal` | — | What one node has seen is that node's business |
| `SyncCursor` | `sync` | `NodeLocal` | — | Per-node bookkeeping |
| `ConflictEntry` | `sync` | `StoreToCloud` | `LastWriterWins` | A queue one machine can see is a queue nobody reviews |
| `BackupSnapshot` | `backup` | `StoreToCloud` | `LastWriterWins` | Backup health is what a multi-store operator needs |
| `Activation` | `licensing` | `NodeLocal` | `LastWriterWins` | A hardware binding belongs to the one machine it was captured on |
| `Lease` | `licensing` | `NodeLocal` | `AppendOnly` | The signed token this node is currently running on; each node holds its own |
| `Licence` | `licensing` | `NodeLocal` | `AppendOnly` | The store server holds the licence; terminals take sub-leases over the LAN rather than a replica (`LICENSING.md` §2) |
| `EmergencyUnlock` | `licensing` | `NodeLocal` | `LastWriterWins` | Issued by the vendor to one installation's licence screen |
| `TamperFlag` | `licensing` | `NodeLocal` | `LastWriterWins` | Reported to the vendor over the heartbeat channel (`API_CONTROL_PLANE.md` §2), not the tenant's own store↔cloud replication |
| `ClockWatermark` | `licensing` | `NodeLocal` | `LastWriterWins` | The highest wall clock *this* installation has seen |
| `MeteringRecord` | `licensing` | `NodeLocal` | `LastWriterWins` | Delivered to the vendor over the heartbeat channel, not synced to the tenant's own cloud replica |
| `SupportGrant` | `licensing` | `Bidirectional` | `LastWriterWins` | Consent can be granted or revoked from the store back office or the cloud-facing admin app; both must converge |
| `UnitOfMeasure` | `catalog` | `Bidirectional` | `CloudWins` | Configuration a store may add to locally; head office reconciles a collision the same way it does for `Store` |
| `Item` | `catalog` | `Bidirectional` | `CloudWins` | A store may raise a new line at the till counter as easily as head office can; the cloud is where a multi-store catalogue is kept consistent |
| `ItemVariant` | `catalog` | `Bidirectional` | `CloudWins` | Follows its item |
| `Barcode` | `catalog` | `Bidirectional` | `CloudWins` | A store attaching a legacy or case-pack code locally is routine; the cloud settles a genuine collision |
| `Partner` | `partners` | `Bidirectional` | `CloudWins` | Suppliers and customers are onboarded at either tier; head office is where a multi-store estate reconciles a duplicate |
| `Account` | `finance` | `CloudToStore` | `CloudWins` | The chart of accounts is head office's; a store never invents a GL account (§7 rule 12) |
| `AccountingPeriod` | `finance` | `CloudToStore` | `CloudWins` | Opening and closing a period is a head-office act |
| `PostingRule` | `finance` | `CloudToStore` | `CloudWins` | The rules engine that turns a module's financial event into a journal is configured centrally (ADR-016) |
| `PostingRuleLine` | `finance` | `CloudToStore` | `CloudWins` | Follows its rule |
| `TaxRule` | `finance` | `CloudToStore` | `CloudWins` | Tax is a rules engine, not a constant (ADR-014); the rate a store charges is not the store's to set |
| `DocumentNumberCounter` | `finance` | `NodeLocal` | `LastWriterWins` | **Deliberately not replicated as a value**, for the same reason as `StockBalance`: two nodes sharing a counter would issue the same document number twice |
| `Journal` | `finance` | `StoreToCloud` | `AppendOnly` | A posted journal is immutable (§7 rule 7); amendment is a reversing entry |
| `JournalLine` | `finance` | `StoreToCloud` | `AppendOnly` | Follows its journal, and is immutable for the same reason |
| `ArInvoice` | `finance` | `StoreToCloud` | `StoreWins` | Raised where the sale happened; mutable until posted |
| `ArInvoiceLine` | `finance` | `StoreToCloud` | `StoreWins` | Follows its invoice |
| `ArReceipt` | `finance` | `StoreToCloud` | `AppendOnly` | Money that changed hands. Immutable, so it accumulates |
| `ArReceiptAllocation` | `finance` | `StoreToCloud` | `AppendOnly` | Follows its receipt, and is immutable for the same reason |
| `ApInvoice` | `finance` | `StoreToCloud` | `StoreWins` | Captured against the store that received the goods |
| `ApInvoiceLine` | `finance` | `StoreToCloud` | `StoreWins` | Follows its invoice |
| `ApPayment` | `finance` | `StoreToCloud` | `AppendOnly` | Money that changed hands. Immutable |
| `ApPaymentAllocation` | `finance` | `StoreToCloud` | `AppendOnly` | Follows its payment |
| `BankAccount` | `finance` | `StoreToCloud` | `StoreWins` | The account a shop banks into is the shop's to maintain |
| `BankStatementLine` | `finance` | `StoreToCloud` | `StoreWins` | Imported at the store that owns the account |
| `ReconciliationVarianceFlag` | `finance` | `StoreToCloud` | `AppendOnly` | A raised flag is a record that it was raised; clearing it is a new row |
| `StockLocation` | `inventory` | `Bidirectional` | `CloudWins` | A store names its own back room; head office adds a distribution centre. Same shape as `Store` |
| `StockLedgerEntry` | `inventory` | `StoreToCloud` | `AppendOnly` | Stock physically moves at the store and the ledger accumulates (ADR-005) |
| `StockBalance` | `inventory` | `NodeLocal` | `LastWriterWins` | **Deliberately not replicated as a value** (ADR-069): a running total is not safely mergeable, so each node rebuilds its own from the entries it applied |
| `StockTransfer` | `inventory` | `StoreToCloud` | `AppendOnly` | Written once, when both its ledger entries already exist, and never edited |
| `StocktakeSession` | `inventory` | `StoreToCloud` | `StoreWins` | The count happens on one store's floor; the cloud observes the result |
| `StocktakeLine` | `inventory` | `StoreToCloud` | `StoreWins` | Follows its session |
| `TillSession` | `pos` | `StoreToCloud` | `StoreWins` | A shift happens at one drawer in one shop; two stores can never contend over the same till |
| `Sale` | `pos` | `StoreToCloud` | `StoreWins` | Rung up in one place, frozen once it completes. `StoreWins` rather than `AppendOnly` because a sale is legitimately mutable while open and only the store can be editing it |
| `SaleLine` | `pos` | `StoreToCloud` | `StoreWins` | Follows its sale |
| `SaleTender` | `pos` | `StoreToCloud` | `AppendOnly` | Money that changed hands. Immutable, so it accumulates and never overwrites |
| `ReceiptPrint` | `pos` | `StoreToCloud` | `AppendOnly` | A print log a later write can overwrite is not a log |

**The `finance` and `inventory` rows above were both added retrospectively, and the second correction
found the first one incomplete.** Stage 09 added the six `inventory` rows, which Stage 08 had declared
as `[Replicated]` attributes and recorded in `docs/DATA_MODEL.md` §5 but never carried into this
table. The Stage 09 review then found that **Stage 07 had drifted in exactly the same way and by
three times as much** — 19 `finance` entities declared in code and absent here — which the heading
above recorded by omitting 07 from its own list of stages. So this table was 25 entities short of the
code across two stages, not six across one.

The attributes are the source of truth and the architecture test enforces them; this table is the
prose copy. Nothing changed in Stage 07's or Stage 08's behaviour, and nothing here is a defect in
either stage's code. The lesson is that a hand-maintained prose copy of a machine-enforced fact drifts
silently and is only ever caught by someone diffing the two — which is worth doing at the end of every
stage that declares a replicated entity, not once a module.

Stage 06 (master data). None of the five `catalog`/`partners` entities is immutable — `Item`,
`ItemVariant`, `Partner` and `UnitOfMeasure` deactivate rather than delete (§7 rule 8), and `Barcode` is
the one entity in the module that is hard deleted (`docs/DECISIONS.md` ADR-061) — so `CloudWins` is a
legitimate policy for all five; nothing here collides with the immutable-record rule two paragraphs below.

**An immutable record may not declare a policy that overwrites it.** `AuditInterceptor` throws on any
update to an `IImmutableRecord` (§7 rule 7, ADR-012), so an immutable entity declaring
`LastWriterWins` would be one the receiver is obliged to overwrite and the persistence layer is
obliged to refuse — a store whose replication starts throwing the first time two nodes touch one
invoice. An architecture test enforces it.

---

## 4. Capture: the transactional outbox (ADR-006)

`OutboxBehaviour` sits in pipeline slot **300**, which is *inside* slot 200's transaction. It runs
after the handler and before the commit, so **the outbox row and the change it describes are one
atomic write**. A crash between the two would leave a store silently diverged from the cloud with
nothing in any log to say so, which is the failure ADR-006 exists to make impossible.

```
Logging(0) → ReadOnlyGuard(50) → Validation(100) → Transaction(200) → Outbox(300) → handler
                                                   └──────── one transaction ────────┘
```

**Modules never write outbox rows.** Everything is derived from the EF change tracker and the
entity's own declaration. A module that could opt in could also forget to, and a forgotten capture is
a table that quietly stopped replicating — discovered months later, when head office's stock figures
are wrong.

Each capture:

1. issues an HLC stamp and writes it to **both** the row (`sync_stamp`) and the operation;
2. serialises the row to JSON — excluding `row_version` (node-local, ADR-035), `sync_state` and
   `sync_stamp`;
3. writes an `OutboxMessage` with a fresh UUID v7 `operation_id`.

A soft delete is captured as a `Delete`. The behaviour detects it the same way `AuditInterceptor`
does — by `deleted_at` being newly set — because the interceptor's rewrite happens at `SaveChanges`,
which is *after* this behaviour runs. The two must agree, or the audit trail and the outbox would
disagree about what happened to a row.

**Queries capture nothing.** A report must not pay for replication.

---

## 5. Delivery

`OutboxDispatcher` is a background service. It is deliberately a separate component from anything on
a sale path: **R1 is not satisfied by making replication fast, it is satisfied by making it
structurally unable to block a sale.** A till writes its outbox row inside its own transaction and is
finished; whether the cloud is reachable is the dispatcher's problem and the till never learns the
answer.

One pass = take what is due → mark in flight → send → mark the results → advance the cursor, all in
one transaction. Failure is expected rather than exceptional — a shop's ADSL drops, the cloud is
being deployed — so a failed pass logs at **warning**, backs the rows off, and comes round again.

| Setting (`Vuma:Sync:Dispatcher`) | Default | |
|---|---|---|
| `Enabled` | `false` | Off where there is no peer |
| `BatchSize` | 200 | |
| `IdleInterval` | 15 s | After an empty poll |
| `InitialBackoff` | 5 s | Doubles per attempt |
| `MaxBackoff` | 10 min | Capped — see below |

**A failed row never gives up.** The backoff caps rather than escalating for ever, because a store on
a bad line must catch up on its own once the line returns. What is bounded is the *rate*, not the
number of attempts. The one exception is a row the peer explicitly `Rejected`: retrying is pointless,
so it waits at `MaxBackoff` with the reason on it rather than being dropped.

A batch carries at most **500** operations (`ReceiveSyncBatchCommandValidator`). The whole batch runs
in one transaction, and a peer that sent a hundred thousand at once would hold a write transaction
open on a trading store for as long as it took — a denial of service that looks like a slow network.

---

## 6. Receipt: the idempotent inbox

`POST /api/v1/sync/batches`, gated on `sync.batch.push`. Everything about the receiver follows from
one assumption: **every batch may arrive more than once, in any order, arbitrarily late** — not as an
error path, as the normal way a store with bad connectivity catches up.

Per operation, in stamp order:

1. **Fold the stamp** into this node's clock (§2) — even for an operation about to be rejected.
   Causality is a property of what this node has *seen*, not of what it chose to keep.
2. **Deduplicate** on `(tenant_id, source_node, operation_id)`. A replay answers `Duplicate`, which is
   a success — a receiver that answered `409` would turn every retry into a support call.
3. **Resolve the type** from a closed set built at startup. Never `Type.GetType`: a receiver that
   materialised whatever type name a peer sent would be a deserialisation gadget with an HTTP endpoint
   in front of it.
4. **Resolve the conflict** from *this node's* `[Replicated]` declaration. The sender gets no say —
   a sender that could nominate a policy could overwrite any row on this node.
5. **Apply**, keeping the sender's stamp and origin. A receiver that re-stamped would make every
   applied change look newer than the original, and the next comparison would undo it.
6. **Record** the receipt and, at the end of the batch, advance the inbound cursor.

The whole batch commits together or not at all, so a half-committed batch cannot leave inbox rows
claiming operations were processed that were not.

### The guarantees, and where they live

| Guarantee | Enforced by |
|---|---|
| Exactly-once *effect* | The unique index on `(tenant_id, source_node, operation_id)` — not the pre-check, which is check-then-act and which two concurrent deliveries both pass |
| A duplicate inside one batch applies once | The change tracker, because neither copy has reached the database yet |
| A node never writes another tenant's data | The receiver refuses a batch whose tenant is not the caller's, outright — a refusal rather than a filter, because a batch that named the wrong tenant is a bug or an attack |
| A deleted row is not resurrected by a late upsert | The receiver reads its own tombstone (`IgnoreQueryFilters` on the soft-delete half only; the tenant predicate is written back by hand) |
| Replication does not loop | `IReplicationScope` records the ids the receiver wrote; the outbox skips them |
| One bad operation does not block the rest | Per-operation `DomainException` handling: a malformed payload becomes a `Rejected` for **that** operation. Without it, the dispatcher re-selects the same oldest-stamped rows every pass and one bad row stops a store replicating for ever |
| A replicated row keeps its origin | The outbox behaviour stamps the audit columns **before** it serialises. See below |

**On stamping order.** `AuditInterceptor` runs at `SaveChanges`, which is *after* pipeline slot 300.
An outbox payload built first therefore carried `created_by = ""` and `created_at = 0001-01-01` — and
because those columns are written once and never again, every other tier stored those blanks
permanently. R6 was satisfied only on the machine where the change was made, which is the one place
nobody needs to ask. `AuditStamper` is now shared by the behaviour and the interceptor and is
idempotent per save, so the behaviour stamps first and serialises a complete row.

A row applied from a peer keeps that origin: the stamper skips it, because
`IReplicationScope.AppliedRowIds` says a peer wrote it. The audit *entry* for the same save still
records this node's own principal — **the row says who changed the record, the trail says what this
machine did about it**, and both questions get asked in an investigation.

### Known limitation: a genuinely concurrent duplicate

The pre-check is check-then-act; the unique index is the guarantee. Two *simultaneous* deliveries of
one operation therefore end with one of them failing the whole batch on a `DbUpdateException` rather
than answering a per-row `Duplicate`. It is self-healing — the sender retries and the second attempt
sees the row committed and gets a clean `Duplicate` — and it cannot double-apply anything, so no data
is at risk. What it costs is a wasted retry and a noisy log on a race that the dispatcher's
single-threaded-per-peer design makes rare. Making it graceful needs a savepoint per operation, which
is a real cost on every batch to smooth a rare path; that trade is Stage 31's to revisit.

**On the loop prevention specifically.** The obvious design is a flag that is true while a batch is
applying. It does not work: the outbox behaviour captures *after* the handler returns, by which time
a `using` region inside the handler has closed. Recording row ids is both correct and the more
truthful model — what must not be re-captured is a specific set of rows, not everything written near
them.

### Outcomes

| Outcome | Means | Sender should |
|---|---|---|
| `Applied` | Written | Nothing |
| `Duplicate` | Already seen | Nothing — this is the retry working |
| `Conflicted` | Not applied: superseded by policy, or in the review queue (`conflictId` set) | Nothing; a person may act |
| `Rejected` | Refused: unknown entity, node-local entity, unparseable payload | Investigate. The sender has a bug |

Only `Rejected` means the sender should look at anything.

---

## 7. The conflict review queue

`ManualReview`, and an authority policy where neither node is the authority, open a `ConflictEntry`
holding **both versions in full**. The local row is left unchanged: the node that detected the
conflict is the node whose users are looking at the row, and changing it under them to a version
nobody has approved is worse than leaving it alone.

`POST /api/v1/sync/conflicts/{id}/resolve` takes `KeptLocal` or `TookRemote` **and a mandatory note**.
This is the one place in Vuma where a person overrules the system about which version of a business
record is real; six months later somebody will ask why a supplier's payment terms are what they are,
and "a human chose the store's version" answers nothing on its own.

Choosing `TookRemote` applies the remote version from the copy the entry kept — which is why both
versions are stored in full rather than as a diff. By the time anybody reviews it, the batch it came
from is long gone. Unlike the receiver, that write **is** captured for replication: a person's
decision is a local decision this node made, and the other tiers have to hear about it or the same
conflict is raised again on the next change.

---

## 8. Backup and restore (R4)

R4 is not "backups are taken". It is *"store burns down → new box, run restore, back trading"*, which
is a claim about a **restore** and can only be supported by having done one.

```
pg_dump -Fc  →  AES-256-GCM  →  vault (S3 / filesystem)  →  SHA-256 recorded
                                                              │
restore:  checksum confirmed  →  decrypt  →  pg_restore --clean --if-exists  →  ledger stamped
```

**Encryption.** AES-256-GCM in 1 MiB framed chunks — GCM is one-shot and will not stream, and a
snapshot does not fit in memory. The chunk index is authenticated as associated data, so chunks
cannot be reordered, dropped or duplicated while their individual tags still verify; a zero-length
authenticated terminator catches truncation. Authenticated encryption is the requirement rather than
a preference: a tampered snapshot must fail to decrypt, not restore into plausible nonsense that a
shop discovers at the till.

**The checksum is of the ciphertext**, so the cloud tier can confirm an object is intact without
holding a key that decrypts a tenant's data — the posture ADR-024 sets out.

**The ledger row is written before the dump starts.** A run that only recorded itself on success
would make a crashed backup indistinguishable from a backup nobody scheduled, and those want very
different phone calls. `verified_at` and `restored_at` are the two fields that matter and the two
usually missing from a backup UI: "last backup 03:00" is not evidence of anything, "last restored
into a scratch database on Tuesday" is.

**A restore confirms the checksum before a single byte moves.** A restore that proceeded on a
corrupted object would produce a database that looks restored and fails at the till — worse than not
restoring, because the shop would stop looking for the real backup.

### Vaults

| Provider | Status |
|---|---|
| `FileSystem` | Built and tested. Pointed at a NAS it is a working target for a single-store customer with no cloud subscription, and it is what the drill exercises on every CI run |
| `S3` | Built against the AWS SDK; works with AWS S3, Backblaze B2, Wasabi and MinIO via `ServiceUrl` + path-style. **Untested** — nothing in this repository has a bucket. See `docs/PROGRESS.md` "Deferred — needs real credentials" |

### Running it

```bash
scripts/backup.sh                                  # snapshot now
scripts/restore.sh "<target connection string>"    # restore the latest completed snapshot
scripts/restore.sh "<target>" <snapshot-id>        # restore a specific one
scripts/dr-drill.sh                                # the whole thing, end to end, on a throwaway cluster
```

The restore target is a required argument with no default. A restore replaces a database, and a
command that defaulted to the configured connection string would put "practise the restore" and
"destroy the live store" one typo apart.

`dr-drill.sh` seeds a store, snapshots it, confirms the object holds no readable tenant data,
verifies the checksum, restores into a database that has never had a schema, and compares row counts.
It is also asserted in `BackupTests.A_snapshot_restores_into_an_empty_database`, so it runs on every
CI run rather than only when somebody remembers.

### Configuration

| Key | |
|---|---|
| `Vuma:Backup:Vault:Provider` | `FileSystem` or `S3` |
| `Vuma:Backup:Vault:Directory` | Filesystem vault root |
| `Vuma:Backup:Vault:Bucket` / `ServiceUrl` / `Region` / `AccessKeyId` / `SecretAccessKey` | S3 |
| `Vuma:Backup:Encryption:Key` | 256-bit base64. **Required** — a snapshot is not written unencrypted |
| `Vuma:Backup:Postgres:ToolDirectory` | Where `pg_dump`/`pg_restore` live. Empty means `PATH` |

Key custody is deliberately unresolved. The floor is that the key is configuration and is *not* the
vendor's storage credential, so a compromise of the bucket is not a compromise of the data. Real
custody — a KMS or HSM — belongs with Stage 04b's licence signing key, which has the same problem and
should get the same answer once.

---

## 9. Permissions

| Permission | Held by |
|---|---|
| `sync.batch.push` | **A peer node's service credential, and nobody who signs in with a password.** It writes any replicated row under any conflict policy without passing a business rule |
| `sync.status.view` | Back office |
| `sync.conflict.view` / `sync.conflict.resolve` | Back office; resolve is high-risk |
| `backup.snapshot.view` / `create` / `verify` | Back office |
| `backup.snapshot.restore` | **Its own permission, not folded into `manage`.** A restore replaces a database — grant it to whoever runs the drill, not to everyone who can press "back up now" |

---

## 10. Telemetry

`CLAUDE.md` §4's OTLP sink ships in Stage 04, configured at `Vuma:Telemetry:Otlp:Endpoint` and **off
unless set**. That default is not laziness: telemetry leaving a tenant's premises is a POPIA question,
R10 caps what may ever leave for vendor purposes at counts and health, and application logs are
neither. The intended destination is the *tenant's own* collector. Vendor telemetry is Stage 30b's
metering payload — a whitelist of aggregate counters — and does not travel this pipe.

The same redaction runs before the OTLP sink as before the file, so a secret is masked once,
centrally, on its way to every destination including the one that leaves the building.

---

## 11. What a later stage owes this document

Adding a replicated entity means:

1. `[Replicated(scope, policy)]` on the entity, chosen deliberately — an architecture test fails the
   build without it, and `NodeLocal` is a valid answer.
2. A row in §3's table.
3. If the entity is an `IImmutableRecord`, `AppendOnly` or `ManualReview` — nothing that overwrites.
4. A test that a change to it is captured, and one that it applies at the far end. The generic
   machinery is tested; what is not tested is that *your* entity's payload round-trips.

---

## 12. Deferred

| | Owner |
|---|---|
| Terminal ↔ store leg and the SQLite outbound queue (ADR-003) | **Still deferred after Stage 09** — belongs with the desktop POS client (08b + `VumaRetail.Desktop`), which is what did not arrive. See §2 |
| A convergence test across all three tiers | Same owner. The existing tests build `Cloud` and `Store` harnesses only; `NodeKind.Terminal` never appears as a replicating node, and no `pos` entity appears in any replication test |
| Cloud → sibling-store fan-out (the cloud relaying one store's change to another) | Stage 06+, when there is master data worth fanning out |
| mTLS on the sync channel — which Kestrel port demands a client certificate | Stage 31 (installer) |
| Continuous PITR / WAL archiving on top of full snapshots | Stage 31, after the DR drill decides whether snapshots alone are enough |
| Retention and pruning of dispatched outbox rows and old snapshots | Stage 31 |
| Real S3 credentials | Deferred; see `docs/PROGRESS.md` |
