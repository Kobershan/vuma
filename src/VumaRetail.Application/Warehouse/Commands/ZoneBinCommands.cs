using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Application.Warehouse.Commands;

/// <summary>Creates a new zone at a Stage 08 location.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateZoneCommand(Guid LocationId, string Code, string Name, ZoneType Type) : ICommand<Guid>;

/// <summary>Rejects a malformed create-zone command before it reaches the handler.</summary>
public sealed class CreateZoneCommandValidator : AbstractValidator<CreateZoneCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateZoneCommandValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Type).IsInEnum();
    }
}

/// <summary>Creates a zone, refusing a location this tenant does not have or a code already used at it.</summary>
/// <param name="locations">Stage 08 location lookup.</param>
/// <param name="zones">Zone lookup and insertion.</param>
public sealed class CreateZoneCommandHandler(IStockLocationRepository locations, IZoneRepository zones)
    : ICommandHandler<CreateZoneCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateZoneCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation location = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.LocationId);

        Zone? existing = await zones.FindByCodeAsync(command.LocationId, command.Code, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            throw WarehouseConflictException.ZoneCode(command.Code);
        }

        Zone zone = Zone.Create(location.TenantId, location.StoreId, location.Id, command.Code, command.Name, command.Type);
        zones.Add(zone);

        return zone.Id;
    }
}

/// <summary>Retires a zone from holding active bins.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivateZoneCommand(Guid ZoneId) : ICommand;

/// <summary>Deactivates a zone.</summary>
/// <param name="zones">Zone lookup.</param>
public sealed class DeactivateZoneCommandHandler(IZoneRepository zones) : ICommandHandler<DeactivateZoneCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(DeactivateZoneCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Zone zone = await zones.FindAsync(command.ZoneId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("zone", command.ZoneId);

        zone.Deactivate();

        return Unit.Value;
    }
}

/// <summary>Creates a new bin within a zone.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateBinCommand(
    Guid ZoneId,
    string Code,
    string Name,
    BinType Type,
    decimal? CapacityQuantity = null,
    string? CapacityUnitOfMeasure = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create-bin command before it reaches the handler.</summary>
public sealed class CreateBinCommandValidator : AbstractValidator<CreateBinCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateBinCommandValidator()
    {
        RuleFor(command => command.ZoneId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Type).IsInEnum();
        RuleFor(command => command)
            .Must(command => command.CapacityQuantity is null || command.CapacityQuantity > 0m)
            .WithMessage("Capacity, when supplied, must be positive.");
        RuleFor(command => command)
            .Must(command => (command.CapacityQuantity is null) == string.IsNullOrWhiteSpace(command.CapacityUnitOfMeasure))
            .WithMessage("A capacity quantity and its unit of measure must be supplied together.");
    }
}

/// <summary>Creates a bin, refusing a zone this tenant does not have or a code already used at its location.</summary>
/// <param name="zones">Zone lookup.</param>
/// <param name="bins">Bin lookup and insertion.</param>
public sealed class CreateBinCommandHandler(IZoneRepository zones, IBinRepository bins) : ICommandHandler<CreateBinCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateBinCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Zone zone = await zones.FindAsync(command.ZoneId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("zone", command.ZoneId);

        Bin? existing = await bins.FindByCodeAsync(zone.LocationId, command.Code, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            throw WarehouseConflictException.BinCode(command.Code);
        }

        Quantity? capacity = command.CapacityQuantity is { } value
            ? new Quantity(value, command.CapacityUnitOfMeasure!)
            : null;

        Bin bin = Bin.Create(zone.TenantId, zone.StoreId, zone.LocationId, zone.Id, command.Code, command.Name, command.Type, capacity);
        bins.Add(bin);

        return bin.Id;
    }
}

/// <summary>Retires a bin from receiving or issuing stock.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivateBinCommand(Guid BinId) : ICommand;

/// <summary>Deactivates a bin.</summary>
/// <param name="bins">Bin lookup.</param>
public sealed class DeactivateBinCommandHandler(IBinRepository bins) : ICommandHandler<DeactivateBinCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(DeactivateBinCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Bin bin = await bins.FindAsync(command.BinId, cancellationToken).ConfigureAwait(false)
            ?? throw new WarehouseNotFoundException("bin", command.BinId);

        bin.Deactivate();

        return Unit.Value;
    }
}
