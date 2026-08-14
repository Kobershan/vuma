# STAGE 06 — Master Data: Items, Variants, Barcodes, UoM, Partners

**Status:** DONE (2026-08-14) · **Depends on:** 05 · **Reference reading:** `docs/DATA_MODEL.md` §1–§3
(mandatory columns, types, schema conventions), `docs/CONVENTIONS.md` §1–§5, `docs/DECISIONS.md`
ADR-037 (the boundary this stage closes), `docs/LICENSING.md` §6 (hard vs soft limits — items and
partners are neither; do not invent a limit kind for them).

## Objective
The reference data every later module reads and nothing before it needed: what a tenant sells
(items, variants, barcodes, units of measure) and who it trades with (suppliers, customers, and
partners who are both). Stage 07 posts value against items and partners; Stage 08 puts stock behind
them; Stage 09/10 sell them. None of that can be built, or even faked convincingly, without this
stage existing first — which is why it is early and everything downstream depends on it.

This stage is deliberately narrow. It owns **identity and description**, not behaviour that belongs
to a later stage:
- No stock quantity or valuation — that is Stage 08's append-only ledger (CLAUDE.md §7 rule 6).
- No price, cost or promotion — that is Stage 10.
- No GL account, tax posting or AR/AP balance — that is Stage 07. An item names a *tax class code*;
  Stage 07's posting rules engine decides what that code means in the ledger (CLAUDE.md §7 rule 12).
- No approval gate on any command here. Stage 05 ships the generic `IApprovalService` for stages that
  need one; ordinary master-data maintenance does not gate on a threshold, so there is nothing for this
  stage to register. A later stage is free to add an approval policy on top without touching this one.

## Deliverables

**`catalog` module** (schema `catalog`)
- `UnitOfMeasure` — already scaffolded (`Domain/Catalog/UnitOfMeasure.cs`); finish it. Base unit or a
  single-level conversion to one, per kind (`UnitOfMeasureType`). Fix the two defects left mid-edit:
  `CatalogRuleException` does not exist yet (the type referenced by `CreateDerived` must be written),
  and the dead `type_ArgumentGuard` method must go.
- `Item` — tenant-scoped product/service record: unique `Code` (upper-cased, like `Store.Code`),
  `Name`, optional `Description`, `ItemType` (`Stock`, `Service`, `NonStock`), base `UnitOfMeasureId`
  (must resolve in the same tenant), optional `TaxClassCode` (a string code, never a schema reference
  — CONVENTIONS.md §2 forbids the cross-schema FK), `IsActive`. Deactivate, not delete (CLAUDE.md §7
  rule 8).
- `ItemVariant` — belongs to an `Item`; carries its own `Sku` (unique per tenant) and an ordered list
  of small attribute pairs (`Size: M`, `Colour: Red` — a value object, not a separate table; no
  retailer needs a third dimension and a fourth can be added later without a migration that breaks
  existing rows). An item with no variants sells directly against itself; an item with variants only
  sells against a variant — enforced in the domain, not left to the caller to get right.
- `Barcode` — `Code` + `Symbology` (`Ean13`, `Upc`, `Code128`, `Internal`), unique per tenant
  regardless of symbology (a scanner reads a code, not a type). Attaches to exactly one of an `Item`
  (only when that item has no variants) or an `ItemVariant`. Exactly one barcode per item/variant may
  be `IsPrimary` — the one printed on a label and shown first at POS; the rest are aliases (a case
  pack code, a legacy code from a migrated system).

**`partners` module** (schema `partners`)
- `Partner` — a supplier, a customer, or both (`PartnerType` is `[Flags]`: `Customer = 1`,
  `Supplier = 2`). Unique `Code` per tenant, `Name`, optional structured `Address`
  (`Domain.Primitives.Address`, the same value object `Store` now uses — ADR-037's boundary closes
  here), optional `Email`, optional `Phone`, optional `TaxNumber`, `IsActive`. No credit terms, no
  balance, no ledger account — Stage 07 owns AR/AP against a `PartnerId` it doesn't need this stage to
  widen for.

**Application layer**
- Commands: `CreateItemCommand`, `UpdateItemDetailsCommand`, `DeactivateItemCommand`,
  `CreateItemVariantCommand`, `CreateUnitOfMeasureCommand` (base and derived), `AddBarcodeCommand`,
  `SetPrimaryBarcodeCommand`, `RemoveBarcodeCommand`, `CreatePartnerCommand`,
  `UpdatePartnerDetailsCommand`, `DeactivatePartnerCommand` — every one carries
  `[CommandSideEffect(SideEffect.Write)]` (CONVENTIONS.md §4).
- Queries: `GetItemQuery`, `ListItemsQuery` (keyset-paginated — `docs/PROGRESS.md` flagged this stage
  as the one that builds real pagination; do not ship offset paging), `GetPartnerQuery`,
  `ListPartnersQuery` (keyset), `ListUnitsOfMeasureQuery`.
- A validator per command (FluentValidation, not handler code).
- Ports (`ICatalogRepository`-shaped, one per aggregate) alongside the existing `IStoreRepository`
  pattern in `Application/Platform/PlatformPorts.cs` — follow that file's shape exactly:
  `Application/Catalog/CatalogPorts.cs`, `Application/Partners/PartnerPorts.cs`.

**Infrastructure**
- EF configurations under `Infrastructure/Persistence/Configurations/Catalog/` and `.../Partners/`,
  each entity carrying the mandatory columns via the shared base configuration (DATA_MODEL.md §1).
- One migration, reversible (`Down` tested), adding both schemas.
- Repositories + a `AddVumaCatalog()` / `AddVumaPartners()` DI extension, matching
  `PlatformServiceCollectionExtensions`.

