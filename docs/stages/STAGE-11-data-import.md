# STAGE 11 — Data Import: Excel, CSV and PDF ingestion with mapping, preview and rollback

**Status:** DONE (2026-08-16), with one exit-checklist item outstanding — see `PROGRESS.md` · **Depends on:** 06 (master data), and reads 08's ledger and 10's price
lists as targets · **Reference reading:** `docs/IMPORT_PIPELINE.md` (written by this stage),
`docs/DATA_MODEL.md` §1–§3, `docs/CONVENTIONS.md` §1–§5, `docs/API_STANDARDS.md` §3–§5 and §9
(idempotency), `docs/TESTING.md` §2–§3, `CLAUDE.md` §7 rules 2, 6, 7 and 8, ADR-065 (document
numbering), ADR-068 (weighted-average cost), ADR-070 (a missing posting rule does not refuse a
movement), **ADR-076**–**ADR-079** (the decisions this stage makes).

## Task index
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) for project and boundary rules, then load only the references named by the eventual task.

**Architecture checklist:** WHAT/WHY come from this stage's Objective; affected layers/components come from its Deliverables; data, API, security, multi-company, synchronization, licensing, and testing rules come from the linked authority documents. Missing answers are **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs in the header apply; a new ADR is required only for a new decision. Nothing outside stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 11-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in docs/tasks/README.md.

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-11-001 | Verify data-import stage gaps | Stage 06; Stages 08/10 targets | NEEDS_VERIFICATION |

## Objective

R5 is one line in `CLAUDE.md` §3 and it is the difference between a system somebody can start using
and a system somebody has to type their whole business into: *"Ingest from Excel/CSV/PDF for
suppliers, customers, inventory and specials — with mapping, preview, validation and rollback."*

Every one of those four words is load-bearing, and the stage is built around them rather than around
the file formats:

- **Mapping** — the shop's file has the columns the shop's file has. Nobody is going to rewrite a
  supplier's price list into our column order, so the *mapping* is data, it is saved, and the same
  supplier's file next month maps itself.
- **Preview** — an import that writes first and shows you afterwards is not an import, it is an
  accident. Nothing touches a business table until somebody has seen the rows.
- **Validation** — per row, not per file. One bad barcode on line 400 must not cost you the other
  399, and it must tell you it was line 400.
- **Rollback** — the promise that makes the other three safe to use. It is also the hardest, because
  §7 rule 6 says stock is never a mutable column and rule 8 says nothing is hard-deleted, so a
  rollback cannot simply be a `DELETE`. See ADR-076.

## What this stage does not own

- **No new master data concepts.** An imported supplier is a Stage 06 `Partner` built by
  `Partner.Create`, not a second kind of supplier. The import is a *caller*, and ADR-079 is the rule
  that keeps it one.
- **No GL account, anywhere** (rule 12). An opening-stock import raises a stock movement; Stage 07's
  posting rules engine decides the accounts, exactly as Stage 08's adjustments do.
- **No promotions import.** R5 says "specials", and the special a shop actually receives in a file is
  a *price* — a promotional price list with an effective window. A `Promotion` is a reward *rule*
  (buy-two-get-one, basket-percentage, happy hour), and a rule is not a row of values. Importing
  price-list lines onto a dated, promotional list covers what a supplier's specials sheet contains;
  the rule engine stays Stage 10's, configured by hand.
- **No OCR.** PdfPig extracts text from machine-generated PDFs, which is what a supplier price list
  is. A *scanned* PDF needs Tesseract and a trained data file, which is a native dependency this
  build cannot acquire — it is stubbed behind `IOcrTextExtractor` per `CLAUDE.md` §1 and recorded in
  `PROGRESS.md` under "Deferred". A scanned PDF is refused with a clear code, never silently
  imported as zero rows.
- **No import screen.** Every deliverable is an endpoint, per R3, for the same Windows/WPF reason
  Stage 09 and Stage 10 stopped where they did (`PROGRESS.md` §4.3, ADR-031). The field catalogue
  endpoint exists precisely so the eventual screen can build its mapping UI without hard-coding
  anything.

## Deliverables

**`imports` module** (schema `imports`, four tables)

- `ImportBatch` — the aggregate root and the state machine. `BatchNumber` from ADR-065's sequence
  (series `IMP`), `TargetKind`, `SourceFormat`, `FileName`, `ContentHash` (SHA-256 of the bytes),
  `SizeBytes`, `Status`, `DuplicateStrategy`, the six row counters, and the who/when of commit and
  rollback. Owns its rows and its column mappings.
