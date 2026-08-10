# STAGE 30b — Vendor Control Plane, Metering & SaaS Billing

**Status:** NOT_STARTED · **Depends on:** 04b, 29, 30 · **Reference reading:** `docs/API_CONTROL_PLANE.md` (all of it), `docs/LICENSING.md` §6–§9, `docs/DECISIONS.md` ADR-024, ADR-025

## Objective
The vendor's half: the service that issues licences, receives every heartbeat, aggregates usage across
the whole customer base, bills the monthly subscription, detects duplicated installs, and gives the
Vuma team one screen answering "who is using this, how much, and is anyone abusing it?"

Placed after the module stages because it aggregates their usage, and before release because the
installer ships activation.

## Deliverables

**`VumaRetail.ControlPlane` — a separate deployable**
- Its own database, its own credentials, its own immutable audit log, deployed independently of the
  tenant cloud (ADR-024)
- Licence signing through a cloud KMS/HSM: the service requests signatures and can never read the
  private key
- Device API per `API_CONTROL_PLANE.md` §2: activation, rebind, lease, heartbeat, metering,
  diagnostics, update check — mTLS, idempotent, resumable
- Vendor API per §3, MFA-mandatory, IP allow-listed, role-scoped (`support`, `billing`, `engineering`,
  `admin`), with two-person approval on `revoke`

**Licence issuance**
- Monthly issuance job: for every active subscription, mint the next month's licence before the current
  one expires. A payment failure stops issuance; it does not revoke the current licence — the ladder
  handles the rest.
- Entitlement changes take effect at the next lease refresh, or immediately via a pushed
  `refresh_lease` command when the store is online
- Grace extension, courtesy months, suspend, reactivate, rebind approval — all audited with actor and
  reason