**API**
- Versioned endpoints for both modules under the existing API platform (Stage 03), documented in
  OpenAPI with examples and error responses (CLAUDE.md §8).
- RBAC permissions registered in the identity permission catalogue: `catalog.item.create`,
  `catalog.item.update`, `catalog.item.deactivate`, `catalog.item.view`, `catalog.uom.manage`,
  `catalog.barcode.manage`, `partners.partner.create`, `partners.partner.update`,
  `partners.partner.deactivate`, `partners.partner.view` (CONVENTIONS.md §3 permission shape).
- `CatalogModuleManifest` and `PartnersModuleManifest` implementing `IModuleManifest`
  (`Application/Identity/Permissions/CoreModuleManifests.cs` is the pattern), **`IsCore = true`** —
  no retail deployment functions without a product to sell and a partner to trade with, the same
  reasoning `PlatformModuleManifest` and `IdentityModuleManifest` already record.
- No new `LimitKind`. Item and partner counts are neither a hard limit (LICENSING.md §6 names stores,
  terminals, named users, modules — a closed list) nor a soft one currently defined; do not extend
  either list speculatively.

**Sync**
- Both modules' entities carry `[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]`,
  consistent with `UnitOfMeasure`'s existing scaffold, and are added to the replication registry
  (`docs/DATA_MODEL.md` §5) and `docs/SYNC_AND_BACKUP.md`.

**Docs**
- `docs/DATA_MODEL.md` gets a new §4c `Tables in catalog` and §4d `Tables in partners`, in the same
  format as §4/§4b.
- `docs/DECISIONS.md` gets an ADR for the variant-attribute shape (value object vs. table) and one for
  why item/partner counts stay unlimited at this stage, if either turns out non-obvious enough to need
  one — CLAUDE.md §1 requires recording, not requires-manufacturing-controversy.

**Seed data**
- `scripts/seed.ps1` gains a handful of demo items (with and without variants), barcodes, base and
  derived units of measure, and a couple of partners, so the module is demonstrable per CLAUDE.md §8.

## Business rules
- Item and partner codes are unique per tenant, case-insensitive (upper-cased at creation, same shape
  as `Store.Code`).
- An item with zero variants may carry a barcode directly; the moment it has one variant, all its
  barcodes must move to variants — an item does not sell "as itself" and "as a variant" at once.
- A barcode value is unique per tenant across the whole catalog, independent of symbology.
- Exactly one primary barcode per item/variant that has any barcode at all; setting a new primary
  demotes the previous one atomically.
- A unit of measure converts at most one level (already enforced by `UnitOfMeasure.CreateDerived`);
  an item's base UoM must belong to the same tenant and be active at the time the item is created.
- Nothing here is hard-deleted. `Deactivate()` on `Item`, `ItemVariant`, `Barcode` (well, a barcode may
  be removed outright — it carries no history of its own the way an item does; record this as the one
  deliberate exception if `DECISIONS.md` gets the ADR), `Partner`, and `UnitOfMeasure` (existing).
- A `Partner` with neither `Customer` nor `Supplier` set is invalid — the flag is why the type exists.

## Tests / acceptance
- Domain: uniqueness rules, the item/variant/barcode attachment rule (barcode-on-item rejected once a
  variant exists), UoM conversion rules (existing tests extended, not replaced), partner type flag
  validation, deactivate-not-delete round trips.
- Integration: EF configuration mapping round-trips every field including `Address` and the variant
  attribute list; migration applies and reverses cleanly against Postgres (`scripts/pg-test.sh` or
  Testcontainers per `docs/TESTING.md` §2); keyset pagination returns stable pages under concurrent
  inserts (no skipped/duplicated rows across a page boundary — this is the acceptance test for the
  pagination promise `PROGRESS.md` recorded).
- API: OpenAPI examples validate; unauthorised role gets 403 with the registered permission name;
  disabling the module's licence flag (via the existing entitlement test harness) blocks the endpoints
  with `LICENCE_MODULE_NOT_ENABLED` — then re-enable and confirm `IsCore` tenants are never actually
  offered the option to disable it in the first place (module toggle UI/API refuses on a core module).
- Read-only: every write command here returns `403 LICENCE_READ_ONLY` under the read-only enforcement
  suite's generated per-module check (STAGE-04b's suite grows one row per command this stage adds; not
  a new suite).
- Architecture: no reference from `Domain.Catalog` or `Domain.Partners` to `Infrastructure` or to
  another module's schema; every new command carries a `[CommandSideEffect]`.

## Exit checklist
- [x] `UnitOfMeasure` compiles clean — `CatalogRuleException` written, dead code removed
- [x] Item, ItemVariant, Barcode, Partner built with the attachment and uniqueness rules enforced in
      the domain, not the handler
- [x] Migration applied and reversed against a real Postgres instance
- [x] Keyset pagination implemented and tested for `ListItemsQuery`/`ListPartnersQuery`
- [x] RBAC permissions registered; `CatalogModuleManifest`/`PartnersModuleManifest` registered as core
- [x] Read-only suite extended and green for every new write command
- [x] Replication registry and `docs/SYNC_AND_BACKUP.md` updated
- [x] `docs/DATA_MODEL.md` §4c/§4d written; ADRs appended where a real judgement call was made
- [x] Seed data added; `dotnet build -c Release` zero warnings in Domain/Application, zero errors
      anywhere; `dotnet test` green
- [x] `docs/PROGRESS.md` updated, committed as `feat(stage-06): master data — items, variants,
      barcodes, UoM, partners`
