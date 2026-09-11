using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>What kind of document a <see cref="StockReservation"/> holds stock for.</summary>
public enum ReservationSource
{
    /// <summary>A Stage 14 sales order (or a TASK-08C-002 sourcing commit acting for one).</summary>
    Order = 0,

    /// <summary>An approved Stage 14b pro forma (ADR-108: approval reserves).</summary>
    ProFormaApproval = 1,

    /// <summary>A stock transfer reserving the source side before it moves.</summary>
    Transfer = 2,

    /// <summary>Goods packed and awaiting shipment — held so nothing else promises them.</summary>
    Shipment = 3,

    /// <summary>A Stage 10b lay-by agreement: reserved, never sold, until final payment (ADR-055).</summary>
    LayBy = 4,

    /// <summary>A Stage 10b stokvel hamper basket: December stock held from reservation day so a
    /// 200-member payout wave cannot read as sellable in November (ADR-055).</summary>
    StokvelHamper = 5,

    /// <summary>A Stage 09b mixed-basket trading session: each segment's lines held in their
    /// owning company from completion-leg start, consumed on posting, released on void or
    /// compensation (ADR-125).</summary>
    MixedBasket = 6,
}

/// <summary>Where a <see cref="StockReservation"/> row sits in its chain.</summary>
/// <remarks>
/// A reservation's <em>current</em> state is its latest row. Exactly one <c>Held</c> row may exist
/// per <c>ReservationId</c> at any time (partial unique index); a terminal row (<c>Consumed</c>,
/// <c>Released</c>, <c>Expired</c>) closes the chain and nothing may follow it.
/// </remarks>
public enum ReservationState
{
    /// <summary>Available is reduced by this row's quantity; on-hand is untouched (ADR-103).</summary>
    Held = 0,

    /// <summary>The held quantity shipped or issued against <c>ConsumedByReferenceId</c>.</summary>
    Consumed = 1,

    /// <summary>The hold was given up; available is restored. A new row, never an edit.</summary>
    Released = 2,

    /// <summary>The hold lapsed under the tenant's expiry policy; available is restored.</summary>
    Expired = 3,
}

