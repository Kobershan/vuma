# LICENSING & ANTI-PIRACY — Vuma Retail

Vuma is sold as SaaS: a monthly subscription per tenant, with the software installed on the
customer's own Windows hardware. That combination — recurring revenue, customer-controlled hardware —
is what this document exists to handle. Built in Stage 04b (client and tenant side) and Stage 30b
(vendor control plane).

## 1. The rules that govern this area

> **Rule 1 — Lapsed subscription means read-only.**
> Vuma is a subscription product billed on a recurring mandate. When the subscription is not current,
> the tenant keeps **full read access** — every screen, every record, every report, every export, every
> reprint — and loses **all write access**. They can see and print their data. They cannot trade,
> capture, edit, import or configure. Selling is a write; the till stops.

> **Rule 2 — Read-only must be deliberate, never accidental.**
> An engineering requirement, not a commercial one. Read-only may only ever be triggered by a **known**
> subscription state after a completed dunning cycle. Never by a network fault, never by a vendor-side
> outage, never by a single failed charge, never by a clock or hardware change.

> **Rule 3 — The customer must always be able to pay you.**
> The licence screen, the payment page and the **payment-method update flow stay fully writable in
> read-only mode**. A customer whose card expired must be able to fix it from inside the product. If
> they cannot, every recovery becomes a phone call to you.

> **Rule 4 — Data already captured is never stranded.**
> Backup continues, restore is permitted, exports work, and any offline sales already sitting in a
> terminal queue still flush to the store server and the cloud. Replicating records that already exist
> is not a new write, and blocking it would destroy the customer's data rather than restrict their
> access.

### Recurring billing fails routinely, and almost never deliberately

This is the single most important operational fact in this document. Cards expire, get reissued after
fraud, hit limits, or require re-authentication. Debit order mandates lapse, and payers can reverse or
dispute them. A large share of failed subscription charges are **involuntary** — the customer fully
intends to pay and has no idea anything went wrong.

The consequences are designed around, not hoped away:

| Failure mode | Consequence if ignored | Handled by |
|---|---|---|
| Card expires silently | A paying customer's tills stop at 08:00 with no warning | Pre-emptive expiry detection (§8) — warn 30 and 7 days before the card expires, not after it fails |
| One failed charge treated as non-payment | Loyal customer punished for their bank's decline | Read-only requires a **completed** dunning cycle with delivered notifications |
| Customer cannot pay from inside a read-only system | Every recovery becomes a support call | Payment and card-update screens stay writable (Rule 3) |
| Vendor-side outage read as "unlicensed" | One bad deployment puts the entire customer base into read-only at once | Stores run on their existing lease to its natural expiry; the control plane is never a dependency |
| Store's internet outage read as non-payment | A fibre cut stops trade | The cannot-verify path (§4 Path A) is separate and has its own tolerance window |

### Statutory records

Read-only resolves this cleanly. Tax and company legislation requires businesses to retain records for
years, and a read-only tenant can still produce every one of them. State the position in the licence
terms so it is contractual rather than improvised during a dispute.

## 2. Objects

```
Vendor (you)
 └── Tenant            a customer business
      └── Subscription plan, quantities, add-ons, billing cycle, status
           └── Licence  monthly, signed, entitlements + limits + expiry
                └── Activation   bound to one store server's hardware fingerprint
                     └── Lease   short-lived signed token the software actually runs on
                          └── Terminal sub-leases  issued by the store server on the LAN
```

**Licence key** — human-readable, issued once, used at activation: `ZNTH-XXXXX-XXXXX-XXXXX-XXXXX`
(Base32, checksummed so a typo is caught before it hits the network).

**Licence** — issued monthly by the control plane, signed with Ed25519. The private key lives in a
cloud KMS/HSM and never leaves it. The public key is embedded and pinned in the binaries. A licence
carries: tenant, stores, entitlement set (modules), limits (stores, terminals, named users,
transactions, storage), issue time, expiry, hardware fingerprint hash, nonce, and a monotonic
issuance counter.

