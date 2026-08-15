using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Platform.Commands;

/// <summary>Creates a new trading location within the tenant (R8).</summary>
/// <remarks>
/// The command Stage 01's seeder was written without, because there was nothing yet for it to gate on
/// (<c>PipelineRulesTests.No_handler_calls_CommitAsync</c>). Stage 06 closes that gap: a tenant adding
/// a second branch now goes through the same permission check, the same hard-limit check and the same
/// audit trail as every other write, instead of a script with direct database access.
/// </remarks>
/// <param name="Code">The short store code, for example <c>JHB01</c>.</param>
/// <param name="Name">The store name as printed on receipts.</param>
/// <param name="Address">The trading address, or <c>null</c> to record it later.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateStoreCommand(string Code, string Name, Address? Address = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create-store command before it reaches the handler.</summary>
public sealed class CreateStoreCommandValidator : AbstractValidator<CreateStoreCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateStoreCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(16);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Creates a store, refusing a code already used in this tenant and a plan without room for one.</summary>
/// <param name="stores">Store lookup and insertion.</param>
/// <param name="tenant">The tenant the store belongs to.</param>
/// <param name="entitlements">The plan's store ceiling (<c>LICENSING.md</c> §6).</param>
/// <param name="counters">How many stores there already are.</param>
public sealed class CreateStoreCommandHandler(
    IStoreRepository stores,
    ITenantContext tenant,
    IEntitlementService entitlements,
    IUsageCounterSource counters) : ICommandHandler<CreateStoreCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateStoreCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await stores.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw PlatformConflictException.StoreCode(command.Code);
        }

        // A hard limit, checked at configuration time (LICENSING.md §6) — never on a trading path.
        ConfigurationCounts counts = await counters
            .CountConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        LimitCheck limit = await entitlements
            .CheckLimitAsync(LimitKind.Stores, counts.Stores + 1, cancellationToken)
            .ConfigureAwait(false);

        limit.ThrowIfExceeded();

        Store store = Store.Create(tenant.TenantId, command.Code, command.Name);
        store.SetDetails(command.Name, command.Address);

        stores.Add(store);

        return store.Id;
    }
}

/// <summary>Updates a store's name and trading address.</summary>
/// <param name="StoreId">The store.</param>
/// <param name="Name">The new name.</param>
/// <param name="Address">The new address, or <c>null</c> to clear it.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record UpdateStoreDetailsCommand(Guid StoreId, string Name, Address? Address = null) : ICommand;

/// <summary>Rejects a malformed update before it reaches the handler.</summary>
public sealed class UpdateStoreDetailsCommandValidator : AbstractValidator<UpdateStoreDetailsCommand>
{
    /// <summary>Builds the rules.</summary>
    public UpdateStoreDetailsCommandValidator()
    {
        RuleFor(command => command.StoreId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Updates a store's details.</summary>
/// <param name="stores">Store lookup.</param>
public sealed class UpdateStoreDetailsCommandHandler(IStoreRepository stores)
    : ICommandHandler<UpdateStoreDetailsCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(UpdateStoreDetailsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Store store = await stores.FindAsync(command.StoreId, cancellationToken).ConfigureAwait(false)
            ?? throw new PlatformNotFoundException("store", command.StoreId);

        store.SetDetails(command.Name, command.Address);

        return Unit.Value;
    }
}
