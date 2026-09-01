using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Sales.Commands;

/// <summary>
/// Records that an operator sold something at a price the resolver did not give them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This command does not price anything and does not refuse anything</b> (business rule 9). The till
/// has already decided to sell off-price — that is what the <c>sales.price.override</c> permission
/// grants — and this is the row that makes the decision visible afterwards. Refusing here would only
/// teach the shop floor to stop recording overrides, not to stop making them.
/// </para>
/// <para>
/// It is a separate call rather than a field on <c>AddSaleLineCommand</c> because ADR-072 says Stage 10
/// changes what fills the price in, not the shape of the sale line — and because an override can happen
/// before a line exists, on a quote, or on a lay-by priced at the counter.
/// </para>
/// </remarks>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/>.</param>
/// <param name="ItemVariantId">The variant. Exactly one of this and <paramref name="ItemId"/>.</param>
/// <param name="Quantity">How much was sold at the overridden price.</param>
/// <param name="ResolvedUnitPrice">What the resolver said one unit should cost.</param>
/// <param name="ActualUnitPrice">What one unit was actually sold for.</param>
/// <param name="Reason">Why. Required — an override with no reason is the one nobody can investigate.</param>
/// <param name="SaleId">The sale it happened on, when there was one by then.</param>
/// <param name="SaleLineId">The line it happened on, when the line already existed.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordPriceOverrideCommand(
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Money ResolvedUnitPrice,
    Money ActualUnitPrice,
    string Reason,
    Guid? SaleId = null,
    Guid? SaleLineId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed override command before it reaches the handler.</summary>
public sealed class RecordPriceOverrideCommandValidator : AbstractValidator<RecordPriceOverrideCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordPriceOverrideCommandValidator()
    {
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command.ResolvedUnitPrice.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.ActualUnitPrice.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.ResolvedUnitPrice.Currency).NotEmpty().Length(3);
        RuleFor(command => command.ActualUnitPrice.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(RecordPriceOverrideCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>Appends the override to the immutable log.</summary>
/// <param name="overrides">The append-only override log.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="principal">Who did it.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RecordPriceOverrideCommandHandler(
    IPriceOverrideLogRepository overrides,
    ITenantContext tenant,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<RecordPriceOverrideCommand, Guid>
{
    /// <inheritdoc />
    public Task<Guid> HandleAsync(
        RecordPriceOverrideCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PriceOverrideLog entry = PriceOverrideLog.Record(
            tenant.TenantId,
            tenant.StoreId,
            command.SaleId,
            command.SaleLineId,
            command.ItemId,
            command.ItemVariantId,
            SalesActor.RequireUserId(principal),
            command.Quantity,
            command.ResolvedUnitPrice,
            command.ActualUnitPrice,
            command.Reason,
            clock.UtcNow);

        overrides.Add(entry);

        return Task.FromResult(entry.Id);
    }
}