**Lease** — what the software validates on every start and every 24 hours: a short-lived (72-hour)
signed token derived from the current licence. Heartbeats refresh it. This is the mechanism that
makes "monthly licence" real without requiring a permanently online store.

## 3. Hardware binding

Activation binds a licence to one store server. The fingerprint is a composite of:

| Component | Weight |
|---|---|
| Motherboard / system UUID | 3 |
| Windows machine GUID | 3 |
| Primary NIC MAC address | 2 |
| System volume serial | 2 |
| CPU signature | 1 |

The fingerprint is stored as a salted hash, never as raw hardware identifiers. Matching is
**N-of-M with tolerance**: a score of 7 or more out of 11 is the same machine. Replacing a network
card or a data disk must not break a customer's licence at 6am on a Monday — but a wholesale move to
different hardware scores below the threshold and requires a rebind.

**Rebinds:** two self-service rebinds per twelve months, unlimited with vendor approval. Every rebind
is logged, and a rebind pattern that looks like licence sharing raises an abuse-queue item rather than
being silently allowed or silently blocked.

**DR exception — this one is non-negotiable.** A restore authenticated through the Stage 04 restore
path automatically issues a rebind. Disaster recovery must never be blocked by licensing. If a store
has burned down, the last thing anyone needs is an activation error. Test this explicitly.

## 4. The enforcement ladder

`Normal → Notice → ReadOnly`. Two paths reach it, deliberately different. Both vendor-configurable per
plan and per tenant; values below are the shipped defaults.

### Path A — cannot verify (the store cannot reach the control plane)

The software does not know whether the subscription is current, so it runs on the last valid lease.

| Since last successful heartbeat | Behaviour |
|---|---|
| 0–3 days | Silent. Normal operation. |
| 4–7 days | Back-office banner, email to the tenant admin, **vendor alerted** — you often know about the connectivity problem before the customer reports it. |
| 8–14 days | Notice at POS session open. Daily email. Vendor alerted at higher severity. |
| **15+ days (configurable 1–45)** | **READ-ONLY.** |

### Path B — non-payment (the store can reach us and the subscription is not current)

| Stage | Behaviour |
|---|---|
| Charge fails | Silent. Automatic retries at 3, 7 and 14 days. Card-update prompt emailed. **No customer-visible change in-product.** |
| Dunning day 3 | Back-office banner with a "update payment method" button that works. |
| Dunning day 7 | POS notice at session open. Vendor account manager alerted. |
| Dunning day 14 | Final notice on all channels, stating the read-only date explicitly. |
| **Dunning complete + grace (default 0, configurable)** | **READ-ONLY.** |

A single failed charge never restricts anyone. The customer gets 14 days of escalating warning with a
working payment link at every step, which is what makes the restriction defensible when they complain.

### What read-only means, precisely

**Works — everything a person can look at or print:**
- Every screen, every record, search, filter, drill-down
- Every report, dashboard and analytics view
- Reprint any receipt, invoice, statement, purchase order, payslip, delivery note
- Export in every format (XLSX, CSV, PDF)
- Backup continues on schedule; restore is permitted
- Offline sales already queued on a terminal still flush to the store server and cloud (Rule 4)
- **Licence, payment and payment-method-update screens are fully writable** (Rule 3)
- Public storefront and loyalty APIs continue to serve **reads** (catalogue browse, balance lookup)

**Blocked — everything that creates or changes a record:**
- New sales. The till will not start a transaction. This is the commercial lever.
- Any create, update or delete on any business entity
- Stock movements, orders, receipts, transfers, adjustments, stocktakes, work orders
- Imports, approvals, configuration, users, prices, promotions
- Public API writes: checkout, loyalty earn and burn
- Android app writes

**Open-session carve-out** (default on, configurable off): if read-only falls due while a till has an
open sale or an unclosed cash-up, that sale and that cash-up complete, then the terminal goes
read-only. No new sale can start. This exists so a restriction does not leave a drawer of unrecorded
cash and a customer holding goods.

