# Proxima Orbit — loyalty vendor integration

Status: integration specification, 2026-09-12. Proxima Orbit is the owner's loyalty app and the
designated loyalty vendor for Vuma. This directory contains integration documentation only; no
upstream Orbit source, service credentials, signed contract or live service was supplied or imported.

## Existing integration

- Port: `src/VumaRetail.Application/Loyalty/LoyaltyPorts.cs` (`IOrbitClient`).
- Adapters: `src/VumaRetail.Infrastructure/Loyalty/HttpOrbitClient.cs` and `InMemoryOrbitClient.cs`.
- Public mappings: `src/VumaRetail.PublicApi/Loyalty/LoyaltyEndpoints.cs`.
- Contract: [API_LOYALTY](API_LOYALTY.md); scope: [Stage 20](stages/STAGE-20-loyalty-public-api.md).

The current StoreServer calls `AddVumaLoyalty()` with the fake adapter selected. Configuration
options alone do not select the HTTP adapter in that host. Production readiness requires explicit
options binding, environment validation, real HTTP configuration and contract tests against Orbit.
The fake must be limited to development/tests; missing production credentials should disable the
integration visibly without fabricating points or preventing ordinary non-loyalty sales.

## Ownership and deployment

Orbit owns its member identity and points ledger. Vuma owns company/customer mappings, earning
intents, sale references, reconciled transaction links and a clearly dated balance cache. Vendor
integration does not mean placing a copy of Orbit's service source or private keys in customer
installers. Keep the upstream app in its separately controlled repository. If source vendoring is
later required, record the exact approved repository, immutable revision, ownership/licence terms,
update owner and hash here before adding code.

Vuma's vendor control plane is a separate concept from the Orbit loyalty provider. Orbit credentials
must not grant vendor-console access, and vendor staff accounts must not become member accounts.

## Required contract before production

1. Pin the approved HTTPS service origin, API version and authentication method. Document ownership
   of tenant/programme/member IDs and allocate independent credentials per integration scope.
2. Establish enrollment, earn, redemption, reversal, balance, tiers and rewards operations with
   explicit rounding/units and stable error codes. Current `HttpOrbitClient` routes are assumptions
   to validate against the real upstream, not proof of compatibility.
3. Guarantee durable idempotency by tenant/programme/operation ID and input hash. A timeout after
   remote success is an unknown result, resolved with the same key or a status lookup. Permanent
   refusals are distinct from transport failures; retries cannot create a second debit/credit.
4. Signed webhooks include event ID, occurrence time, key ID, tenant/programme mapping and monotonically
   ordered member version. Verify raw bytes, replay/freshness and scope before updating the cache.
   An old signed balance event must never replace a newer balance and appear freshly synchronized.
5. Store local earning intents durably during outage and deliver them after reconnect. An unavailable
   provider means redemption remains pending/refused; it is not accepted tender. Never authorize a
   burn from a cached balance shared by multiple offline tills.
6. Reverse/reconcile cancelled sales and refunds through linked operations. Finance explicitly owns
   redemption accounting and liability policy; Stage 20's recorded GL deferral must close before
   loyalty tender is sold as production-ready.
7. Publish only consented/minimal member data. Keep credentials server-side, redact logs and test
   tenant/programme/member isolation with three independent accounts.

## Acceptance evidence to attach

- Two simultaneous requests with the same earn key produce one durable credit.
- Two tills attempt to spend a 100-point balance by 80 each; at most one succeeds.
- Timeout after a committed credit, retry and restart still produce one credit.
- Out-of-order balance events (version 12 then 11) leave version 12 in place.
- One day's queued earns survive restart and reconnect without duplicates.
- Wrong tenant/programme/member, invalid signature, missing secret and revoked credential are refused.
- A real sandbox contract run and an isolated production onboarding rehearsal are recorded, with
  secrets omitted. These checks are currently UNVERIFIED for a real Orbit service.

See [the audit](REPOSITORY-AUDIT-2026-09-12.md) and
[the architecture recommendations](OFFLINE-CLOUD-API-AND-PROTECTION.md).