/// <summary>
/// One immutable, append-only record of a hold on stock: a promise that a stated quantity of a
/// stated stock-keeping unit at a stated location in <em>this</em> company's database is spoken
/// for and must not be sold twice.
/// </summary>
/// <remarks>
/// <para>
/// ADR-103 and <c>CLAUDE.md</c> §7 rule 6: a reservation reduces <em>available</em>
/// (<c>OnHand − Reserved − InStaging</c>) immediately and leaves <em>on-hand</em> alone until
/// goods physically move through <see cref="StockLedgerEntry"/>. Correcting a hold therefore
/// means appending a terminal row (<see cref="Release"/>, <see cref="Consume"/>,
/// <see cref="Expire"/>), the same way a financial document is amended by a new document
/// rather than an edit — enforced structurally by <see cref="IImmutableRecord"/>.
/// </para>
/// <para>
/// No reservation ever spans companies: an order drawing on two companies' stock holds two
/// reservations, one in each database, tied together only by <see cref="GroupDocumentRef"/>
/// (ADR-103, <c>docs/MULTI_COMPANY.md</c> §4). Cross-company commitment is TASK-08C-002's saga;
/// this entity is always written inside one company's own database.
/// </para>
/// <para>
/// <see cref="IntentId"/>/<see cref="LegId"/> carry the saga leg that took this hold, when one
/// did. Retrying a leg replays the same (intent, leg, line) triple, and the partial unique index
/// on <c>(intent_id, leg_id, location_id, item or variant)</c> turns the replay into a lookup hit
/// rather than a double hold (ADR-116: legs are idempotent). A leg holds at most one row per
/// line; a re-sourced remainder is a plain hold under the same group reference, never a second
/// row on the leg's key.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class StockReservation : Entity, IImmutableRecord
{
    private StockReservation(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        ReservationSource source,
        Guid sourceDocumentId,
        string? groupDocumentRef,
        Guid reservationId,
        int sequenceNumber,
        ReservationState state,
        DateTimeOffset? expiresAt,
        Guid? intentId,
        Guid? legId,
        Guid? consumedByReferenceId,
        string? reason,
        string? batchReference,
        DateOnly? expiryDate,
        string? serialNumber)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Quantity = quantity;
        Source = source;
        SourceDocumentId = sourceDocumentId;
        GroupDocumentRef = groupDocumentRef;
        ReservationId = reservationId;
        SequenceNumber = sequenceNumber;
        State = state;
        ExpiresAt = expiresAt;
        IntentId = intentId;
        LegId = legId;
        ConsumedByReferenceId = consumedByReferenceId;
        Reason = reason;
        BatchReference = batchReference;
        ExpiryDate = expiryDate;
        SerialNumber = serialNumber;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private StockReservation()
    {
    }

    /// <summary>The location the held stock sits at.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item held, or <c>null</c> when a variant is held instead.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant held, or <c>null</c> when an item is held directly.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>
    /// How much this row holds (for <c>Held</c>) or closes (for a terminal row, always the full
    /// held quantity — a reservation is settled in full, never partially, so a chain can never
    /// disagree with itself about what remains).
    /// </summary>
    public Quantity Quantity { get; private set; }

    /// <summary>What kind of document this hold belongs to.</summary>
    public ReservationSource Source { get; private set; }

    /// <summary>The document this hold belongs to — an order, an approval, a transfer.</summary>
    public Guid SourceDocumentId { get; private set; }

    /// <summary>
    /// The cross-company order this hold belongs to, when one does — shared by every leg's holds
    /// across every company's database. A bare reference, never a cross-database foreign key.
    /// </summary>
    public string? GroupDocumentRef { get; private set; }

    /// <summary>
    /// The logical reservation this row belongs to — minted once by <see cref="Hold"/> and shared
    /// by the hold and its single terminal row.
    /// </summary>
    public Guid ReservationId { get; private set; }

    /// <summary>Zero for the hold, one for its terminal row. A chain never has a third row.</summary>
    public int SequenceNumber { get; private set; }

    /// <summary>Where this row sits in its chain.</summary>
    public ReservationState State { get; private set; }

    /// <summary>When the hold lapses, or <c>null</c> for a hold that never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>The saga intent that took this hold, when a saga did.</summary>
    public Guid? IntentId { get; private set; }

    /// <summary>The saga leg that took this hold, when a saga did.</summary>
    public Guid? LegId { get; private set; }

    /// <summary>What the hold was consumed by — a shipment, a sale issue — set only on <c>Consumed</c>.</summary>
    public Guid? ConsumedByReferenceId { get; private set; }

    /// <summary>Why the hold was taken, released or expired. Operator text, capped and trimmed.</summary>
    public string? Reason { get; private set; }

    /// <summary>Optional batch or lot identity held by this reservation.</summary>
    public string? BatchReference { get; private set; }

    /// <summary>Optional expiry date carried by the batch identity.</summary>
    public DateOnly? ExpiryDate { get; private set; }

    /// <summary>Optional serial identity held by this reservation.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>Takes a new hold. The one way a reservation chain comes to exist.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store — the location's own.</param>
    /// <param name="companyId">The owning company. Must be set: a hold with no company answers "can I sell this" for nobody.</param>
    /// <param name="locationId">The location the held stock sits at.</param>
    /// <param name="itemId">The item, when it has no variants. Exactly one of this and <paramref name="itemVariantId"/> must be set.</param>
    /// <param name="itemVariantId">The variant. Exactly one of this and <paramref name="itemId"/> must be set.</param>
    /// <param name="quantity">How much to hold. Must be positive — holding nothing reserves nothing and explains nothing.</param>
    /// <param name="source">What kind of document this hold belongs to.</param>
    /// <param name="sourceDocumentId">The document's id.</param>
    /// <param name="groupDocumentRef">The cross-company order reference, when one exists.</param>
    /// <param name="expiresAt">When the hold lapses, or <c>null</c> for a hold that never expires.</param>
    /// <param name="intentId">The saga intent taking this hold, when a saga does.</param>
    /// <param name="legId">The saga leg taking this hold, when a saga does. Required with <paramref name="intentId"/>.</param>
    /// <param name="reason">Why the hold was taken.</param>
    /// <param name="batchReference">Optional batch or lot identity.</param>
    /// <param name="expiryDate">Optional expiry date.</param>
    /// <param name="serialNumber">Optional serial identity.</param>
    /// <exception cref="InventoryRuleException">A structural invariant was broken.</exception>
    public static StockReservation Hold(
        Guid tenantId,
        Guid? storeId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity quantity,
        ReservationSource source,
        Guid sourceDocumentId,
        string? groupDocumentRef = null,
        DateTimeOffset? expiresAt = null,
        Guid? intentId = null,
        Guid? legId = null,
        string? reason = null,
        string? batchReference = null,
        DateOnly? expiryDate = null,
        string? serialNumber = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A reservation must belong to a tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A reservation must belong to a company.", nameof(companyId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A reservation must name a location.", nameof(locationId));
        }

        StockItemReference.Validate(itemId, itemVariantId);

        if (quantity.IsNegative || quantity.IsZero)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        if (sourceDocumentId == Guid.Empty)
        {
            throw new ArgumentException("A reservation must name its source document.", nameof(sourceDocumentId));
        }

        if (intentId.HasValue != legId.HasValue)
        {
            throw new ArgumentException("A saga hold names both its intent and its leg, or neither.");
        }

        if (expiresAt.HasValue && expiresAt.Value == default)
        {
            throw InventoryRuleException.ReservationExpiryInvalid();
        }

        (batchReference, expiryDate, serialNumber) = StockTracking.Validate(
            quantity.Value, batchReference, expiryDate, serialNumber);

        var reservation = new StockReservation(
            tenantId,
            storeId,
            locationId,
            itemId,
            itemVariantId,
            quantity,
            source,
            sourceDocumentId,
            NormaliseRef(groupDocumentRef),
            UuidV7.NewGuid(),
            sequenceNumber: 0,
            ReservationState.Held,
            expiresAt,
            intentId,
            legId,
            consumedByReferenceId: null,
            NormaliseReason(reason), batchReference, expiryDate, serialNumber);
        reservation.AssignCompany(companyId);
        return reservation;
    }

    /// <summary>
    /// Consumes a held reservation — the held quantity shipped or issued. A new row; the held row
    /// is never touched.
    /// </summary>
    /// <param name="consumedByReferenceId">What consumed it — a shipment, a sale issue.</param>
    /// <param name="consumedAt">When it was consumed. Carried for audit symmetry; the row's own stamp is authoritative.</param>
    /// <exception cref="InventoryRuleException">The row is not currently held.</exception>
    public StockReservation Consume(Guid consumedByReferenceId, DateTimeOffset consumedAt)
    {
        if (State != ReservationState.Held)
        {
            throw InventoryRuleException.ReservationNotHeld(ReservationId, State);
        }

        if (consumedByReferenceId == Guid.Empty)
        {
            throw new ArgumentException("A consumption must name what consumed the hold.", nameof(consumedByReferenceId));
        }

        _ = consumedAt;

        return Next(
            ReservationState.Consumed,
            consumedByReferenceId: consumedByReferenceId,
            reason: Reason);
    }

    /// <summary>
    /// Releases a held reservation — the hold is given up and available is restored. A new row;
    /// the held row is never touched.
    /// </summary>
    /// <param name="reason">Why the hold was released.</param>
    /// <exception cref="InventoryRuleException">The row is not currently held.</exception>
    public StockReservation Release(string? reason = null)
    {
        if (State != ReservationState.Held)
        {
            throw InventoryRuleException.ReservationNotHeld(ReservationId, State);
        }

        return Next(ReservationState.Released, consumedByReferenceId: null, reason: reason ?? Reason);
    }

    /// <summary>
    /// Expires a held reservation whose time has come. A new row; the held row is never touched.
    /// </summary>
    /// <exception cref="InventoryRuleException">The row is not currently held.</exception>
    public StockReservation Expire()
    {
        if (State != ReservationState.Held)
        {
            throw InventoryRuleException.ReservationNotHeld(ReservationId, State);
        }

        return Next(ReservationState.Expired, consumedByReferenceId: null, reason: Reason);
    }

    private StockReservation Next(ReservationState state, Guid? consumedByReferenceId, string? reason)
    {
        var next = new StockReservation(
            TenantId,
            StoreId,
            LocationId,
            ItemId,
            ItemVariantId,
            Quantity,
            Source,
            SourceDocumentId,
            GroupDocumentRef,
            ReservationId,
            SequenceNumber + 1,
            state,
            ExpiresAt,
            IntentId,
            LegId,
            consumedByReferenceId,
            NormaliseReason(reason), BatchReference, ExpiryDate, SerialNumber);
        if (CompanyId.HasValue)
        {
            next.AssignCompany(CompanyId.Value);
        }

        return next;
    }

    private static string? NormaliseRef(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormaliseReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        string trimmed = reason.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }
}
