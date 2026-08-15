# STAGE 04b — Licensing, Activation & Entitlement

**Status:** DONE (2026-08-11) · **Depends on:** 04 · **Reference reading:** `docs/LICENSING.md` (all of it), `docs/API_CONTROL_PLANE.md` §2, `docs/DECISIONS.md` ADR-028 (the live decision; ADR-023 and ADR-027 are superseded and must not be implemented)

## Objective
The client and tenant half of the SaaS model: activation against a licence key, a signed monthly
licence refreshed by lease, entitlement gating that every later module plugs into, metering collection,
and the enforcement ladder that ends in **read-only** when a subscription lapses. The vendor half
(console, billing, dunning, abuse queue, unlock issuance) is Stage 30b.

Read-only itself is easy to build. Almost all of the engineering here is in making sure it can **only
ever be deliberate**, and in making sure read-only is genuinely *complete* on the read side — see
ADR-028 and `LICENSING.md` §1 and §4.

Built here, early, because every module stage from 06 onward gates on entitlements and reports usage.

## Deliverables

**Licence domain (`licensing` module)**
- `Licence` — signed payload: tenant, stores, entitlements, limits, issue/expiry, fingerprint hash,
  nonce, monotonic issuance counter. Ed25519 verification against a **pinned public key** compiled into
  the binaries.
- `Lease` — 72-hour signed token derived from the current licence; what the running software actually
  validates. Refreshed on start and every 24 hours.
- `Activation` — hardware fingerprint capture (composite, weighted, N-of-M tolerant per
  `LICENSING.md` §3), stored as a salted hash. Raw hardware identifiers are never persisted or
  transmitted in the clear.
- Licence key format with checksum so a typo fails locally before a network call
- Tamper detection: DPAPI-protected licence store plus a shadow copy (disagreement = flag), persisted
  monotonic "highest wall-clock seen" (backwards clock = flag), assembly integrity self-check

**Entitlement service**
- `IEntitlementService` — the single choke point. `IsModuleEnabled(module)`, `CheckLimit(limitType,
  proposedValue)`, `CurrentEnforcementLevel()`.
- Module manifests declare their licence flag; navigation, endpoints and desktop commands all gate
  through this service. **Architecture test fails the build if a module gates itself any other way.**
- Hard limits (stores, terminals, named users, modules) checked at configuration time only — never in
  a transaction path. Soft limits (transactions, storage, API calls) meter and warn, never block.
- Unlicensed module endpoints return 403 with a stable `LICENCE_MODULE_NOT_ENABLED` code and an
  upgrade reference, so the UI can offer the right next step instead of a dead end.

**Enforcement ladder**
- Implement Path A (cannot verify) and Path B (non-payment) exactly as tabled in `LICENSING.md` §4.
  Both are vendor-configurable per plan and per tenant; ship the documented defaults.
- `Normal → Notice → ReadOnly`. Hard lockout exists only as a manual vendor action for confirmed abuse
  and is never reached automatically.
- **ReadOnly**: implemented as a single interceptor in the command pipeline, not as scattered checks.
  Every command declares itself read or write; writes are refused with `403 LICENCE_READ_ONLY` carrying
  the amount due and a payment link. Queries pass untouched. **A command with no declared side-effect
  classification fails the build** — that is how a future module cannot accidentally stay writable.
- Three explicit exemptions that stay writable in read-only, and they are exemptions in one reviewable
  list: the payment and payment-method-update commands, the outbound flush of already-captured offline
  data, and the backup job.
- The licence screen shows status, amount due, a working payment button, "retry now", emergency-code
  entry and the support number.
- **Unlock is immediate and automatic:** payment lands → next heartbeat or "retry now" → unlocked in
  under 60 seconds, no reactivation step, no vendor intervention.
- **Open-session carve-out** (default on, vendor-configurable off): if lockout falls due while a till
  has an open sale or an unclosed cash-up, that sale and that cash-up complete, then the terminal locks.
  No new sale can start. This exists so a lockout does not create a drawer of unrecorded cash and a
  queue of customers holding goods.
- A single `IEnforcementPolicy` decides the level and what it blocks, so the whole behaviour is one
  reviewable list rather than scattered checks

**Vendor unlock mechanisms (client side)**
- **Emergency write code** — signed, single-tenant, single-use, time-limited (default 72h). Entered on
  the licence screen and **works with no internet at all**. Verified against the pinned public key, so
  it cannot be forged. Consumed codes are recorded and reported at the next heartbeat.
- **Write unlock** — a vendor-granted, time-limited restoration of write access, applied from the
  control plane and effective at the next heartbeat or pushed immediately when online.
- **Grace extension / courtesy month** — applied by the vendor, effective at the next heartbeat or
  pushed immediately when the store is online.

**Device API client**
- Activation, rebind, lease refresh, heartbeat and daily metering against `docs/API_CONTROL_PLANE.md`
  §2, over mTLS, all idempotent and resumable after any length of outage
- Heartbeat payload: install id, boot id, fingerprint, monotonic counter, terminal counts, sync lag,
  outbox depth, last verified backup, version, integrity status, error counts
- Command handling from the heartbeat response: `refresh_lease`, `update_now`, `run_backup`,
  `collect_diagnostics`, `set_channel`, `deactivate`, `revoke_support_access`
- Store server holds the licence; terminals receive **sub-leases** over the LAN, so a till never needs
  internet of its own

