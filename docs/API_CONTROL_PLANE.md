# CONTROL PLANE API — Zenith Retail

Two audiences, one host (`ZenithRetail.ControlPlane`), strictly separated:

1. **Device API** — what an installed Zenith store server calls: activation, licence lease refresh,
   heartbeat, metering. Machine-to-machine, mTLS.
2. **Vendor API** — what the vendor's own console and mobile app call: who is using Zenith, how much,
   licence and billing administration, fleet health, abuse queue. Human, MFA-mandatory.

Read `docs/LICENSING.md` first — the rules there govern what these endpoints are allowed to do.

Base paths:
`https://control.zenithretail.app/device/v1` · `https://control.zenithretail.app/vendor/v1`

## 1. Isolation

The control plane is the crown jewels: compromise it and an attacker can mint licences, read every
tenant's usage, or disable every store. It therefore runs as a **separate deployment with separate
credentials, a separate database and a separate audit trail** from the tenant cloud API.

- Licence signing key in a cloud KMS/HSM; the service can request signatures, never read the key
- Vendor staff: MFA mandatory, IP allow-list, session recording on destructive actions, least privilege
  by vendor role (`support`, `billing`, `engineering`, `admin`)
- No vendor endpoint can read a tenant's business data. There is no such join. Support access is a
  separate, tenant-granted, time-boxed, audited grant (see `LICENSING.md` §8)
- Every action, including reads of tenant metadata, is written to an immutable vendor audit log

---

## 2. Device API (mTLS + device token)

### Activation
```
POST /device/v1/activations
  { licenceKey, fingerprint: { components…, score }, installId,
    machine: { os, cpu, ramGb, hostname }, storeName, contactEmail, version }
  → 201 { tenantId, storeId, nodeId, licence: {…signed…}, lease: {…signed…},
          clientCertificate, controlPlaneEndpoints }
  → 409 LICENCE_ALREADY_ACTIVATED  { boundFingerprintHint, rebindPath }
  → 402 SUBSCRIPTION_NOT_ACTIVE
  → 422 LICENCE_KEY_INVALID

POST /device/v1/activations/{id}/rebind
  { newFingerprint, reason[hardware_failure|migration|disaster_recovery], evidence? }
  → 200 { licence, lease }   auto-approved for DR restores and within the self-service allowance
  → 202 PENDING_VENDOR_APPROVAL

DELETE /device/v1/activations/{id}     clean deactivation, frees the binding immediately
```

### Lease refresh — the mechanism the monthly licence actually runs on
```
POST /device/v1/lease
  { nodeId, currentLeaseId, fingerprint, monotonicCounter, wallClock, bootId, version }
  → 200 { lease: { leaseId, entitlements, limits, issuedAt, expiresAt(+72h),
                   enforcementLevel, messages[] }, signature }
  → 402 { enforcementLevel: "read_only", reason: "subscription_lapsed",
          dueAmount, payUrl, updatePaymentMethodUrl, dunningCompletedAt, supportPhone }
```
Called on start and every 24 hours. `enforcementLevel` is the control plane telling the client where
it sits on the ladder — the client also computes it locally from lease age so a lost connection
produces the same answer. **The two must agree; a test asserts it.**

### Heartbeat
```
POST /device/v1/heartbeat
  { nodeId, at, uptimeSeconds, version, terminalsOnline, terminalsRegistered,
    syncLagSeconds, outboxDepth, lastBackupAt, lastBackupVerifiedAt,
    monotonicCounter, bootId, clockRollbackDetected, integrityCheck, errorsLast24h }
  → 200 { ack, serverTime, commands: [ { type, payload } ] }
```
`commands` lets the control plane push work without a separate channel: `refresh_lease`,
`update_now`, `run_backup`, `collect_diagnostics`, `set_channel`, `deactivate`, `revoke_support_access`.

### Metering
```
POST /device/v1/metering
  { nodeId, period: "2026-08-08", counts: { transactions: {sale, return, order, …},
    activeUsers, registeredUsers, terminals, stores, storageBytes, apiCalls,
    documentsGenerated, importsRun }, moduleUsage: { inventory: 412, hr: 8, … },
    health: { crashes, syncFailures, printerErrors, offlineMinutes } }
  → 202 { accepted, nextExpectedAt }
```
Daily rollups, computed at the store, idempotent by `(nodeId, period)`, re-sendable after an outage.
**Counts and health only.** No business rows, no names, no free text. A test asserts the payload
schema contains no field sourced from a business table.

### Diagnostics and updates
```
POST /device/v1/diagnostics       scrubbed logs, on request or on crash
GET  /device/v1/updates/check     → available version, channel, release notes, rollout eligibility
POST /device/v1/telemetry/errors  aggregated exceptions with values scrubbed
```

---

## 3. Vendor API — "who is using it and how much"

### Fleet and tenants
```
GET /vendor/v1/tenants?status=&plan=&health=&search=&cursor=
  → tenant list: name, plan, MRR, stores, terminals, users, version, licence state,
    lastHeartbeatAt, healthScore, churnRisk, openIssues

GET /vendor/v1/tenants/{id}
  → detail: contacts, subscription, stores with per-store health, terminals with
    last-seen, versions, entitlements, limits vs current usage, notes, timeline

GET /vendor/v1/tenants/{id}/usage?from=&to=&granularity=day|week|month
  → transactions, active users, storage, API calls, module usage, trend vs plan limits

GET /vendor/v1/tenants/{id}/health
  → sync lag, backup verification status, offline stores, error rates, update status,
    hardware warnings, printer failure rates

GET /vendor/v1/fleet/overview
  → tenants by status, total stores/terminals live now, version distribution,
    stores currently offline, backups unverified in 48h, licences expiring in 7 days,
    failed payments, open abuse alerts

GET /vendor/v1/fleet/versions        who is on what, upgrade campaign targeting
GET /vendor/v1/fleet/offline         every store not heard from, with duration and last contact
```