**Unlock issuance (the vendor's support lifeline)**
- **Emergency write codes:** generate a signed, single-tenant, single-use, time-limited code from the
  tenant screen in one click, readable over the phone, working with no connectivity at the customer's
  end. Default 72 hours, selectable 1–168. Every issuance audited with actor and reason, and a report
  of who is issuing how many — a support agent handing out codes weekly to the same tenant is a signal.
- **Write unlock:** restore write access to a read-only tenant, time-limited, for a settled dispute, a
  payment you can see but the gateway has not confirmed, or a customer closing off month-end before
  cancelling
- **Bulk safety valve:** the ability to suspend enforcement fleet-wide in one action, for the case where
  a vendor-side bug has wrongly locked customers out. This is the emergency brake and it should exist
  before it is needed.

**Metering and usage analytics**
- Ingest daily rollups from every node, deduplicated by `(nodeId, period)`, backfilled after outages
- Aggregate per tenant, per store, per module: transactions, active users, terminals, storage, API
  calls, module usage depth
- **Feature adoption analytics** — which modules are genuinely used versus merely licensed. This is the
  data that tells the vendor what to build next and which customers are about to churn because they
  never onboarded properly.
- Usage vs plan limits with 80% and 100% alerts, and an overage report ready to bill
- Health scoring per tenant: sync lag, backup verification, offline duration, crash rate, printer
  failures, update currency, support ticket volume → a churn-risk indicator

**Subscription billing**
- Recurring mandates: card on file and debit order, stored as gateway tokens (never card data), with
  multiple methods per tenant and a designated fallback so one decline is not the end of it
- **Involuntary-failure prevention (ADR-029)** — this is where subscription revenue is actually
  protected, before dunning ever starts:
  - card expiry detection with notifications at 30 and 7 days before expiry
  - mandate expiry and re-authentication reminders for debit orders
  - gateway account-updater support so reissued cards keep working automatically
  - retry timing tuned to paydays rather than blind +3/+7/+14
  - a self-service payment-method update page linked from every dunning email **and reachable from
    inside the product while read-only**
  - dunning **pauses rather than advances** if its notifications are not recorded as delivered — a
    bounced email means the customer was never warned
- Plans and pricing: base per store, per terminal, per named user, per-module add-ons, monthly and
  annual, trials, discounts, promotional pricing
- Mid-cycle upgrades prorated and immediate; downgrades at cycle end with a usage-fits check
- Invoice generation, payment collection through `IPaymentGateway` (reuse the Stage 21 abstraction),
  receipts, credit notes, tax on vendor invoices per the vendor's own jurisdiction
- Dunning ladder: retry at 3/7/14 days, escalating notifications, then Path B of the enforcement ladder
- Reseller/partner accounts: margin, partner-billed tenants, partner-scoped visibility (a partner sees
  only their own tenants — dedicated security test)
- Revenue reporting: MRR, ARR, churn, expansion, cohort retention, LTV, and an export to the vendor's
  own accounting. **The vendor's books are not a tenant's books** (ADR-025) — no vendor revenue ever
  appears in any tenant's Stage 07 ledger.

**Abuse detection**
- Detectors for every signal in `LICENSING.md` §6: duplicate fingerprints on one licence, monotonic
  counter rollback, clock tamper, impossible travel, terminal overage, integrity check failure, and
  **colliding document number series across installs** — the strongest signal available and it uses
  data already being synced
- Detections land in a queue with a full evidence trail and timeline. **Nothing auto-disables.** A
  human resolves each one with a verdict and an action, and every verdict is recorded so patterns of
  false positives can be tuned out.
- Rebind pattern analysis: frequent rebinds across distant fingerprints get flagged rather than
  silently allowed or silently blocked

**Fleet operations**
- Version distribution across the installed base, staged rollout by channel/percentage/tenant, halt a
  bad rollout instantly, adoption and failure rates by version
- Offline store board with duration and last contact — the vendor often knows a store has a problem
  before the store phones in, which is a genuine support advantage
- Backup verification failures across the fleet, surfaced as an operational queue (a customer whose
  backups have silently failed for a week is a disaster waiting to happen)
- Remote command dispatch through the heartbeat channel with full audit

**Support access**
- Request → tenant approval → time-boxed grant → automatic expiry, with dual audit and the tenant-side
  banner from Stage 04b
- No vendor endpoint returns tenant business data outside an active grant. This is structural, not
  filtered, and there is a dedicated security suite proving it.

**Provisioning and offboarding**
- Self-service signup and vendor-created tenants: provision tenant, generate licence key, send the
  onboarding pack, create the trial subscription
- Offboarding: full export package (database dump, documents, data dictionary), 90-day retention
  window, verified deletion with a certificate

**Vendor console and vendor mobile mode**
- Web console covering all of the above
- **Vendor mode in the Stage 30 Android app** rather than a second app: fleet overview, tenant lookup,
  alerts feed, licence actions, billing status, abuse queue triage. Gated by vendor-role auth against
  the control plane, and completely separate from the tenant-facing modes.
- Alert push per `API_CONTROL_PLANE.md` §4, severity-routed and digested

## Business rules
- The control plane **may not** be a dependency of trading. A full simulated trading day with the
  control plane switched off must produce zero customer impact — assert it.
- Payment failure never revokes a live licence; it stops the next issuance and starts the dunning
  ladder. Revocation is a deliberate, two-person, audited act.
- Metering ingestion accepts only whitelisted aggregate counters. A payload containing a business-table
  field is rejected and alerted on, because it means a client bug is leaking customer data.
- Every vendor action against a tenant is audited on both sides.

## Tests / acceptance
- Licence issuance across a month boundary, with payment success and payment failure paths
- Ladder integration: lapse a subscription, assert the tenant reaches read-only only after completed
  dunning with delivered notifications, and recovers full write access within 60 seconds of payment
- Dunning pauses when a notification bounces, and resumes when a deliverable address is supplied
- Card-expiry pre-emptive notifications fire at 30 and 7 days on a virtual clock
- A payment made from inside a read-only tenant is received and restores access
- Emergency code issuance → offline redemption → expiry → reuse rejected
- Export unlock grants exports only, and expires
- Fleet-wide enforcement suspension takes effect on every node at the next heartbeat
- Metering: 500 simulated nodes, 90 days, with outages and backfills — no duplicates, no gaps
- Billing: proration on a mid-cycle upgrade, downgrade blocked when usage exceeds the smaller plan,
  overage calculation, full dunning ladder
- Partner isolation: a partner account cannot see another partner's tenants (security test)
- **Abuse detection suite:** simulate a cloned install, a VM snapshot rollback, a clock rollback, an
  impossible-travel pair and colliding document series — each must be detected, and a legitimate
  hardware replacement must **not** be
- Support access: no tenant business data reachable without a grant; grant expiry enforced; dual audit
  entries present
- Control plane offline for a full trading day: zero customer impact
- Vendor audit log immutable and complete

## Exit checklist
- [ ] Device API live: activation, lease, heartbeat, metering all working against real installs
- [ ] Vendor console answers "who is using Vuma and how much" at fleet and tenant level
- [ ] Monthly licence issuance, billing, dunning, lockout and instant reactivation working end to end
- [ ] Emergency write code, write unlock and fleet-wide enforcement suspension all working and audited
- [ ] Pre-emptive card-expiry and mandate notifications proven; dunning pauses on undelivered notice
- [ ] Abuse queue detecting the full signal set with a human-in-the-loop workflow and no auto-disable
- [ ] Vendor mode live in the Android app with alert push
- [ ] Support-access consent proven; no business-data path without a grant
- [ ] Control-plane-outage test proves no customer impact
- [ ] `docs/PROGRESS.md` + ADRs updated, committed
