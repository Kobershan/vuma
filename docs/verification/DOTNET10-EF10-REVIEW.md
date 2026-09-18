# .NET 10 / EF Core 10 static re-verification

Date: 2026-09-18

This is a source review only. The repository rules for this migration prohibit SDK commands on
this machine; the owner must perform the Release build on a .NET 10 machine.

Reviewed areas:

- `VumaRetail.Infrastructure/Persistence/Configurations/Imports/ImportsConfigurations.cs`
  ignores `ImportBatch.CommittableRows` and `ImportBatch.CompensatableRows`.
- `VumaRetail.Infrastructure/Persistence/Configurations/Pos/PosConfigurations.cs` and
  `VumaRetail.Infrastructure/Persistence/VumaRegistryDbContext.cs` ignore the computed
  `Sale.LiveLines` projection; no second relationship is configured for it.
- Computed values in field sales, finance, identity, registry and sales configurations are
  explicitly ignored rather than convention-mapped.
- Tenant/company global filters remain defined in both database contexts. Explicit filter
  bypasses are limited to repository cases that document a tenant-wide read.
- Read-only repository queries continue to use `AsNoTracking`; tracked queries are retained only
  where a command subsequently mutates the loaded aggregate.
- PostgreSQL `FOR UPDATE` statements are isolated raw SQL queries, including the return-sibling
  lock and other ledger/reservation locks. They are executed inside the existing command
  transaction rather than through a joined query.

No model snapshot or migration was generated here because that requires the prohibited EF SDK
execution. The owner’s first migration checkpoint must include a clean EF model/migration check.