### Licences
```
POST   /vendor/v1/licences                    issue for a tenant
GET    /vendor/v1/licences/{id}
POST   /vendor/v1/licences/{id}/suspend       { reason }
POST   /vendor/v1/licences/{id}/reactivate
POST   /vendor/v1/licences/{id}/extend-grace  { days, reason }     courtesy extension
POST   /vendor/v1/licences/{id}/rebind        { approve|reject, note }
PATCH  /vendor/v1/licences/{id}/entitlements  { modules[], limits{} }   plan change
POST   /vendor/v1/licences/{id}/revoke        { reason }   hard stop — requires two-person approval
```
Every one of these is audited with the actor, reason and before/after. `revoke` requires a second
vendor admin to approve, because it is the single most damaging button in the product.

### Subscriptions and billing
```
GET  /vendor/v1/subscriptions?status=past_due|active|trialing|cancelled
POST /vendor/v1/subscriptions                 create with plan and quantities
PATCH /vendor/v1/subscriptions/{id}           upgrade/downgrade with proration preview
POST /vendor/v1/subscriptions/{id}/cancel     { effective, reason, exportRequested }
GET  /vendor/v1/invoices?tenantId=&status=
POST /vendor/v1/invoices/{id}/retry-payment
GET  /vendor/v1/dunning                       everyone in the ladder, their step, and whether their
                                              notifications were actually delivered
GET  /vendor/v1/payment-methods/at-risk       cards expiring in 30 days, lapsing mandates, repeated
                                              soft declines — the involuntary-churn work queue
POST /vendor/v1/tenants/{id}/payment-method/request-update
                                              send the customer a secure update link
GET  /vendor/v1/revenue                       MRR, ARR, churn, expansion, cohort retention
GET  /vendor/v1/overages?period=              soft-limit breaches ready to bill
```

### Abuse queue
```
GET  /vendor/v1/abuse?status=open&severity=
  → detections: clone (two fingerprints, one licence), counter rollback, clock tamper,
    impossible travel, terminal overage, colliding document series, integrity check failure
GET  /vendor/v1/abuse/{id}                    full evidence trail and timeline
POST /vendor/v1/abuse/{id}/resolve            { verdict[legitimate|misconfiguration|violation],
                                                action[none|contact|rebind|suspend], note }
```
Detections **never** auto-disable anything. A human decides. A false positive that kills a paying
customer's store costs far more than a week of piracy.

### Support access (consent-gated)
```
POST /vendor/v1/tenants/{id}/support-access/request   { reason, scope, durationHours }
  → the tenant admin receives an approval request in their own UI and by email
GET  /vendor/v1/tenants/{id}/support-access           active and historical grants
DELETE /vendor/v1/tenants/{id}/support-access/{id}    end early
```
While a grant is active the tenant sees a persistent banner, and every vendor action is written to the
**tenant's** audit log as well as the vendor's. There is no route to tenant business data without a grant.

### Releases
```
POST /vendor/v1/releases                      register a version with notes and channel
POST /vendor/v1/releases/{v}/rollout          { channel, percentage, tenantIds?, schedule }
POST /vendor/v1/releases/{v}/halt             stop a bad rollout immediately
GET  /vendor/v1/releases/{v}/adoption         who has it, who failed, error rates by version
```

### Provisioning
```
POST /vendor/v1/tenants                       create tenant, plan, licence key, onboarding pack
POST /vendor/v1/tenants/{id}/export           full data export package for offboarding
DELETE /vendor/v1/tenants/{id}                after the retention window; produces a deletion certificate
```

---

## 4. Alerts pushed to the vendor app

`tenant.signed_up` · `tenant.activated` · `payment.failed` · `subscription.past_due` ·
`subscription.cancelled` · `licence.expiring` · `abuse.detected` · `integrity.failed` ·
`store.offline` (beyond threshold) · `backup.verification_failed` · `sync.lag_critical` ·
`usage.over_limit` · `version.rollout_failing` · `churn.risk_raised` · `support.access_requested`

Delivered by push to the vendor mobile app, email, and optionally a chat webhook. Severity-routed and
digestible so the important ones are not buried.

## 5. Rules

- Device endpoints are mTLS-only with a per-node client certificate; a stolen certificate can be
  revoked from the vendor console and takes effect at the next call.
- Every device call is idempotent and safe to retry; a store that has been offline for a month must be
  able to catch up without duplicating metering.
- The device API degrades gracefully: if the control plane is down, the store keeps trading on its
  existing lease to that lease's natural expiry and retries. **A vendor-side outage must never lock a
  customer out** — one bad deployment would otherwise lock out the entire customer base at once. Test
  it by running a full trading day with the control plane switched off, and again with it returning
  500s, timeouts and malformed responses.
- Rate limits per node prevent a misbehaving install from flooding the platform.
- No vendor endpoint returns tenant business data. Not filtered — structurally absent.