- `ImportColumnMapping` — one source column bound to one target field, with an optional constant
  default for a column the file does not have at all (a supplier file with no `currency` column, for
  a tenant that trades in one currency).
- `ImportRow` — one source row, and the single most important table in the module. Carries the raw
  values as `jsonb`, its own status, its own error list, the id of the entity it produced, what it
  did to that entity (`Created`/`Updated`/`Skipped`), and — the part rollback is built on — the
  **before-image** of the entity it updated, also `jsonb`.
- `ImportMappingTemplate` — a saved mapping, keyed by target kind and a **source signature** (a hash
  of the normalised header row). A file whose headers match a template is mapped on upload with no
  human step at all.

**Readers** (`IImportSourceReader`, one per format, selected by `SourceFormat`)

- `CsvImportSourceReader` — a hand-written RFC 4180 reader (ADR-077): quoted fields, embedded commas,
  embedded newlines, doubled quotes, CRLF or LF, BOM, and a configurable delimiter so a
  semicolon-separated European export works.
- `ExcelImportSourceReader` — ClosedXML, the `CLAUDE.md` §4 locked choice. First worksheet by
  default, named worksheet by option, formulas read as their cached values, dates read as dates
  rather than as serial numbers.
- `PdfImportSourceReader` — PdfPig words plus their bounding boxes, clustered into rows by baseline
  and into columns by x-position. Machine-generated, aligned supplier price lists only; a PDF with no
  extractable text is refused as `IMPORTS_PDF_HAS_NO_TEXT_LAYER`.

**Targets** (`IImportTargetHandler`, one per `TargetKind`, five of them)

`Suppliers` · `Customers` · `Items` · `StockOnHand` · `PriceListLines`

Each declares its **field catalogue** — name, type, required, description, and the aliases a header
is matched against — which is what `GET /api/v1/imports/targets` serves and what auto-mapping uses.

**The pipeline** (`docs/IMPORT_PIPELINE.md` describes it in full)

```
Uploaded ─ parse ─→ Parsed ─ map ─→ Mapped ─ validate ─→ Validated ─ commit ─→ Committed
                                                                                   │
   └──────────────── discard ───────────────→ Discarded            rollback ───────┘
                                                                                   ↓
                                                                             RolledBack
```

**Commands** (all classified, ADR-078 makes the classification sweep see them)

`CreateImportBatchCommand` · `SetImportMappingCommand` · `ValidateImportBatchCommand` ·
`CommitImportBatchCommand` · `RollbackImportBatchCommand` · `DiscardImportBatchCommand` ·
`SaveImportMappingTemplateCommand` · `DeleteImportMappingTemplateCommand`

**Queries**

`GetImportBatchQuery` · `ListImportBatchesQuery` · `GetImportPreviewQuery` (paged, the preview) ·
`ListImportTargetsQuery` (the field catalogue) · `ListImportMappingTemplatesQuery`

**Endpoints** — `/api/v1/imports/...`, 13 operations, every one in `/openapi/v1.json`.

**Permissions** — `imports.batch.view`, `imports.batch.create`, `imports.batch.commit` (high risk),
`imports.batch.rollback` (high risk), `imports.template.manage`.

**Module manifest** — `imports`, `IsCore => false`. A shop that keys its own data in never buys this.

## Business rules

1. **Nothing touches a business table before `Commit`.** Upload, parse, map and validate write only
   inside the `imports` schema. This is what makes preview honest.
2. **A row is the unit of success.** A commit applies every valid row and skips every invalid one; it
   does not fail the batch because row 400 is bad. The batch's counters are the report.
3. **A commit is one transaction.** Rule 2 is about *validity*, not about partial writes: if the
   database refuses the 400th valid row, no row is committed. Half an import is the one outcome
   rollback cannot describe.
4. **A batch commits once.** `Status` is checked inside the transaction, and the endpoint takes an
   idempotency key (`API_STANDARDS.md` §9), so a retried upload of the same commit is the same commit.
5. **Rollback is compensating, never destructive (ADR-076).** A created row is soft-deleted; an
   updated row is restored from its before-image; a stock movement is reversed by a new ledger entry,
   never removed (rule 6).
6. **A rollback that cannot be complete is refused.** If any created entity has since been referenced
   by another document — the imported item has been sold, the imported partner has an invoice — the
   whole rollback is refused, naming the blocking rows. A partial rollback would leave a sale
   pointing at a deleted item, which is worse than no rollback at all.
