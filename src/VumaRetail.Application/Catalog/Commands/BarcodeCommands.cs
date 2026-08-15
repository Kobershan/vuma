using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Catalog;

namespace VumaRetail.Application.Catalog.Commands;

/// <summary>Attaches a new barcode to an item or a variant. Exactly one of the two owners must be set.</summary>
/// <param name="ItemId">The item, when the item has no variants. Mutually exclusive with <see cref="ItemVariantId"/>.</param>
/// <param name="ItemVariantId">The variant. Mutually exclusive with <see cref="ItemId"/>.</param>
/// <param name="Code">The scanned value.</param>
/// <param name="Symbology">What kind of barcode this is.</param>
/// <param name="IsPrimary">
/// Whether this should become the owner's primary barcode. Always true for an owner's first barcode,
/// whatever is passed here.
/// </param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddBarcodeCommand(
    Guid? ItemId,
    Guid? ItemVariantId,
    string Code,
    BarcodeSymbology Symbology,
    bool IsPrimary = false) : ICommand<Guid>;

/// <summary>Rejects a malformed add-barcode command before it reaches the handler.</summary>
public sealed class AddBarcodeCommandValidator : AbstractValidator<AddBarcodeCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddBarcodeCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(64);

        RuleFor(command => command)
            .Must(command => command.ItemId.HasValue ^ command.ItemVariantId.HasValue)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }
}

/// <summary>
/// Attaches a barcode, refusing a value already assigned anywhere in the tenant's catalog and keeping
/// the owner's "exactly one primary" invariant.
/// </summary>
/// <param name="items">Item lookup, when attaching directly to an item.</param>
/// <param name="variants">Variant lookup, when attaching to a variant.</param>
/// <param name="barcodes">Barcode lookup and insertion.</param>
public sealed class AddBarcodeCommandHandler(
    IItemRepository items,
    IItemVariantRepository variants,
    IBarcodeRepository barcodes) : ICommandHandler<AddBarcodeCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AddBarcodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await barcodes.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw CatalogConflictException.BarcodeTaken(command.Code);
        }

        Barcode barcode;
        IReadOnlyList<Barcode> siblings;

        if (command.ItemVariantId is { } variantId)
        {
            ItemVariant variant = await variants.FindAsync(variantId, cancellationToken).ConfigureAwait(false)
                ?? throw new CatalogNotFoundException("item variant", variantId);

            barcode = Barcode.CreateForVariant(variant, command.Code, command.Symbology);
            siblings = await barcodes.ListForVariantAsync(variantId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Guid itemId = command.ItemId!.Value;

            Item item = await items.FindAsync(itemId, cancellationToken).ConfigureAwait(false)
                ?? throw new CatalogNotFoundException("item", itemId);

            barcode = Barcode.CreateForItem(item, command.Code, command.Symbology);
            siblings = await barcodes.ListForItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        }

        barcodes.Add(barcode);

        // The owner's first barcode is always primary — "exactly one primary barcode per item/variant
        // that has any barcode at all" cannot hold with zero of them marked, so the first one has no
        // choice in the matter.
        if (command.IsPrimary || siblings.Count == 0)
        {
            Barcode.SetPrimary([.. siblings, barcode], barcode.Id);
        }

        return barcode.Id;
    }
}

/// <summary>Makes a barcode its owner's primary, demoting whichever one held that place before.</summary>
/// <param name="BarcodeId">The barcode to promote.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record SetPrimaryBarcodeCommand(Guid BarcodeId) : ICommand;

/// <summary>Rejects a malformed set-primary-barcode command before it reaches the handler.</summary>
public sealed class SetPrimaryBarcodeCommandValidator : AbstractValidator<SetPrimaryBarcodeCommand>
{
    /// <summary>Builds the rules.</summary>
    public SetPrimaryBarcodeCommandValidator() => RuleFor(command => command.BarcodeId).NotEmpty();
}

/// <summary>Promotes a barcode to primary.</summary>
/// <param name="barcodes">Barcode lookup.</param>
public sealed class SetPrimaryBarcodeCommandHandler(IBarcodeRepository barcodes)
    : ICommandHandler<SetPrimaryBarcodeCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(SetPrimaryBarcodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Barcode barcode = await barcodes.FindAsync(command.BarcodeId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("barcode", command.BarcodeId);

        IReadOnlyList<Barcode> siblings = barcode.ItemVariantId is { } variantId
            ? await barcodes.ListForVariantAsync(variantId, cancellationToken).ConfigureAwait(false)
            : await barcodes.ListForItemAsync(barcode.ItemId!.Value, cancellationToken).ConfigureAwait(false);

        Barcode.SetPrimary(siblings, barcode.Id);

        return Unit.Value;
    }
}

/// <summary>
/// Removes a barcode outright — the one deliberate hard delete in this stage (see
/// <c>docs/DECISIONS.md</c>). A barcode carries no history of its own the way an item does.
/// </summary>
/// <param name="BarcodeId">The barcode to remove.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RemoveBarcodeCommand(Guid BarcodeId) : ICommand;

/// <summary>Rejects a malformed remove-barcode command before it reaches the handler.</summary>
public sealed class RemoveBarcodeCommandValidator : AbstractValidator<RemoveBarcodeCommand>
{
    /// <summary>Builds the rules.</summary>
    public RemoveBarcodeCommandValidator() => RuleFor(command => command.BarcodeId).NotEmpty();
}

/// <summary>
/// Removes a barcode, promoting a remaining sibling to primary if the removed one held that place.
/// </summary>
/// <param name="barcodes">Barcode lookup and removal.</param>
public sealed class RemoveBarcodeCommandHandler(IBarcodeRepository barcodes)
    : ICommandHandler<RemoveBarcodeCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(RemoveBarcodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Barcode barcode = await barcodes.FindAsync(command.BarcodeId, cancellationToken).ConfigureAwait(false)
            ?? throw new CatalogNotFoundException("barcode", command.BarcodeId);

        bool wasPrimary = barcode.IsPrimary;

        IReadOnlyList<Barcode> siblings = barcode.ItemVariantId is { } variantId
            ? await barcodes.ListForVariantAsync(variantId, cancellationToken).ConfigureAwait(false)
            : await barcodes.ListForItemAsync(barcode.ItemId!.Value, cancellationToken).ConfigureAwait(false);

        barcodes.Remove(barcode);

        if (wasPrimary)
        {
            List<Barcode> remaining = [.. siblings.Where(sibling => sibling.Id != barcode.Id)];

            if (remaining.Count > 0)
            {
                Barcode.SetPrimary(remaining, remaining[0].Id);
            }
        }

        return Unit.Value;
    }
}
