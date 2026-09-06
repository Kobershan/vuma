using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Inventory;

/// <summary>How much of one stock-keeping unit is held back from sale, as read inside the owning company.</summary>
/// <param name="LocationId">The location.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Promise">The available-to-promise figure. Authoritative: read inside the company that owns the stock.</param>
public sealed record LocalAvailability(Guid LocationId, Guid? ItemId, Guid? ItemVariantId, AvailableToPromise Promise);

/// <summary>One company's contribution to a group availability view. Planning only — never the basis for a commit.</summary>
/// <param name="CompanyId">The contributing company.</param>
/// <param name="CompanyCode">The contributing company's code, for display.</param>
/// <param name="Promise">The figure as last published. Read <see cref="AsAt"/> before trusting it.</param>
/// <param name="AsAt">When this contributor last published. Shown on every surface.</param>
/// <param name="IsStale">Whether the contributor has not published within the freshness threshold.</param>
public sealed record GroupAvailabilityContribution(
    Guid CompanyId,
    string CompanyCode,
    AvailableToPromise Promise,
    DateTimeOffset AsAt,
    bool IsStale);

/// <summary>
/// Group-wide availability for one stock-keeping unit: one read of the registry projection, not
/// N company queries (ADR-119). A stale contributor is named, never silently summed.
/// </summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Contributions">One row per publishing company.</param>
/// <param name="AsAt">When this view was assembled.</param>
public sealed record GroupAvailabilityView(
    Guid? ItemId,
    Guid? ItemVariantId,
    IReadOnlyList<GroupAvailabilityContribution> Contributions,
    DateTimeOffset AsAt)
{
    /// <summary>Available across fresh contributors only — stale stock is shown, not promised.</summary>
    public decimal TotalFreshAvailable => Contributions.Where(c => !c.IsStale).Sum(c => c.Promise.Available.Value);

    /// <summary>Whether any contributor is stale.</summary>
    public bool HasStaleContributors => Contributions.Any(c => c.IsStale);

    /// <summary>Company codes that have not published recently.</summary>
    public IReadOnlyList<string> StaleContributorCodes =>
        Contributions.Where(c => c.IsStale).Select(c => c.CompanyCode).ToList();
}

/// <summary>Reads availability — company-local (authoritative) and group (planning only).</summary>
/// <remarks>
/// The two reads return different types so a caller cannot substitute a stale group figure where
/// an authoritative local one belongs. A group read model never decides a commit
/// (<c>docs/MULTI_COMPANY.md</c> §11 rule 4); only <c>IReservationService</c>, inside the owning
/// company's database, commits stock.
/// </remarks>
public interface IAvailabilityService
{
    /// <summary>Authoritative available-to-promise, read inside the acting company.</summary>
    Task<LocalAvailability> GetLocalAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>Planning-only group view from the registry projection, always stamped <c>AsAt</c>.</summary>
    Task<GroupAvailabilityView> GetGroupAsync(
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default);
}

/// <summary>What holding stock actually held.</summary>
/// <param name="ReservationId">The logical reservation, or <c>null</c> when nothing could be held.</param>
/// <param name="Held">How much was held.</param>
/// <param name="Shortfall">How much of the demand could not be covered. Zero means fully held.</param>
/// <param name="AvailableAfter">What remains available after this hold.</param>
/// <param name="AsAt">When the figures were read.</param>
public sealed record ReserveOutcome(
    Guid? ReservationId,
    Quantity Held,
    Quantity Shortfall,
    Quantity AvailableAfter,
    DateTimeOffset AsAt);

/// <summary>Takes and releases holds on stock, always inside one company's own database.</summary>
/// <remarks>
/// <para>
/// Every method runs one serialisable transaction in the acting company's database with the
/// availability re-check inside it — the check-then-act race is the defect this stage exists to
/// prevent, and a stale group projection must never be able to cause it (ADR-102).
/// </para>
/// <para>
/// Implementations are not thread-safe beyond what a single database transaction allows; a saga
/// leg per company gets its own scope and therefore its own instance.
/// </para>
/// </remarks>
public interface IReservationService
{
    /// <summary>
    /// Holds up to <paramref name="demanded"/> of one stock-keeping unit at one location.
    /// Holds what exists and reports the rest as shortfall — it never throws on shortfall and
    /// never drives available negative.
    /// </summary>
    Task<ReserveOutcome> ReserveAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity demanded,
        ReservationSource source,
        Guid sourceDocumentId,
        string? groupDocumentRef = null,
        DateTimeOffset? expiresAt = null,
        Guid? intentId = null,
        Guid? legId = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>Consumes a live hold — the held quantity shipped or issued.</summary>
    Task ConsumeAsync(Guid reservationId, Guid consumedByReferenceId, CancellationToken cancellationToken = default);

    /// <summary>Releases a live hold — available is restored by a new ledger row, never an edit.</summary>
    Task ReleaseAsync(Guid reservationId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>Expires every live hold whose time has passed, oldest first, capped at 100 per call.</summary>
    /// <returns>How many holds expired.</returns>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads how much of a stock-keeping unit sits in staging bins at a location.</summary>
/// <remarks>
/// ADR-114: staging areas are bins; stock in them is on hand and not available. The warehouse
/// module owns the bins; this port is inventory's read seam onto them so availability does not
/// depend on warehouse query shapes.
/// </remarks>
public interface IStagingQuantityReader
{
    /// <summary>Quantity in staging-type bins at one location for one stock-keeping unit.</summary>
    Task<Quantity> ReadStagingAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        string unitOfMeasure,
        CancellationToken cancellationToken = default);
}

/// <summary>One company's current availability figure for one stock-keeping unit at one location.</summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="CompanyId">The publishing company.</param>
/// <param name="LocationId">The location.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="OnHand">What the ledger says is physically present.</param>
/// <param name="Reserved">What live holds speak for.</param>
/// <param name="InStaging">What sits in staging bins.</param>
/// <param name="UnitOfMeasure">The unit all three figures share.</param>
/// <param name="AsAt">When the figures were read.</param>
public sealed record AvailabilitySnapshot(
    Guid TenantId,
    Guid CompanyId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal OnHand,
    decimal Reserved,
    decimal InStaging,
    string UnitOfMeasure,
    DateTimeOffset AsAt);

/// <summary>Publishes availability snapshots to the registry projection (ADR-119).</summary>
/// <remarks>
/// Upsert by natural key, idempotent by construction: publishing the same figure twice changes
/// nothing, so a retry after a crash is always safe.
/// </remarks>
public interface IGroupAvailabilityPublisher
{
    /// <summary>Publishes one snapshot to the registry projection.</summary>
    Task PublishAsync(AvailabilitySnapshot snapshot, CancellationToken cancellationToken = default);
}

/// <summary>Reads the registry availability projection — one read, never N company queries (ADR-119).</summary>
public interface IRegistryAvailabilityReader
{
    /// <summary>Every company's last-published figure for one stock-keeping unit.</summary>
    Task<GroupAvailabilityView> ReadAsync(
        Guid? itemId,
        Guid? itemVariantId,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default);
}
