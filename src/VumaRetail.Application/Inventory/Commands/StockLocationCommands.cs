using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Inventory;

namespace VumaRetail.Application.Inventory.Commands;

/// <summary>Creates a new stock location.</summary>
/// <param name="Code">The location's unique code within the tenant, upper-cased at creation.</param>
/// <param name="Name">The location's name.</param>
/// <param name="Type">What kind of location this is.</param>
/// <param name="StoreId">The owning store, or <c>null</c> for a tenant-wide location such as a central warehouse.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateStockLocationCommand(
    string Code,
    string Name,
    StockLocationType Type,
    Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create-location command before it reaches the handler.</summary>
public sealed class CreateStockLocationCommandValidator : AbstractValidator<CreateStockLocationCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateStockLocationCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Creates a location, refusing a code already used in this tenant.</summary>
/// <param name="locations">Location lookup and insertion.</param>
/// <param name="tenant">The tenant the location belongs to.</param>
public sealed class CreateStockLocationCommandHandler(
    IStockLocationRepository locations,
    ITenantContext tenant) : ICommandHandler<CreateStockLocationCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateStockLocationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await locations.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw InventoryConflictException.LocationCode(command.Code);
        }

        StockLocation location = StockLocation.Create(tenant.TenantId, command.StoreId, command.Code, command.Name, command.Type);

        locations.Add(location);

        return location.Id;
    }
}

/// <summary>Retires a location from receiving or issuing stock. History is retained; nothing is deleted (§7 rule 8).</summary>
/// <param name="LocationId">The location.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivateStockLocationCommand(Guid LocationId) : ICommand;

/// <summary>Rejects a malformed deactivate-location command before it reaches the handler.</summary>
public sealed class DeactivateStockLocationCommandValidator : AbstractValidator<DeactivateStockLocationCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeactivateStockLocationCommandValidator() => RuleFor(command => command.LocationId).NotEmpty();
}

/// <summary>Deactivates a location.</summary>
/// <param name="locations">Location lookup.</param>
public sealed class DeactivateStockLocationCommandHandler(IStockLocationRepository locations)
    : ICommandHandler<DeactivateStockLocationCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(DeactivateStockLocationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        StockLocation location = await locations.FindAsync(command.LocationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryNotFoundException("stock location", command.LocationId);

        location.Deactivate();

        return Unit.Value;
    }
}
