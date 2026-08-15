using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Pos.Commands;

/// <summary>
/// Opens a sale at the calling terminal's open till session.
/// </summary>
/// <param name="SaleId">
/// The sale's identity, or <c>null</c> to mint one here. A terminal that rang this sale up offline
/// supplies the id it already printed on the customer's slip, which makes the replay idempotent —
/// re-sending the same id returns the existing sale instead of creating a second one (business rule 12).
/// </param>
/// <param name="LocationId">The stock location the goods leave.</param>
/// <param name="CustomerId">The customer, when one was identified.</param>
/// <param name="Currency">The currency to ring the sale up in. Defaults to the store's.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenSaleCommand(
    Guid? SaleId,
    Guid LocationId,
    Guid? CustomerId = null,
    string Currency = "ZAR") : ICommand<Guid>;

/// <summary>Rejects a malformed open-sale command before it reaches the handler.</summary>
public sealed class OpenSaleCommandValidator : AbstractValidator<OpenSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenSaleCommandValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
    }
}

/// <summary>
/// Opens a sale, or returns the existing one when a terminal replays an id it already used.
/// </summary>
/// <param name="sales">Sale lookup and insertion.</param>
/// <param name="sessions">Resolves the terminal's open till session.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="principal">Who is at the till, and which till.</param>
/// <param name="clock">The only source of time.</param>
public sealed class OpenSaleCommandHandler(
    ISaleRepository sales,
    ITillSessionRepository sessions,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<OpenSaleCommand, Guid>
{
    /// <summary>The document number series a receipt number is drawn from.</summary>
    public const string SaleNumberSeries = "SALE";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(OpenSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid terminalId = PosActor.RequireTerminalId(principal);
        Guid operatorUserId = PosActor.RequireUserId(principal);

        if (command.SaleId is { } replayed)
        {
            Sale? already = await sales.FindAsync(replayed, cancellationToken).ConfigureAwait(false);

            if (already is not null)
            {
                // The idempotent replay. Deliberately returns rather than refusing: a till that lost
                // its acknowledgement and retried has done nothing wrong, and refusing would leave it
                // with a sale it can neither continue nor abandon.
                return already.Id;
            }
        }

        TillSession session = await sessions.FindOpenForTerminalAsync(terminalId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("open till session for terminal", terminalId);

        string saleNumber = await numbers.NextAsync(SaleNumberSeries, cancellationToken).ConfigureAwait(false);

        Sale sale = Sale.Open(
            command.SaleId ?? UuidV7.NewGuid(),
            tenant.TenantId,
            tenant.StoreId,
            saleNumber,
            session,
            operatorUserId,
            command.LocationId,
            command.CustomerId,
            command.Currency,
            clock.UtcNow);

        sales.Add(sale);

        return sale.Id;
    }
}

/// <summary>Rings a line up on an open sale.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/>.</param>
/// <param name="ItemVariantId">The variant. Exactly one of this and <paramref name="ItemId"/>.</param>
/// <param name="Quantity">How much. Must be positive.</param>
/// <param name="UnitPrice">
/// What one unit is being sold for. Supplied by the caller because Stage 06 shipped no price and the
/// price list is Stage 10's (ADR-072) — and because a weighed or open-price line has no shelf price to
/// look up regardless.
/// </param>
/// <param name="DiscountAmount">A manual discount off this line, or <c>null</c> for none.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddSaleLineCommand(
    Guid SaleId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Money UnitPrice,
    Money? DiscountAmount = null) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddSaleLineCommandValidator : AbstractValidator<AddSaleLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddSaleLineCommandValidator()
    {
        RuleFor(command => command.SaleId).NotEmpty();
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
        RuleFor(command => command.UnitPrice.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.UnitPrice.Currency).NotEmpty().Length(3);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
        RuleFor(command => command.DiscountAmount!.Value.Amount)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.DiscountAmount is not null);
    }

    private static bool HaveExactlyOneItemOrVariant(AddSaleLineCommand command)
        => (command.ItemId is not null) != (command.ItemVariantId is not null);
}

