namespace VumaRetail.Infrastructure.Persistence;

/// <summary>
/// The PostgreSQL schema names. One schema per module (ADR-010, <c>CONVENTIONS.md</c> §2).
/// </summary>
/// <remarks>
/// <para>
/// Schemas are the seam the modular monolith is cut along. A module owns its schema, and no module
/// draws a foreign key into another's — that constraint is what keeps a module extractable later, and
/// an architecture test enforces it.
/// </para>
/// <para>
/// Names are constants rather than strings at each call site so a rename is one edit, and so a typo
/// creating a stray schema is a compile error instead of a migration nobody notices.
/// </para>
/// </remarks>
public static class Schemas
{
    /// <summary>Tenants, stores, the audit trail — the things every other schema depends on.</summary>
    public const string Platform = "platform";

    /// <summary>Users, roles, permissions, terminals. Stage 02.</summary>
    public const string Identity = "identity";

    /// <summary>Outbox, inbox, sync cursors, conflict review queue. Stage 04.</summary>
    public const string Sync = "sync";

    /// <summary>
    /// Snapshot ledger for the cloud backup vault (R4). Stage 04.
    /// </summary>
    /// <remarks>
    /// Its own schema rather than a table in <see cref="Sync"/>. Replication and backup answer
    /// different questions — "is head office up to date" and "can this store be rebuilt" — and a
    /// store with no cloud peer still takes backups. Sharing a schema would tie the two together in
    /// the one place where being able to separate them matters: a disaster recovery.
    /// </remarks>
    public const string Backup = "backup";

    /// <summary>Licences, leases, activations, entitlements, metering. Stage 04b.</summary>
    public const string Licensing = "licensing";

    /// <summary>Items, item variants, barcodes and units of measure. Stage 06.</summary>
    public const string Catalog = "catalog";

    /// <summary>Suppliers, customers, and partners who are both. Stage 06.</summary>
    public const string Partners = "partners";

    /// <summary>Chart of accounts, GL, AR, AP, banking, tax, posting rules. Stage 07.</summary>
    public const string Finance = "finance";

    /// <summary>
    /// Stock locations, the append-only stock ledger, balances, transfers and stocktakes. Stage 08.
    /// </summary>
    public const string Inventory = "inventory";

    /// <summary>
    /// Till sessions, sales, sale lines, tenders and the receipt print log. Stage 09.
    /// </summary>
    /// <remarks>
    /// Declaring the schema is also what puts POS into the daily metering rollup: usage is grouped on
    /// the schema half of <c>schema.table</c> from the audit trail (<c>UsageCounterSource</c>), so a
    /// module gets its counter by existing rather than by writing one. Rule 16 stays true — the count
    /// is a count, and no business row is read to produce it.
    /// </remarks>
    public const string Pos = "pos";

    /// <summary>
    /// Price lists, promotions, returns and the price override log. Stage 10.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Pos"/> rather than folded into it, even though the till is the loudest
    /// caller of both. A price list is not a till concept — a quote, a lay-by, an ecommerce basket and
    /// a wholesale invoice are all priced off the same rows, and a shop with no cash drawer still has
    /// prices. Sharing POS's schema would make every one of those later modules depend on the till.
    /// </remarks>
    public const string Sales = "sales";
}
