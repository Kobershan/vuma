using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Catalog;

namespace VumaRetail.Application.Catalog.Commands;

/// <summary>Creates a new item.</summary>
/// <param name="Code">The item's unique code within the tenant, upper-cased at creation.</param>
/// <param name="Name">The item's name.</param>
/// <param name="ItemType">Stocked, service, or non-stock.</param>
/// <param name="UnitOfMeasureId">The base unit of measure this item is counted in.</param>
/// <param name="Description">An optional longer description.</param>
/// <param name="TaxClassCode">An optional tax class code.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateItemCommand(
    string Code,
    string Name,
    ItemType ItemType,
    Guid UnitOfMeasureId,
    string? Description = null,
    string? TaxClassCode = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create-item command before it reaches the handler.</summary>
public sealed class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateItemCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.UnitOfMeasureId).NotEmpty();
        RuleFor(command => command.TaxClassCode).MaximumLength(32);
    }
}

/// <summary>Creates an item, refusing a code already used and a unit of measure this tenant cannot use.</summary>
/// <param name="items">Item lookup and insertion.</param>
/// <param name="units">Unit-of-measure lookup, to confirm the base unit belongs to this tenant and is active.</param>
/// <param name="tenant">The tenant the item belongs to.</param>
public sealed class CreateItemCommandHandler(
    IItemRepository items,
    IUnitOfMeasureRepository units,
    ITenantContext tenant) : ICommandHandler<CreateItemCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await items.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw CatalogConflictException.ItemCode(command.Code);
        }

        UnitOfMeasure? unit = await units.FindAsync(command.UnitOfMeasureId, cancellationToken).ConfigureAwait(false);

        if (unit is not { IsActive: true })
        {
            throw CatalogRuleException.UnitOfMeasureNotAvailable(command.UnitOfMeasureId);
        }

        Item item = Item.Create(
            tenant.TenantId,
            command.Code,
            command.Name,
            command.ItemType,
            command.UnitOfMeasureId,
            command.Description,
            command.TaxClassCode);

        items.Add(item);

        return item.Id;
    }
}

/// <summary>Updates an item's descriptive fields.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Name">The new name.</param>
/// <param name="Description">The new description, or <c>null</c> to clear it.</param>
/// <param name="TaxClassCode">The new tax class code, or <c>null</c> to clear it.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record UpdateItemDetailsCommand(
    Guid ItemId,
    string Name,
    string? Description = null,
    string? TaxClassCode = null) : ICommand;

/// <summary>Rejects a malformed update-item command before it reaches the handler.</summary>
public sealed class UpdateItemDetailsCommandValidator : AbstractValidator<UpdateItemDetailsCommand>
{
    /// <summary>Builds the rules.</summary>
    public UpdateItemDetailsCommandValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.TaxClassCode).MaximumLength(32);
    }
}

/// <summary>Updates an item's details.</summary>
/// <param name="items">Item lookup.</param>
public sealed class UpdateItemDetailsCommandHandler(IItemRepository items)
    : ICommandHandler<UpdateItemDetailsCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(UpdateItemDetailsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Item item = await items.FindAsync(command.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("item", command.ItemId);

        item.SetDetails(command.Name, command.Description, command.TaxClassCode);

        return Unit.Value;
    }
}

/// <summary>Retires an item from sale and receipt. History is retained; nothing is deleted (§7 rule 8).</summary>
/// <param name="ItemId">The item.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivateItemCommand(Guid ItemId) : ICommand;

/// <summary>Rejects a malformed deactivate-item command before it reaches the handler.</summary>
public sealed class DeactivateItemCommandValidator : AbstractValidator<DeactivateItemCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeactivateItemCommandValidator() => RuleFor(command => command.ItemId).NotEmpty();
}

/// <summary>Deactivates an item.</summary>
/// <param name="items">Item lookup.</param>
public sealed class DeactivateItemCommandHandler(IItemRepository items)
    : ICommandHandler<DeactivateItemCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(DeactivateItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Item item = await items.FindAsync(command.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("item", command.ItemId);

        item.Deactivate();

        return Unit.Value;
    }
}

/// <summary>Adds a variant to an item.</summary>
/// <param name="ItemId">The parent item.</param>
/// <param name="Sku">The variant's unique SKU within the tenant, upper-cased at creation.</param>
/// <param name="Attributes">The variant's attribute pairs, for example <c>Size: M</c>.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateItemVariantCommand(
    Guid ItemId,
    string Sku,
    IReadOnlyList<VariantAttribute> Attributes) : ICommand<Guid>;

/// <summary>Rejects a malformed create-variant command before it reaches the handler.</summary>
public sealed class CreateItemVariantCommandValidator : AbstractValidator<CreateItemVariantCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateItemVariantCommandValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Attributes).NotNull();
    }
}

/// <summary>
/// Adds a variant, refusing a SKU already used and an item that already carries a barcode of its own.
/// </summary>
/// <param name="items">Item lookup.</param>
/// <param name="variants">Variant lookup and insertion.</param>
/// <param name="barcodes">Barcode lookup, to enforce that an item does not sell both as itself and as a variant.</param>
public sealed class CreateItemVariantCommandHandler(
    IItemRepository items,
    IItemVariantRepository variants,
    IBarcodeRepository barcodes) : ICommandHandler<CreateItemVariantCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateItemVariantCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Item item = await items.FindAsync(command.ItemId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("item", command.ItemId);

        if (await variants.FindBySkuAsync(command.Sku, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw CatalogConflictException.VariantSku(command.Sku);
        }

        // ADR — see docs/DECISIONS.md: an item sells "as itself" or "as a variant", never both. The
        // reverse half of the rule — a barcode may not attach to an item that already has a variant —
        // lives in the domain (Barcode.CreateForItem); this half needs the barcode repository, which
        // the Item aggregate has no access to, so it is enforced here rather than inside Item.AddVariant.
        IReadOnlyList<Barcode> directBarcodes = await barcodes
            .ListForItemAsync(item.Id, cancellationToken)
            .ConfigureAwait(false);

        if (directBarcodes.Count > 0)
        {
            throw CatalogRuleException.ItemHasDirectBarcodesCannotAddVariant(item.Code);
        }

        ItemVariant variant = item.AddVariant(command.Sku, command.Attributes);

        variants.Add(variant);

        return variant.Id;
    }
}
