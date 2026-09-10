# TASK-22-01 — Control-App company identity and business type registry

**Depends on:** 06c

Issue non-enumerable company_id before local onboarding; consume it during provisioning; add business_id, business_type, audit and replay-safe registration. Prove no local minting and no schema migration on type change.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