/// <summary>
/// Resolves the item, prices the line through the tax rules engine, and adds it to the sale.
/// </summary>
/// <remarks>
/// The tax figures are computed here, once, and stored on the line. A rate change next month must not
/// restate a receipt a customer is holding, so nothing recomputes them afterwards
/// (<c>STAGE-09</c> business rule 5).
/// </remarks>
/// <param name="sales">Sale lookup.</param>
/// <param name="catalog">Resolves the item's description, unit of measure and tax class.</param>
/// <param name="tax">Stage 07's tax rules engine, through its published port.</param>
/// <param name="clock">The only source of time — the date the tax rule is resolved on.</param>
public sealed class AddSaleLineCommandHandler(
    ISaleRepository sales,
    ISellableItemResolver catalog,
    ITaxCalculator tax,
    IClock clock) : ICommandHandler<AddSaleLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AddSaleLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        sale.EnsureOpen();

        SellableItem item = await catalog
            .ResolveAsync(command.ItemId, command.ItemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(item.UnitOfMeasureCode, command.Quantity.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw Domain.Inventory.InventoryRuleException.UnitOfMeasureMismatch(
                item.UnitOfMeasureCode, command.Quantity.UnitOfMeasure);
        }

        Money discount = command.DiscountAmount ?? Money.Zero(command.UnitPrice.Currency);
        Money extended = command.UnitPrice * command.Quantity.Value;
        Money charged = (extended - discount).RoundToCurrencyScale();

        // The rule decides whether `charged` is inclusive or exclusive — a South African shelf price is
        // inclusive, a wholesale list is not, and the till should not have to know which.
        TaxCalculation calculation = await tax
            .CalculateAsync(item.TaxClassCode, charged, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), cancellationToken)
            .ConfigureAwait(false);

        SaleLine line = SaleLine.Ring(
            sale.TenantId,
            sale.StoreId,
            sale.Id,
            sale.NextLineNumber,
            item.ItemId,
            item.ItemVariantId,
            item.Description,
            command.Quantity,
            command.UnitPrice,
            discount,
            calculation.TaxCode,
            calculation.NetAmount,
            calculation.TaxAmount,
            calculation.GrossAmount);

        sale.AddLine(line);

        return line.Id;
    }
}

/// <summary>Takes a line off an open sale. The line stays on the record.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="SaleLineId">The line to void.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record VoidSaleLineCommand(Guid SaleId, Guid SaleLineId) : ICommand;

/// <summary>Rejects a malformed void-line command before it reaches the handler.</summary>
public sealed class VoidSaleLineCommandValidator : AbstractValidator<VoidSaleLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public VoidSaleLineCommandValidator()
    {
        RuleFor(command => command.SaleId).NotEmpty();
        RuleFor(command => command.SaleLineId).NotEmpty();
    }
}

/// <summary>Voids a line and refreshes the sale's totals.</summary>
/// <param name="sales">Sale lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class VoidSaleLineCommandHandler(ISaleRepository sales, IClock clock)
    : ICommandHandler<VoidSaleLineCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(VoidSaleLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        sale.VoidLine(command.SaleLineId, clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Takes a payment against an open sale.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="TenderType">How it was paid.</param>
/// <param name="Amount">How much. Must be positive.</param>
/// <param name="Reference">The reference the tender left behind — a card authorisation code, a voucher serial.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record TenderSaleCommand(
    Guid SaleId,
    TenderType TenderType,
    Money Amount,
    string? Reference = null) : ICommand<Guid>;

/// <summary>Rejects a malformed tender command before it reaches the handler.</summary>
public sealed class TenderSaleCommandValidator : AbstractValidator<TenderSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public TenderSaleCommandValidator()
    {
        RuleFor(command => command.SaleId).NotEmpty();
        RuleFor(command => command.TenderType).IsInEnum();
        RuleFor(command => command.Amount.Amount).GreaterThan(0m);
        RuleFor(command => command.Amount.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Reference).MaximumLength(128);
    }
}

/// <summary>Captures the payment.</summary>
/// <param name="sales">Sale lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class TenderSaleCommandHandler(ISaleRepository sales, IClock clock)
    : ICommandHandler<TenderSaleCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(TenderSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        SaleTender tender = SaleTender.Capture(
            sale.TenantId,
            sale.StoreId,
            sale.Id,
            command.TenderType,
            command.Amount,
            command.Reference,
            clock.UtcNow);

        sale.AddTender(tender);

        return tender.Id;
    }
}