**Metering collection**
- Daily rollup job producing counts and health only, idempotent by `(nodeId, period)`, queued and
  re-sendable
- **Privacy enforcement in code:** the metering payload is built from a whitelist of aggregate counters.
  A test asserts no field is sourced from a business table and that the serialised payload contains no
  free text, name, or document content.

**Support access (tenant side)**
- Tenant admin receives, approves and can revoke a time-boxed vendor support grant
- While active: a persistent banner in the tenant's own UI, and every vendor action written to the
  **tenant's** audit log as well as the vendor's
- No route exists to tenant business data without an active grant — build it this way now; consent
  cannot be retrofitted

**First-run and UI**
- Activation wizard: licence key → fingerprint → activate → confirmation, with clear errors for
  already-activated, subscription-inactive and invalid-key cases
- Licence status screen: plan, entitlements, limits vs current usage, expiry, last heartbeat,
  enforcement level, and a "retry now" button
- Warnings that are calm and actionable, never alarming to a cashier mid-queue

## Business rules
- **A lockout may only be caused by a known subscription state.** Never by a network fault, never by a
  vendor-side outage, never by a single failed charge, never by a clock change, never by a hardware
  change within fingerprint tolerance. Every one of these has a test.
- **Control plane unreachable ≠ unlicensed.** The lease authorises; the network only refreshes it. A
  timeout, a 500, or a garbage response is treated as unreachable. If the vendor's control plane goes
  down, every customer keeps trading to their lease's natural expiry.
- **Path B requires completed dunning.** A failed charge alone never locks anyone out; the dunning
  cycle must have completed and its notifications must be recorded as delivered.
- **Read access is never restricted by licensing.** Reports, exports, reprints and dashboards work in
  every state. If a query path ever consults `IEntitlementService` for the enforcement level, that is a
  bug.
- **The customer must always be able to pay from inside the product.** The payment and card-update
  screens are writable in read-only. Assert it.
- **Already-captured data is never stranded.** Queued offline sales still flush; backup still runs.
- **A DR restore does not bypass licensing.** The restore auto-issues a rebind (`LICENSING.md` §3) so
  disaster recovery is never blocked by an activation error — but entitlement comes from the control
  plane, so a suspended tenant restores into a locked state. A restored *old* lease is caught by the
  monotonic issuance counter and cannot be replayed to extend access.
- Clock manipulation buys nothing: the monotonic highest-seen timestamp means setting the clock back
  cannot extend a lease, and is itself reported as a tamper flag rather than triggering a lockout.
- Emergency codes cannot be forged without the licence signing key, cannot be reused, and expire on
  time even with no connectivity.

## Tests / acceptance
- Signature verification: a licence signed by the wrong key is rejected; a tampered payload is rejected
- Fingerprint tolerance: change the NIC → same machine; change motherboard + disk + machine GUID →
  rebind required. Table-driven across the weighting.
- Full ladder simulation on a virtual clock for both paths: assert the exact behaviour at every
  boundary, and that a single successful heartbeat one day before lockout resets to Normal
- **No-accidental-lockout suite** (see `docs/TESTING.md` §7 — this is the one that matters):
  control plane unreachable / erroring / returning garbage → trades to the configured boundary and not
  a minute earlier; failed charges without completed dunning → no lockout; clock moved in either
  direction → flagged, never locked; hardware change within tolerance → no lockout
- **Read-only correctness suite** (see `docs/TESTING.md` §7): full report catalogue sweep succeeds;
  every write across every module is refused with the right code, generated from the module manifests;
  the three exemptions work; public API reads serve and public API writes return a neutral 503
- Recovery latency: payment recorded → full access within 60 seconds of the next heartbeat, no manual step
- Emergency write code: unlocks with networking fully disabled, expires exactly on time, rejects reuse,
  rejects a code signed with the wrong key, rejects another tenant's code
- Open-session carve-out: in-progress sale and cash-up complete; a new sale is refused
- Restore of a suspended tenant lands in the locked state; a replayed old lease is rejected
- Hard limit: registering terminal 11 on a ten-terminal plan fails with a clear message; the tenth
  succeeds
- Soft limit: exceeding the transaction allowance meters and warns but never blocks
- Clock rollback produces a flag and no extension
- Metering payload privacy test (schema whitelist + no business-table fields)
- Offline for 45 days then reconnect: metering for every missed day arrives exactly once
- Control plane switched off for a full simulated trading day: zero customer impact

## Exit checklist
- [x] Activation works end to end from a licence key on clean hardware
- [x] Lease refresh, heartbeat and metering all functioning and resumable — `MeteringTests` proves the
      forty-five-day offline backlog catches up in one pass, exactly once per day
- [x] Entitlement gating enforced through the single service, with the architecture test in place —
      `IEntitlementService` is now the gate only (`IsModuleEnabledAsync`, `CheckLimitAsync`); reporting
      the level moved to `IEnforcementStatusReader` so "no query handler depends on
      `IEntitlementService`" holds with no exemption list (ADR-054)
- [x] Full ladder proven on both paths, including instant reset and sub-60-second unlock on payment
- [x] No-accidental-lockout suite green — this gates the stage
- [x] Read-only correctness suite green, including the generated per-module write-refusal tests
- [x] Emergency write code proven with no connectivity; payment from inside read-only proven to recover
- [x] Support-access consent flow working with dual audit
- [x] Replication registry updated, `docs/PROGRESS.md` + ADRs updated, committed
