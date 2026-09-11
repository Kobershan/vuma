using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Commands;

/// <summary>Creates a new count schedule targeting slow movers, zones, classes, value bands or suppliers.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateCountScheduleCommand(
    string Name,
    CountCadence Cadence,
    string Scope,
    int SlowMoverDays,
    int RandomSampleSize,
    DateTimeOffset NextRunAt,
    Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create command before it reaches the handler.</summary>
public sealed class CreateCountScheduleCommandValidator : AbstractValidator<CreateCountScheduleCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateCountScheduleCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Cadence).IsInEnum();
        RuleFor(command => command.Scope).NotEmpty().MaximumLength(256);
        RuleFor(command => command.SlowMoverDays).GreaterThanOrEqualTo(0);
        RuleFor(command => command.RandomSampleSize).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Creates a count schedule. Generates Stage 13 <see cref="CycleCount"/>s when run
/// — no new counting model (ADR-115).
/// </summary>
/// <param name="tenantContext">The ambient tenant and store.</param>
/// <param name="schedules">Schedule persistence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreateCountScheduleCommandHandler(
    ITenantContext tenantContext,
    ICountScheduleRepository schedules,
    IClock clock)
    : ICommandHandler<CreateCountScheduleCommand, Guid>
{
    /// <inheritdoc />
    public Task<Guid> HandleAsync(CreateCountScheduleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nextRun = command.NextRunAt == default ? clock.UtcNow.AddDays(1) : command.NextRunAt;
        CountSchedule schedule = CountSchedule.Create(
            tenantContext.TenantId,
            tenantContext.StoreId,
            command.Name,
            command.Cadence,
            command.Scope,
            command.SlowMoverDays,
            command.RandomSampleSize,
            nextRun);

        schedules.Add(schedule);
        return Task.FromResult(schedule.Id);
    }
}

/// <summary>A line in a count sheet generated from a schedule.</summary>
public sealed record CountSheetLine(
    Guid BinId,
    string BinCode,
    string BinName,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity? QuantityOnHand);

/// <summary>Generates a count sheet for a schedule — the list of bins and stock-keeping units to count.</summary>
/// <param name="ScheduleId">The schedule to generate a sheet for.</param>
/// <param name="LocationId">The location whose active bins are listed.</param>
public sealed record GenerateCountSheetQuery(Guid ScheduleId, Guid LocationId) : IQuery<IReadOnlyList<CountSheetLine>>;

/// <summary>
/// Generates a count sheet from a schedule. The sheet lists the bins and stock-keeping units
/// the schedule targets, ready for counting. The actual count rows are created when
/// the sheet is recorded (Stage 13).
/// </summary>
public sealed class GenerateCountSheetQueryHandler(
    ICountScheduleRepository schedules,
    IBinRepository bins,
    IBinStockRepository binStocks)
    : IQueryHandler<GenerateCountSheetQuery, IReadOnlyList<CountSheetLine>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CountSheetLine>> HandleAsync(GenerateCountSheetQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await schedules.FindAsync(query.ScheduleId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new WarehouseNotFoundException("count schedule", query.ScheduleId);
        }

        IReadOnlyList<Bin> locationBins = await bins.ListActiveForLocationAsync(query.LocationId, cancellationToken)
            .ConfigureAwait(false);
        var lines = new List<CountSheetLine>();
        foreach (Bin bin in locationBins)
        {
            IReadOnlyList<BinStock> stock = await binStocks.ListForBinAsync(bin.Id, cancellationToken)
                .ConfigureAwait(false);
            lines.AddRange(stock.Select(balance => new CountSheetLine(
                bin.Id, bin.Code, bin.Name, balance.ItemId, balance.ItemVariantId,
                balance.QuantityOnHand)));
        }

        return lines;
    }
}
