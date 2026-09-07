using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory;

/// <summary>One candidate source for a demand line, from the group projection. Planning only.</summary>
/// <param name="CompanyId">The candidate company.</param>
/// <param name="CompanyCode">The candidate company's code (tie-breaks, display).</param>
/// <param name="LocationId">The candidate location.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Available">What the projection says is available there.</param>
/// <param name="AsAt">When the figure was published.</param>
/// <param name="IsStale">Whether the figure is past the freshness threshold.</param>
public sealed record SourcingCandidate(
    Guid CompanyId,
    string CompanyCode,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Available,
    DateTimeOffset AsAt,
    bool IsStale);

/// <summary>What the sourcing strategy plans from: demands, candidates, and ordering context.</summary>
/// <param name="Demands">The order lines asking for stock, in order.</param>
/// <param name="Candidates">Group availability per stock-keeping unit, planning only.</param>
/// <param name="OrderingCompanyId">The company the order was captured against. Planned first.</param>
/// <param name="ProximityLocations">Location ids nearest-first, when the caller knows geography. Empty means none.</param>
public sealed record SourcingPlanRequest(
    IReadOnlyList<SourcingDemandLine> Demands,
    IReadOnlyList<SourcingCandidate> Candidates,
    Guid OrderingCompanyId,
    IReadOnlyList<Guid> ProximityLocations);

/// <summary>Decides which company and location supplies how much of each line (ADR-102: plan).</summary>
/// <remarks>
/// Pure function of its inputs — no database, no clock — so the stage's own numbers (12+8,
/// 15+5-backorder) are unit tests, not integration tests. The plan is a projection until
/// <c>ISourcingCommitService</c> commits it; a stale candidate may cause a re-plan at commit but
/// never a negative, because nothing here writes stock.
/// </remarks>
public interface ICompanySourcingStrategy
{
    /// <summary>Plans sourcing for every demand line.</summary>
    SourcingPlan Plan(SourcingPlanRequest request);
}

/// <summary>The source order a commit splits, as carried by the caller (Stage 14 owns the truth).</summary>
/// <param name="OrderId">The source order's id, carried as the holds' source document.</param>
/// <param name="OrderNumber">The source order number — every split segment shares it as <c>GroupDocumentRef</c>.</param>
/// <param name="PartnerId">The customer, when one was identified.</param>
/// <param name="Channel">Where the order was taken.</param>
/// <param name="FulfilmentType">How the goods reach the customer.</param>
/// <param name="DeliveryAddress">Where the goods go, for delivery orders.</param>
/// <param name="Currency">The order's currency. Every demand line is priced in it.</param>
/// <param name="Demands">The lines asking for stock, with captured economics.</param>
public sealed record SourcingSourceOrder(
    Guid OrderId,
    string OrderNumber,
    Guid? PartnerId,
    SalesChannel Channel,
    OrderFulfilmentType FulfilmentType,
    Address? DeliveryAddress,
    string Currency,
    IReadOnlyList<SourcingDemandLine> Demands);

/// <summary>Asks for a sourcing commit: plan from the group view, commit per company as a saga.</summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="OrderingCompanyId">The company the order was captured against.</param>
/// <param name="Source">The source order and its lines.</param>
/// <param name="IdempotencyKey">Stable across retries of the same commit (the source order id is the natural key).</param>
/// <param name="ProximityLocations">Location ids nearest-first, when the caller knows geography.</param>
/// <param name="InitiatedBy">Who asked, in audit-principal form.</param>
public sealed record SourcingCommitRequest(
    Guid TenantId,
    Guid OrderingCompanyId,
    SourcingSourceOrder Source,
    string IdempotencyKey,
    IReadOnlyList<Guid> ProximityLocations,
    string InitiatedBy);

