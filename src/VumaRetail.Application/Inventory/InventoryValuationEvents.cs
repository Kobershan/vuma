using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory;

/// <summary>
/// The one financial fact a stock movement raises: value moved, at this location, for this
/// stock-keeping unit, of this kind, worth this much.
/// </summary>
/// <remarks>
/// <para>
/// <c>CLAUDE.md</c> §7 rule 12: no module names a GL account. This event says <em>what happened</em> —
/// it has no field a GL account code could go in, and nothing in this module resolves one. Stage 07's
/// posting rules engine, when it exists, is the only thing that turns "a receipt worth R450 landed at
/// Sandton warehouse" into a debit and a credit.
/// </para>
/// <para>
/// One event per <see cref="StockLedgerEntry"/> posted — deliberately the same cardinality, so a
/// listener can always correlate one back to the other by <see cref="LedgerEntryId"/> without this
/// event needing to carry anything the ledger row does not already say.
/// </para>
/// </remarks>
/// <param name="TenantId">The tenant the movement belongs to.</param>
/// <param name="StoreId">The owning store, or <c>null</c> for a tenant-wide location.</param>
/// <param name="LocationId">Where the movement happened.</param>
/// <param name="LedgerEntryId">The <see cref="StockLedgerEntry"/> this event reports.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="MovementType">What kind of movement this is.</param>
/// <param name="Quantity">The signed quantity delta.</param>
/// <param name="Value">The signed monetary value of the movement — <see cref="Quantity"/> extended by its unit cost.</param>
/// <param name="ReferenceType">What kind of document this correlates to, if any.</param>
/// <param name="ReferenceId">The correlating document's id.</param>
/// <param name="OccurredAt">When the movement was posted, UTC.</param>
public sealed record InventoryValuationEvent(
    Guid TenantId,
    Guid? StoreId,
    Guid LocationId,
    Guid LedgerEntryId,
    Guid? ItemId,
    Guid? ItemVariantId,
    StockMovementType MovementType,
    Quantity Quantity,
    Money Value,
    StockReferenceType ReferenceType,
    Guid? ReferenceId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Where inventory raises the financial event a stock movement produces, for whatever eventually turns
/// it into a balanced journal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deferred integration point.</b> Stage 08 formally depends on Stage 07 (Finance/GL posting), which
/// is not on <c>main</c> yet (see <c>docs/PROGRESS.md</c>). Rather than block on it, this stage builds
/// self-contained and raises through this interface — the same shape Stage 04b used for the control
/// plane client and the S3 backup vault (a working port, a tested default implementation, the real
/// integration wired later). <c>Infrastructure</c>'s default binding is
/// <c>LoggingInventoryValuationEventPublisher</c>: it records that a valuation event was raised and does
/// nothing else. When Stage 07's posting rules engine lands, it registers the real implementation —
/// turning each event into a balanced journal against the chart of accounts — behind this exact
/// interface, and nothing in Stage 08's command handlers changes.
/// </para>
/// <para>
/// This is a sequencing gap, not a credentials one: unlike the control plane or the backup vault, there
/// is no external service to eventually point at, only code that has not been built yet on a separate
/// branch. It is recorded in <c>docs/PROGRESS.md</c> all the same, because "the real implementation does
/// not exist yet" is exactly the situation that section exists to track honestly.
/// </para>
/// </remarks>
public interface IInventoryValuationEventPublisher
{
    /// <summary>Raises one valuation event.</summary>
    /// <param name="valuationEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task PublishAsync(InventoryValuationEvent valuationEvent, CancellationToken cancellationToken = default);
}