7. **The import writes through the domain, never through the DbContext (ADR-079).** A target handler
   builds entities with the same `Create` factories the command handlers use, so every invariant that
   protects the API protects the import. It does not call another command's handler
   (`CONVENTIONS.md` §4).
8. **Duplicates are a declared strategy, not a surprise.** `Skip`, `Update` or `Fail`, chosen on the
   batch, applied per row against the target's natural key (partner code, item SKU, barcode).
9. **Money and quantity are parsed once, at the edge.** A cell becomes `Money` or `Quantity` during
   validation and is stored parsed on the row; the target handler never re-parses a string
   (`CONVENTIONS.md` §6, and it is how §4.14's double-rounding happened elsewhere).
10. **An opening-stock import is an adjustment, not a truth.** `StockOnHand` writes through Stage
    08's `IStockLedgerPoster` as a counted adjustment with the batch as its reference, so the ledger
    explains where the number came from and ADR-068's weighted-average cost is maintained.

## Tests / acceptance

- **Readers** — RFC 4180 corner cases (quoted comma, quoted newline, doubled quote, BOM, CRLF,
  ragged rows, empty trailing line); an `.xlsx` built in-test by ClosedXML and read back, including a
  date cell, a formula cell and a numeric cell that looks like text; a PDF built in-test and read
  back; a PDF with no text layer refused with the documented code.
- **Mapping** — auto-map by header alias; a template matched by source signature; a required target
  field left unmapped refusing the transition to `Mapped`.
- **Validation** — per-row errors carry the row number and the field; a bad row does not invalidate
  its neighbours; a decimal in the wrong locale (`1 234,56`) is reported rather than silently read as
  1.
- **Commit** — the five targets each create and each update; `Skip`/`Update`/`Fail` all behave;
  committing twice is refused; a database failure on the last row leaves nothing written (rule 3).
- **Rollback** — created partners soft-deleted; updated items restored to their before-image byte for
  byte; a stock import reversed by a compensating ledger entry with the balance back where it
  started; a rollback blocked by a sale against an imported item refused with the blocking row listed.
- **Architecture** — the new commands are classified for the read-only guard, and the sweep that
  proves it is now derived from the module manifests rather than hand-listed (ADR-078, closes
  `PROGRESS.md` §4.16); no cross-schema foreign key out of `imports`; the new entities are in the
  replication registry.
- Coverage ≥ 80% line on the stage's Domain + Application code (§8).

## Exit checklist

- [x] `dotnet build -c Release` — 0 warnings, 0 errors
- [x] Full suite green with the new tests, 0 skipped — 995 tests (607 unit, 33 architecture, 355 integration)
- [x] EF migration generated and applied; `Down` tested reversible — `20260816130335_Imports`, exercised by `MigrationTests`
- [x] All new endpoints in `/openapi/v1.json`, verified against a booted host — `ApiContractTests` boots the host and asserts every mapped route is described
- [x] RBAC permissions registered; `ImportsModuleManifest` registered
- [x] Stock written through `IStockLedgerPoster`; no GL account named
- [x] Entitlement flag declared (`imports`, non-core); metering counter follows from the schema
- [x] Replicated entities registered and `docs/SYNC_AND_BACKUP.md` updated in the same commit
- [x] `docs/DATA_MODEL.md` §4i and `docs/IMPORT_PIPELINE.md` written; ADR-076–ADR-079 appended (plus ADR-080)
- [x] Seed data — one committed import and one rolled-back import, both demonstrable
- [ ] **All six agents run** — **NOT DONE.** The session that completed this stage was not authorised to
      spawn subagents, so this remains outstanding exactly as it did for Stage 10 (`PROGRESS.md` §4.17).
      It must be run before Stage 11 is trusted as DONE. Two defects were nonetheless found and fixed
      here by writing the integration tests the stage asked for — see the session log.
- [x] `docs/PROGRESS.md` updated, committed as `feat(stage-11): data import`

## Notes for later stages

- **Stage 12 (procurement)** wants a supplier price-list import on a schedule rather than by upload.
  The reader and target seams are the same; what it adds is a fetch. Do not fork the pipeline.
- **Stage 21b (Vuma Connect)** is this stage's opposite: a supplier *publishes* prices rather than a
  retailer importing a file. `IImportTargetHandler` is the shape a Connect proposal should reuse —
  the retailer accepting a proposal is a commit, and it should be rollback-able for the same reasons.
- **`ImportRow.BeforeImage` is a general before-image mechanism** that only this module uses so far.
  If a later stage needs undo, extend it rather than inventing a second one.