/// <summary>One leg's outcome: what a company actually held, and what it could not.</summary>
/// <param name="CompanyId">The leg's company.</param>
/// <param name="LegId">The leg.</param>
/// <param name="Held">Reservation ids taken, per demand line id. Missing lines were fully backordered.</param>
/// <param name="Shortfalls">Uncovered quantity per demand line id after re-sourcing.</param>
public sealed record SourcingLegOutcome(
    Guid CompanyId,
    Guid LegId,
    IReadOnlyDictionary<Guid, Guid> Held,
    IReadOnlyDictionary<Guid, Quantity> Shortfalls);

/// <summary>A committed sourcing plan: holds taken, backorders decided, documents written.</summary>
/// <param name="IntentId">The saga intent that coordinated the commit.</param>
/// <param name="Plan">The plan as committed (allocations reduced to what was actually held).</param>
/// <param name="Legs">One outcome per leg.</param>
/// <param name="SplitOrderIds">One confirmed order per supplying company, sharing the group ref.</param>
/// <param name="WasReplay">Whether this call replayed a completed intent rather than committing anew.</param>
public sealed record CommittedSourcingPlan(
    Guid IntentId,
    SourcingPlan Plan,
    IReadOnlyList<SourcingLegOutcome> Legs,
    IReadOnlyList<Guid> SplitOrderIds,
    bool WasReplay);