**Error codes.** Internal API and desktop return `403 LICENCE_READ_ONLY` with the amount due and a
payment link, so the UI can offer the fix rather than a dead end. The **public** storefront and loyalty
APIs return a neutral `503 SERVICE_TEMPORARILY_UNAVAILABLE` on writes — a tenant's own customers must
never be shown their supplier's billing status.

**Recovery is immediate.** Payment lands → next heartbeat or "retry now" → full access within 60
seconds. No reactivation step, no vendor intervention.

**Hard lockout** remains available as a **manual vendor action only** (`revoke`, two-person approval),
for confirmed piracy or abuse resolved through the queue in §7. It is never reached automatically.

## 5. Vendor unlock mechanisms

All three are issued from the control plane, all are audited with actor and reason, and all work with
no internet at the customer's end.

**Emergency write code.** A signed, time-limited code (default 72 hours, vendor-selectable 1–168) the
vendor generates and reads over the phone. The customer types it into the licence screen and trades
normally until it expires. This handles the Friday-evening payment, the debit order the bank reversed
in error, and the store whose connectivity died over a long weekend. Without it you personally answer
those calls at 07:00 on Saturdays. Codes are single-tenant, single-use, expiring, and every issuance is
logged and reported — repeated codes to one tenant is a signal worth seeing.

**Write unlock.** A vendor-granted, time-limited restoration of write access to a read-only tenant —
for a settled dispute, a payment you can see but the gateway hasn't confirmed, or a customer who needs
to close off month-end before cancelling. Read access never needs unlocking; it is always there.

**Grace extension and courtesy month.** Extend the ladder or issue a free month from the control plane;
effective at the next heartbeat, or pushed immediately when the store is online.

## 6. Entitlement enforcement

One choke point: `IEntitlementService`. Every module manifest declares its licence flag; an
architecture test fails the build if a module gates itself any other way.

| Limit type | Behaviour when exceeded |
|---|---|
| Stores, terminals, named users | **Hard.** The eleventh terminal on a ten-terminal plan cannot register. Clear message naming the plan limit and an upgrade path. |
| Modules | **Hard.** Not licensed = not visible in navigation, endpoints return 403 with an upgrade code. |
| Transactions/month, storage GB, API calls | **Soft.** Never block. Meter, warn at 80% and 100%, bill as overage or trigger an upgrade conversation. |

Nothing a cashier does mid-sale hits a hard limit: limit checks happen at configuration time —
registering a terminal, creating a user — not at transaction time. This is a performance and usability
rule, and it stays true whether the tenant is licensed or locked.

## 7. Anti-piracy and duplicate detection

**Be honest about what is achievable.** Software that runs on hardware the customer controls can
always eventually be cracked by someone determined. The goal is to make casual duplication
impractical, make deliberate piracy detectable, and — most importantly — make the legitimate product
depend on services only the vendor can provide.

**Client-side (raises cost, does not prevent)**
- All binaries Authenticode-signed; the licensing assembly integrity-checked against embedded hashes
- Ed25519 licence signature verified against a pinned public key; a modified public key fails the
  signature check on the *next* real licence
- Licence state stored in a DPAPI-protected store plus a shadow copy; disagreement between them is a
  tamper flag
- Monotonic "highest wall-clock seen" persisted; a system clock set backwards is a tamper flag, not a
  free extension

**Server-side detection (this is where the actual leverage is)**

Every heartbeat carries an install id, boot id, hardware fingerprint, a monotonic counter, the licence
nonce, terminal count, version and public IP. The control plane looks for:

| Signal | What it usually means |
|---|---|
| One licence, two different fingerprints heartbeating | Cloned install |
| Monotonic counter goes backwards or repeats | VM snapshot rollback, or a restored clone running alongside the original |
| Two distinct public IPs in distant geographies within minutes | Shared licence |
| Terminal count above entitlement | Unlicensed expansion |
| Clock rollback flag | Tamper attempt |
| **Document number series colliding across installs** | Two systems trading on one licence — the strongest signal available, and it uses data already being synced |
| Long silence followed by a fingerprint change with no rebind | Migration attempt or a clone |

