using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Queries;

/// <summary>Previews a consolidated wave without committing it to the warehouse.</summary>
/// <param name="LocationId">The location to pick from.</param>
/// <param name="PeriodFrom">The period start.</param>
/// <param name="PeriodTo">The period end.</param>
/// <param name="GeographyLevel">The geography level (Province | City | Suburb).</param>
/// <param name="GeographyValue">The geography value.</param>
/// <param name="CompanyScopeId">Optional company scope.</param>
public sealed record PreviewConsolidatedWaveQuery(
    Guid LocationId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string GeographyLevel,
    string GeographyValue,
    Guid? CompanyScopeId = null) : IQuery<PreviewConsolidatedWaveResponse>;

/// <summary>Rejects a malformed preview query before it reaches the handler.</summary>
public sealed class PreviewConsolidatedWaveQueryValidator : AbstractValidator<PreviewConsolidatedWaveQuery>
{
    /// <summary>Builds the rules.</summary>
    public PreviewConsolidatedWaveQueryValidator()
    {
        RuleFor(query => query.LocationId).NotEmpty();
        RuleFor(query => query.PeriodFrom).LessThanOrEqualTo(query => query.PeriodTo);
        RuleFor(query => query.GeographyLevel).NotEmpty();
        RuleFor(query => query.GeographyValue).NotEmpty();
    }
}

/// <summary>
/// A preview of a consolidated wave: the grouped lines and per-order breakdowns,
/// without persisting anything.
/// </summary>
/// <param name="LocationId">The location.</param>
/// <param name="GeographyLevel">The geography level.</param>
/// <param name="GeographyValue">The geography value.</param>
/// <param name="PeriodFrom">The period start.</param>
/// <param name="PeriodTo">The period end.</param>
/// <param name="GroupedLines">The grouped lines that a build would produce.</param>
/// <param name="OrderBreakdowns">The per-order breakdowns.</param>
public sealed record PreviewConsolidatedWaveResponse(
    Guid LocationId,
    string GeographyLevel,
    string GeographyValue,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<GroupedLinePreview> GroupedLines,
    IReadOnlyList<OrderBreakdownPreview> OrderBreakdowns);

/// <summary>One grouped SKU line in a wave preview.</summary>
public sealed record GroupedLinePreview(
    Guid? ItemId,
    Guid? ItemVariantId,
    string UnitOfMeasure,
    string PackSize,
    decimal TotalQuantity,
    int OrderCount);

/// <summary>One order's contribution to a grouped line.</summary>
public sealed record OrderBreakdownPreview(
    Guid OrderId,
    Guid OrderLineId,
    Guid? ItemId,
    decimal Quantity);

/// <summary>
/// Previews a consolidated wave by querying open order lines and grouping them
/// without persisting anything.
/// </summary>
/// <param name="orderLines">Qualifying open order lines.</param>
public sealed class PreviewConsolidatedWaveQueryHandler(IOrderLineReader orderLines)
    : IQueryHandler<PreviewConsolidatedWaveQuery, PreviewConsolidatedWaveResponse>
{
    /// <inheritdoc />
    public async Task<PreviewConsolidatedWaveResponse> HandleAsync(
        PreviewConsolidatedWaveQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var lines = await orderLines
            .ReadOpenLinesAsync(
                query.LocationId, query.PeriodFrom, query.PeriodTo,
                query.GeographyLevel, query.GeographyValue,
                query.CompanyScopeId, cancellationToken)
            .ConfigureAwait(false);

        var grouped = lines
            .GroupBy(line => (line.ItemId, line.ItemVariantId, line.UnitOfMeasure, line.PackSize))
            .Select(g => new GroupedLinePreview(
                g.Key.ItemId, g.Key.ItemVariantId, g.Key.UnitOfMeasure, g.Key.PackSize,
                g.Sum(l => l.Quantity), g.Select(l => l.OrderId).Distinct().Count()))
            .ToList();

        var breakdowns = lines
            .Select(l => new OrderBreakdownPreview(l.OrderId, l.OrderLineId, l.ItemId, l.Quantity))
            .ToList();

        return new PreviewConsolidatedWaveResponse(
            query.LocationId, query.GeographyLevel, query.GeographyValue,
            query.PeriodFrom, query.PeriodTo, grouped, breakdowns);
    }
}

/// <summary>Gets the per-order breakdown for a consolidated wave line.</summary>
/// <param name="PickWaveLineId">The grouped wave line to break down.</param>
public sealed record GetBreakdownQuery(Guid PickWaveLineId) : IQuery<IReadOnlyList<OrderBreakdownResponse>>;

/// <summary>One order's contribution to a grouped wave line, as read back.</summary>
public sealed record OrderBreakdownResponse(
    Guid OrderId,
    Guid OrderLineId,
    decimal Quantity);

/// <summary>
/// Returns the per-order breakdown for a consolidated wave line.
/// </summary>
/// <param name="breakdowns">The breakdown repository.</param>
public sealed class GetBreakdownQueryHandler(IPickWaveLineBreakdownRepository breakdowns)
    : IQueryHandler<GetBreakdownQuery, IReadOnlyList<OrderBreakdownResponse>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderBreakdownResponse>> HandleAsync(
        GetBreakdownQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<PickWaveLineBreakdown> all = await breakdowns
            .FindByWaveLineIdAsync(query.PickWaveLineId, cancellationToken)
            .ConfigureAwait(false);

        return [.. all.Select(b => new OrderBreakdownResponse(b.OrderId, b.OrderLineId, b.Quantity))];
    }
}