/// <summary>
/// Commits a sourcing plan as a saga: one serialisable reservation leg per company, compensated
/// by releases if a later leg fails hard (ADR-102, ADR-116).
/// </summary>
/// <remarks>
/// An application service, NOT a command handler: a handler may resolve at most one company
/// context, while a commit spans several — each leg runs in its own child scope through
/// <c>ISourcingCompanyGateway</c>. Shortfalls re-source once then backorder (arithmetic, expected);
/// a hard leg failure compensates everything taken and the order is not created (operational, abort).
/// </remarks>
public interface ISourcingCommitService
{
    /// <summary>Plans from the group view and commits per company.</summary>
    Task<CommittedSourcingPlan> CommitAsync(SourcingCommitRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One reservation leg's ask, inside one company's database.</summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="CompanyId">The leg's company.</param>
/// <param name="LocationId">Where the stock sits.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Planned">How much the plan asks this leg to hold.</param>
/// <param name="SourceDocumentId">The source order's id.</param>
/// <param name="GroupDocumentRef">The source order number every leg shares.</param>
/// <param name="ExpiresAt">When the hold lapses, from the tenant's expiry policy.</param>
/// <param name="IntentId">The saga intent, or <c>null</c> for a re-sourced remainder (a plain hold under the same group reference, never a second row on the leg's key).</param>
/// <param name="LegId">This leg, or <c>null</c> for a re-sourced remainder.</param>
public sealed record ReservationLegRequest(
    Guid TenantId,
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Planned,
    Guid SourceDocumentId,
    string GroupDocumentRef,
    DateTimeOffset? ExpiresAt,
    Guid? IntentId,
    Guid? LegId);

/// <summary>One split segment to persist inside its company's database.</summary>
/// <param name="CompanyId">The supplying company.</param>
/// <param name="LocationId">The segment's fulfilling location (its largest allocation's).</param>
/// <param name="Lines">The segment's lines with telescoped economics.</param>
public sealed record SplitOrderDraft(Guid CompanyId, Guid LocationId, IReadOnlyList<SplitOrderLineDraft> Lines);

/// <summary>One split segment line: a demand line's slice for one company.</summary>
/// <param name="SourceLineId">The demand line this slices.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">The slice quantity.</param>
/// <param name="UnitPrice">The source line's unit price, carried not priced.</param>
/// <param name="DiscountAmount">The slice's discount share (telescoped, exact).</param>
/// <param name="TaxAmount">The slice's tax share (telescoped, exact).</param>
public sealed record SplitOrderLineDraft(
    Guid SourceLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Money UnitPrice,
    Money DiscountAmount,
    Money TaxAmount);

/// <summary>Does one company's work inside that company's own database — one leg, one transaction.</summary>
/// <remarks>
/// The seam between the saga coordinator (one scope, registry only) and the companies (one scope
/// each). Production runs each call in a child scope bound to the company; tests run the same
/// calls against named test databases. Either way a call touches exactly one company database.
/// </remarks>
public interface ISourcingCompanyGateway
{
    /// <summary>Holds up to the planned quantity in the leg's company. Idempotent on (intent, leg, line).</summary>
    Task<ReserveOutcome> ReserveLegAsync(ReservationLegRequest leg, CancellationToken cancellationToken = default);

    /// <summary>Authoritative availability in one company, for the re-source round's fresh reads.</summary>
    Task<LocalAvailability> ReadLocalAsync(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Releases one live hold by its reservation id (the re-source round's release step).</summary>
    Task ReleaseHoldAsync(
        Guid tenantId,
        Guid companyId,
        Guid reservationId,
        CancellationToken cancellationToken = default);

    /// <summary>Releases every live hold carrying one group reference — the compensation set.</summary>
    /// <returns>How many holds were released.</returns>
    Task<int> ReleaseGroupHoldsAsync(
        Guid tenantId,
        Guid companyId,
        string groupDocumentRef,
        CancellationToken cancellationToken = default);

    /// <summary>Writes one confirmed split segment, numbered by its own company's series.</summary>
    /// <returns>The new order's id.</returns>
    Task<Guid> WriteSplitOrderAsync(
        Guid tenantId,
        Guid companyId,
        string groupDocumentRef,
        SourcingSourceOrder source,
        SplitOrderDraft draft,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns a committed sourcing plan into one document per supplying company, each written entirely
/// inside its own database, sharing a <c>GroupDocumentRef</c>.
/// </summary>
/// <remarks>
/// Pure: builds drafts, asserts the split reconciles line for line and cent for cent, and returns
/// them for the commit service to persist per company. Money slices telescope (cumulative rounding,
/// ADR-075's shape); the assert compares exact decimals, so dust has nowhere to hide.
/// </remarks>
public interface ISplitDocumentBuilder
{
    /// <summary>Builds one draft per supplying company.</summary>
    /// <exception cref="InventoryRuleException">The split does not reconcile to its source.</exception>
    IReadOnlyList<SplitOrderDraft> Build(SourcingSourceOrder source, SourcingPlan committed);
}

/// <summary>How long a hold for one source kind lives before it lapses.</summary>
/// <remarks>
/// Read at hold time from the tenant's policy; the expiry job itself only reads the stamped
/// <c>ExpiresAt</c>, so a policy change never rewrites live holds. Defaults: order and pro-forma
/// approval holds 72 hours; transfer and shipment holds never expire.
/// </remarks>
public interface IReservationExpiryPolicy
{
    /// <summary>How long a new hold lives, or <c>null</c> for a hold that never expires.</summary>
    Task<TimeSpan?> ResolveAsync(ReservationSource source, CancellationToken cancellationToken = default);
}

/// <summary>One saga leg's state, as reported to operations.</summary>
/// <param name="LegId">The leg.</param>
/// <param name="CompanyId">The leg's company.</param>
/// <param name="State">Where the leg stands.</param>
/// <param name="Attempts">How many times the leg was dispatched.</param>
/// <param name="LastError">The last failure, when one happened.</param>
public sealed record SourcingIntentLegResult(
    Guid LegId,
    Guid CompanyId,
    string State,
    int Attempts,
    string? LastError);

/// <summary>A sourcing saga intent and its legs, as reported to operations.</summary>
/// <param name="IntentId">The intent.</param>
/// <param name="Type">The intent type.</param>
/// <param name="State">Where the intent stands.</param>
/// <param name="CreatedAt">When the intent was written.</param>
/// <param name="Legs">One row per leg, in company order.</param>
public sealed record SourcingIntentResult(
    Guid IntentId,
    string Type,
    string State,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SourcingIntentLegResult> Legs);

/// <summary>Reads saga intent state for the sourcing ops surface (in-flight report).</summary>
public interface ISourcingIntentReader
{
    /// <summary>Finds an intent and its legs, or <c>null</c>.</summary>
    Task<SourcingIntentResult?> FindAsync(Guid intentId, CancellationToken cancellationToken = default);
}