/// <summary>What a completed sale hands back to the till.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="SaleNumber">The receipt number.</param>
/// <param name="Gross">What was owed.</param>
/// <param name="AmountTendered">What was taken.</param>
/// <param name="ChangeGiven">What to hand back, in cash.</param>
/// <param name="StockIssuesRefused">
/// How many lines completed without relieving stock (ADR-073). Zero on a normal sale; anything else is
/// a reconciliation the store owes itself.
/// </param>
public sealed record SaleCompletionResult(
    Guid SaleId,
    string SaleNumber,
    Money Gross,
    Money AmountTendered,
    Money ChangeGiven,
    int StockIssuesRefused);

/// <summary>Closes the sale: freezes it, relieves stock and raises the financial event.</summary>
/// <param name="SaleId">The sale.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteSaleCommand(Guid SaleId) : ICommand<SaleCompletionResult>;

/// <summary>Rejects a malformed complete-sale command before it reaches the handler.</summary>
public sealed class CompleteSaleCommandValidator : AbstractValidator<CompleteSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public CompleteSaleCommandValidator() => RuleFor(command => command.SaleId).NotEmpty();
}

/// <summary>Delegates to <see cref="ISaleCompletionService"/> and reports what happened.</summary>
/// <param name="sales">Sale lookup.</param>
/// <param name="completion">The three steps completion always takes, in one place.</param>
public sealed class CompleteSaleCommandHandler(ISaleRepository sales, ISaleCompletionService completion)
    : ICommandHandler<CompleteSaleCommand, SaleCompletionResult>
{
    /// <inheritdoc />
    public async Task<SaleCompletionResult> HandleAsync(
        CompleteSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        await completion.CompleteAsync(sale, cancellationToken).ConfigureAwait(false);

        return new SaleCompletionResult(
            sale.Id,
            sale.SaleNumber,
            sale.Gross,
            sale.AmountTendered,
            sale.ChangeGiven,
            sale.LiveLines.Count(line => line.StockIssue is StockIssueStatus.Refused));
    }
}

/// <summary>Sets a sale aside so the terminal can serve the next customer.</summary>
/// <param name="SaleId">The sale.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ParkSaleCommand(Guid SaleId) : ICommand;

/// <summary>Rejects a malformed park command before it reaches the handler.</summary>
public sealed class ParkSaleCommandValidator : AbstractValidator<ParkSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public ParkSaleCommandValidator() => RuleFor(command => command.SaleId).NotEmpty();
}

/// <summary>Parks the sale.</summary>
/// <param name="sales">Sale lookup.</param>
public sealed class ParkSaleCommandHandler(ISaleRepository sales) : ICommandHandler<ParkSaleCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ParkSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        sale.Park();

        return Unit.Value;
    }
}

/// <summary>Brings a parked sale back to the screen.</summary>
/// <param name="SaleId">The sale.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ResumeSaleCommand(Guid SaleId) : ICommand;

/// <summary>Rejects a malformed resume command before it reaches the handler.</summary>
public sealed class ResumeSaleCommandValidator : AbstractValidator<ResumeSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public ResumeSaleCommandValidator() => RuleFor(command => command.SaleId).NotEmpty();
}

/// <summary>Resumes the sale.</summary>
/// <param name="sales">Sale lookup.</param>
public sealed class ResumeSaleCommandHandler(ISaleRepository sales) : ICommandHandler<ResumeSaleCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ResumeSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        sale.Resume();

        return Unit.Value;
    }
}

/// <summary>Abandons a sale before it is paid for.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="Reason">Why. Recorded, because an abandoned sale is what shrinkage looks like from outside.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record VoidSaleCommand(Guid SaleId, string Reason) : ICommand;

/// <summary>Rejects a malformed void-sale command before it reaches the handler.</summary>
public sealed class VoidSaleCommandValidator : AbstractValidator<VoidSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public VoidSaleCommandValidator()
    {
        RuleFor(command => command.SaleId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>Voids the sale.</summary>
/// <param name="sales">Sale lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class VoidSaleCommandHandler(ISaleRepository sales, IClock clock)
    : ICommandHandler<VoidSaleCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(VoidSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        sale.Void(command.Reason, clock.UtcNow);

        return Unit.Value;
    }
}
