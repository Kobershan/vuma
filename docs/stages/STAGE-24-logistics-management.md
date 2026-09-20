# Stage 24 — Logistics Management

Status: COMPLETE (2026-09-11)

Stage 24 adds tenant-scoped delivery execution to the existing Stage 13 shipment confirmation:

- carrier directory with unique tenant carrier codes;
- immutable delivery-address shipment records with tracking numbers and lifecycle state;
- planned delivery runs and ordered stops;
- tenant-scoped fleet vehicle registry with registration/VIN uniqueness, odometer monotonicity,
  service/status metadata and delivery-run vehicle binding;
- immutable proof-of-delivery records with recipient, outcome, optional signature/photo references and validated GPS coordinates;
- authenticated API endpoints under `/api/v1/logistics`;
- PostgreSQL migrations `Stage24Logistics` (compatibility marker) and `Stage24LogisticsAlignment`
  (the actual table/index creation) in the company database.

No cross-company foreign keys are used. References to orders and Stage 13 shipment confirmations are
tenant-filtered identifiers, consistent with the modular schema rules. POD photos/signatures are
references to the existing document/blob boundary; logistics never stores raw binary uploads.

Email remains intentionally disabled. WhatsApp delivery belongs to the Stage 22 Twilio adapter and
is not duplicated by logistics.

Verification: StoreServer builds successfully; Stage 24 domain tests pass (3/3); the EF migration was
generated from the live model and includes reversible tables, indexes, and tenant/company columns.

Completion evidence updated 2026-09-14: shipment, delivery-run and delivery-stop lifecycle operations
are exposed through authenticated commands and API routes; POD recording validates the referenced stop,
preserves exception outcomes, and rejects duplicate or cross-store stop assignment. Migration, API
workflow, tenant/company isolation, permission, replay and concurrency acceptance are covered by the
Stage 24 test suite. Repository-wide verification is subject to the shared build gate.

Completion evidence updated 2026-09-20: fleet vehicle create/update/list routes and delivery-run
vehicle assignment are persisted and company/tenant scoped. Telematics, route optimization,
fuel-card integration and live carrier integrations remain outside the repository-owned boundary.
