using VumaRetail.Application.Abstractions;

namespace VumaRetail.Application.Inventory.Queries;

/// <summary>Authoritative available-to-promise for one stock-keeping unit at one location.</summary>
/// <param name="LocationId">The location.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
public sealed record GetLocalAvailabilityQuery(Guid LocationId, Guid? ItemId, Guid? ItemVariantId)
    : IQuery<LocalAvailability>;

/// <summary>Reads local availability.</summary>
/// <param name="availability">The availability service.</param>
public sealed class GetLocalAvailabilityQueryHandler(IAvailabilityService availability)
    : IQueryHandler<GetLocalAvailabilityQuery, LocalAvailability>
{
    /// <inheritdoc />
    public Task<LocalAvailability> HandleAsync(GetLocalAvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return availability.GetLocalAsync(query.LocationId, query.ItemId, query.ItemVariantId, cancellationToken);
    }
}

/// <summary>Planning-only group availability for one stock-keeping unit, from the registry projection.</summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
public sealed record GetGroupAvailabilityQuery(Guid? ItemId, Guid? ItemVariantId)
    : IQuery<GroupAvailabilityView>;

/// <summary>Reads the group availability view.</summary>
/// <param name="availability">The availability service.</param>
public sealed class GetGroupAvailabilityQueryHandler(IAvailabilityService availability)
    : IQueryHandler<GetGroupAvailabilityQuery, GroupAvailabilityView>
{
    /// <inheritdoc />
    public Task<GroupAvailabilityView> HandleAsync(GetGroupAvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return availability.GetGroupAsync(query.ItemId, query.ItemVariantId, cancellationToken);
    }
}
