using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>
/// The document a transfer between two locations produces — a header correlating the two
/// <see cref="StockLedgerEntry"/> rows a transfer always posts, never a single row that silently
/// changes a location.
/// </summary>
/// <remarks>
/// <para>
/// R8 (multi-store, multi-warehouse) and the stage brief's own rule: a transfer is two correlated
/// ledger entries, out of the source and in at the destination, sharing this document's id as their
/// <see cref="StockLedgerEntry.ReferenceId"/>. Both post inside the same database transaction — this
/// stage builds the synchronous, single-node case (both locations served by the same store server,
/// which covers the common small-multi-store deployment); an asynchronous cross-server transfer with
/// an in-transit state and a receiving confirmation is a real extension a later stage can add without
/// changing this shape, not something Stage 08 needs to invent.
/// </para>
/// <para>
/// <see cref="UnitCost"/> is the source location's weighted-average cost at the moment of transfer,
/// carried unchanged to the destination. A transfer moves value, it does not create or destroy it —
/// valuing the destination side at anything other than what left the source would do exactly that.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class StockTransfer : Entity, IImmutableRecord
{
    private StockTransfer(
        Guid id,
        Guid tenantId,
        Guid sourceLocationId,
        Guid destinationLocationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Money unitCost,
        Guid outEntryId,
        Guid inEntryId,
        string? note)
        : base(id, tenantId, storeId: null)
    {
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Quantity = quantity;
        UnitCost = unitCost;
        OutEntryId = outEntryId;
        InEntryId = inEntryId;
        Note = note;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private StockTransfer()
    {
    }

    /// <summary>The location stock left.</summary>
    public Guid SourceLocationId { get; private set; }

    /// <summary>The location stock arrived at.</summary>
    public Guid DestinationLocationId { get; private set; }

    /// <summary>The item transferred, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant transferred.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much moved. Always positive — the ledger entries carry the sign.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>The source's weighted-average cost per unit at the moment of transfer.</summary>
    public Money UnitCost { get; private set; }

    /// <summary>The <see cref="StockLedgerEntry"/> posted at the source.</summary>
    public Guid OutEntryId { get; private set; }

    /// <summary>The <see cref="StockLedgerEntry"/> posted at the destination.</summary>
    public Guid InEntryId { get; private set; }

    /// <summary>An optional free-text note.</summary>
    public string? Note { get; private set; }

    /// <summary>The value that moved — <see cref="Quantity"/> extended by <see cref="UnitCost"/>.</summary>
    public Money Value => Quantity.Extend(UnitCost);

    /// <summary>
    /// Records a completed transfer. Called once both ledger entries have been posted — the entries are
    /// the movement; this row is the correlating document a caller lists and reads back.
    /// </summary>
    /// <param name="id">
    /// The transfer's own id — minted by the caller <em>before</em> either ledger entry is posted, so
    /// both entries can carry it as their <see cref="StockLedgerEntry.ReferenceId"/> and this document,
    /// once it exists, needs no separate correlation field to find them by.
    /// </param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="sourceLocationId">Where stock left.</param>
    /// <param name="destinationLocationId">Where stock arrived. Must differ from <paramref name="sourceLocationId"/>.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="quantity">How much moved. Must be positive.</param>
    /// <param name="unitCost">The source's average cost per unit, carried to the destination unchanged.</param>
    /// <param name="outEntryId">The ledger entry posted at the source.</param>
    /// <param name="inEntryId">The ledger entry posted at the destination.</param>
    /// <param name="note">An optional free-text note.</param>
    public static StockTransfer Record(
        Guid id,
        Guid tenantId,
        Guid sourceLocationId,
        Guid destinationLocationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        Money unitCost,
        Guid outEntryId,
        Guid inEntryId,
        string? note)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A transfer must be given a pre-minted id.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A transfer must belong to a tenant.", nameof(tenantId));
        }

        if (sourceLocationId == Guid.Empty || destinationLocationId == Guid.Empty)
        {
            throw new ArgumentException("A transfer must name both a source and a destination location.");
        }

        if (sourceLocationId == destinationLocationId)
        {
            throw InventoryRuleException.TransferLocationsMustDiffer();
        }

        StockItemReference.Validate(itemId, itemVariantId);

        if (quantity.IsNegative || quantity.IsZero)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        return new StockTransfer(
            id,
            tenantId,
            sourceLocationId,
            destinationLocationId,
            itemId,
            itemVariantId,
            quantity,
            unitCost,
            outEntryId,
            inEntryId,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());
    }
}
