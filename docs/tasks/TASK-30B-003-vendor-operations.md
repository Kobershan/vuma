# TASK-30B-003 — Abuse, fleet, support, provisioning, and vendor surfaces

Status: IN_PROGRESS — abuse review, partner scope, and time-boxed support grant boundaries implemented
Stage: 30b
Type: Control-plane application, security, vendor operations

## Work log

2026-09-15: Added human-reviewed abuse evidence detection for duplicate installs, counter/clock
rollback, impossible travel and colliding document series. Added partner tenant scoping and
tenant-approved, expiring support grants. No detector performs automatic disablement. Focused tests
pass **4/4**. Fleet rollout, provisioning/offboarding, console/Android mode, MFA/IP policy, alerts,
immutable persistence and full outage/offline acceptance remain.

2026-09-17: Added a deterministic `FleetOperations` policy boundary for staged version rollouts,
instant rollout halts, offline/backup health reporting and audited remote-command queueing. Unknown
nodes and invalid rollout/health inputs fail closed. Control-plane tests pass **17/17**; durable
fleet persistence, authenticated vendor routes and production provisioning remain.

2026-09-17: Added vendor provisioning with generated licence keys, verified-export-gated
offboarding and a 90-day retention window. Emergency write codes are signer-backed, limited to
1–168 hours, tenant-specific and single-use. Control-plane tests pass **19/19**; durable audit,
authenticated vendor routes and production provisioning remain.
