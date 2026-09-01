using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Sales.Commands;

/// <summary>Creates a price list.</summary>
/// <param name="Code">The list code, unique per tenant. Upper-cased on the way in.</param>
/// <param name="Name">The list's name, as a manager would recognise it.</param>
/// <param name="Currency">The currency every line is denominated in.</param>
/// <param name="Kind">Retail, wholesale or staff.</param>
/// <param name="PricesIncludeTax">True when the prices are authored tax-inclusive, as a shelf price is.</param>
/// <param name="Priority">Which list wins when several price the same item. Higher wins.</param>
/// <param name="EffectiveFrom">The first day it may be used.</param>
/// <param name="EffectiveTo">The last day, or <c>null</c> for open-ended.</param>
/// <param name="StoreId">The store this list is scoped to, or <c>null</c> for tenant-wide.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreatePriceListCommand(
    string Code,
    string Name,
    string Currency,
    PriceListKind Kind,
    bool PricesIncludeTax,
    int Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create-price-list command before it reaches the handler.</summary>
public sealed class CreatePriceListCommandValidator : AbstractValidator<CreatePriceListCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreatePriceListCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Kind).IsInEnum();
    }
}

/// <summary>Creates the list, refusing a code the tenant already uses.</summary>
/// <param name="priceLists">Price list lookup and insertion.</param>
/// <param name="tenant">The ambient tenant and store.</param>
public sealed class CreatePriceListCommandHandler(IPriceListRepository priceLists, ITenantContext tenant)
    : ICommandHandler<CreatePriceListCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        CreatePriceListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await priceLists.CodeExistsAsync(command.Code, cancellationToken).ConfigureAwait(false))
        {
            throw SalesConflictException.PriceListCode(command.Code);
        }

        PriceList created = PriceList.Create(
            tenant.TenantId,
            command.StoreId,
            command.Code,
            command.Name,
            command.Currency,
            command.Kind,
            command.PricesIncludeTax,
            command.Priority,
            command.EffectiveFrom,
            command.EffectiveTo);

        priceLists.Add(created);

        return created.Id;
    }
}

/// <summary>Renames a price list, re-ranks it and moves its effective window.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Name">The new name.</param>
/// <param name="Priority">The new priority.</param>
/// <param name="EffectiveFrom">The new first day.</param>
/// <param name="EffectiveTo">The new last day, or <c>null</c>.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AmendPriceListCommand(
    Guid PriceListId,
    string Name,
    int Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null) : ICommand;

/// <summary>Rejects a malformed amend command before it reaches the handler.</summary>
public sealed class AmendPriceListCommandValidator : AbstractValidator<AmendPriceListCommand>
{
    /// <summary>Builds the rules.</summary>
    public AmendPriceListCommandValidator()
    {
        RuleFor(command => command.PriceListId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
    }
}

/// <summary>Amends the list.</summary>
/// <param name="priceLists">Price list lookup.</param>
public sealed class AmendPriceListCommandHandler(IPriceListRepository priceLists)
    : ICommandHandler<AmendPriceListCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        AmendPriceListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PriceList list = await priceLists.FindAsync(command.PriceListId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("price list", command.PriceListId);

        list.Amend(command.Name, command.Priority, command.EffectiveFrom, command.EffectiveTo);

        return Unit.Value;
    }
}

/// <summary>Puts a price on a list, or reprices the break that is already there.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/>.</param>
/// <param name="ItemVariantId">The variant. Exactly one of this and <paramref name="ItemId"/>.</param>
/// <param name="UnitPrice">What one unit sells for, in the list's currency.</param>
/// <param name="MinimumQuantity">The quantity this price starts applying at — <c>1</c> for the ordinary price.</param>
/// <remarks>
/// Upserts rather than refusing a duplicate, which is what makes Stage 11's bulk price import
/// idempotent: re-running the same sheet reprices the rows it already created instead of failing on
/// the second row or accumulating a second price for the same break. The aggregate still refuses a
/// duplicate — this handler reprices before it gets that far.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record SetPriceListLineCommand(
    Guid PriceListId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Money UnitPrice,
    decimal MinimumQuantity = 1m) : ICommand<Guid>;

/// <summary>Rejects a malformed set-price command before it reaches the handler.</summary>
public sealed class SetPriceListLineCommandValidator : AbstractValidator<SetPriceListLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public SetPriceListLineCommandValidator()
    {
        RuleFor(command => command.PriceListId).NotEmpty();
        RuleFor(command => command.UnitPrice.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.UnitPrice.Currency).NotEmpty().Length(3);
        RuleFor(command => command.MinimumQuantity).GreaterThan(0m);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(SetPriceListLineCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>Adds the price, or reprices the existing break.</summary>
/// <param name="priceLists">Price list lookup.</param>
public sealed class SetPriceListLineCommandHandler(IPriceListRepository priceLists)
    : ICommandHandler<SetPriceListLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        SetPriceListLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PriceList list = await priceLists.FindAsync(command.PriceListId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("price list", command.PriceListId);

        PriceListLine? existing = list.Lines.FirstOrDefault(line =>
            line.ItemId == command.ItemId
            && line.ItemVariantId == command.ItemVariantId
            && line.MinimumQuantity == command.MinimumQuantity);

        if (existing is not null)
        {
            existing.Reprice(command.UnitPrice);
            return existing.Id;
        }

        PriceListLine created = list.AddLine(
            command.ItemId, command.ItemVariantId, command.UnitPrice, command.MinimumQuantity);

        return created.Id;
    }
}

/// <summary>Retires a price list from resolution. Nothing is deleted (§7 rule 8).</summary>
/// <param name="PriceListId">The list.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivatePriceListCommand(Guid PriceListId) : ICommand;

/// <summary>Rejects a malformed deactivate command before it reaches the handler.</summary>
public sealed class DeactivatePriceListCommandValidator : AbstractValidator<DeactivatePriceListCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeactivatePriceListCommandValidator() => RuleFor(command => command.PriceListId).NotEmpty();
}

/// <summary>Deactivates the list.</summary>
/// <param name="priceLists">Price list lookup.</param>
public sealed class DeactivatePriceListCommandHandler(IPriceListRepository priceLists)
    : ICommandHandler<DeactivatePriceListCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        DeactivatePriceListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PriceList list = await priceLists.FindAsync(command.PriceListId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("price list", command.PriceListId);

        list.Deactivate();

        return Unit.Value;
    }
}

/// <summary>Brings a retired price list back into use.</summary>
/// <param name="PriceListId">The list.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ActivatePriceListCommand(Guid PriceListId) : ICommand;

/// <summary>Rejects a malformed activate command before it reaches the handler.</summary>
public sealed class ActivatePriceListCommandValidator : AbstractValidator<ActivatePriceListCommand>
{
    /// <summary>Builds the rules.</summary>
    public ActivatePriceListCommandValidator() => RuleFor(command => command.PriceListId).NotEmpty();
}

/// <summary>Activates the list.</summary>
/// <param name="priceLists">Price list lookup.</param>
public sealed class ActivatePriceListCommandHandler(IPriceListRepository priceLists)
    : ICommandHandler<ActivatePriceListCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        ActivatePriceListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PriceList list = await priceLists.FindAsync(command.PriceListId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("price list", command.PriceListId);

        list.Activate();

        return Unit.Value;
    }
}