Detections land in an **abuse queue** for a human to judge. They do not auto-disable anything. A false
positive that kills a paying customer's store is far more expensive than a week of piracy.

**The real moat.** A cracked install loses cloud backup, verified restore, auto-updates, multi-store
sync, the Android app and the public APIs. A business that cares about its data cannot afford to run
without those, and a business that doesn't care about its data was never going to pay. Design the
value into the services, not into the obfuscation.

## 8. Subscription and billing (vendor side)

**Plans** — tiered by capability, priced on quantity: base per store, per additional terminal, per
named user, plus per-module add-ons. Monthly and annual cycles, annual at a discount. Trials with an
automatic expiry that lands on the offline ladder's restricted mode, never on a hard stop.

**Changes** — mid-cycle upgrades prorated and effective immediately; downgrades effective at the next
cycle, with a check that current usage fits the smaller plan before it's accepted.

**Payment methods** — card on file and debit order mandates, both stored as gateway tokens (never card
data). Multiple methods per tenant with a designated fallback, so one decline is not the end of it.

**Pre-emptive failure prevention** — this is where involuntary churn is actually solved, before dunning
ever starts:
- Card expiry detection: notify the billing contact 30 and 7 days before the stored card expires
- Mandate expiry and re-authentication reminders for debit orders
- Account-updater support where the gateway offers it, so reissued cards keep working automatically
- Retry timing that avoids known-bad windows and retries after payday rather than blindly at +3 days
- A self-service payment-method update page reachable from every dunning email **and from inside the
  product while read-only** (Rule 3)

**Dunning** — attempt, retry at 3/7/14 days, escalating notification to the tenant's billing contact,
vendor alert, then the Path B ladder. Every step configurable. Read-only is only ever entered after the
cycle has **completed** and its notifications are recorded as **delivered** — a bounced dunning email
means the customer was never warned, and the ladder pauses rather than advancing.

**Resellers and partners** — a partner can hold multiple tenants, receive margin, and see only their
own tenants. Partner-billed tenants have their dunning routed to the partner.

**Keep the vendor's books separate.** The vendor's subscription revenue is not a tenant's business
data and does not belong in any tenant's Stage 07 ledger. The control plane keeps its own billing
records and exports them to the vendor's own accounting (which may well be a Vuma instance — that's
fine, but it's a separate tenant, connected through the normal integration, not a back door).

## 9. Telemetry and privacy — what the vendor may and may not collect

The vendor's customers will ask this question, and their own POPIA/GDPR position depends on the
answer. Get it right, publish it, and enforce it in code.

**Collected (operational metadata):** counts and health only — transaction counts by type, active
terminal and user counts, storage used, module usage, version, uptime, sync lag, backup verification
status, error rates and stack traces with values scrubbed, hardware profile, licence state.

**Never collected:** customer names or contact details, sales line detail, product-level pricing,
employee names or pay, supplier terms, any document content, any free-text field, any table row from a
business module. The metering job aggregates at source; raw business data never leaves the store for
telemetry purposes. A test asserts that the metering payload contains no field sourced from a business
table.

**Support access is not a back door.** Vendor staff cannot silently read a tenant's business data.
Support access requires a tenant-granted, time-boxed grant; while it is active a banner is visible in
the tenant's own UI, every action is written to the tenant's audit log as well as the vendor's, and it
expires automatically. Build it this way from the start — retrofitting consent is not possible.

## 10. Lifecycle

**Signup → trial → paid:** self-service or vendor-created tenant, provisioning generates the tenant,
the licence key and the onboarding pack. Trial licences carry a full entitlement set and a short
expiry.

**Offboarding:** on cancellation the tenant gets a complete export package (database dump plus
documents plus a readable data dictionary) and a retention window — 90 days by default — before
deletion. Deletion is verified and certified. This is both the decent thing to do and the thing that
makes customers comfortable signing up in the first place.
