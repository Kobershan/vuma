using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Licensing;
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
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenSaleCommand(
    Guid? SaleId,
    Guid LocationId,
    Guid? CustomerId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed open-sale command before it reaches the handler.</summary>
public sealed class OpenSaleCommandValidator : AbstractValidator<OpenSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenSaleCommandValidator() => RuleFor(command => command.LocationId).NotEmpty();
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
/// <param name="openSales">§4.10: registers the sale as in flight for the read-only carve-out.</param>
/// <param name="windows">The carve-out's hard deadlines.</param>
public sealed class OpenSaleCommandHandler(
    ISaleRepository sales,
    ITillSessionRepository sessions,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IPrincipalAccessor principal,
    IClock clock,
    IOpenSessionRegistry openSales,
    IOpenSessionWindows windows) : ICommandHandler<OpenSaleCommand, Guid>
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

        // §4.13: the sale's currency is never taken from the caller — it is always the till session's,
        // which is itself resolved from the store/tenant at OpenTillSessionCommand time. A sale can
        // therefore never diverge from the drawer it is rung up against.
        Sale sale = Sale.Open(
            command.SaleId ?? UuidV7.NewGuid(),
            tenant.TenantId,
            tenant.StoreId,
            saleNumber,
            session,
            operatorUserId,
            command.LocationId,
            command.CustomerId,
            session.Currency,
            clock.UtcNow);

        sales.Add(sale);

        // §4.10: registered unconditionally, whatever the tenant's current enforcement level, so the
        // registry already knows this sale was in flight at the instant a restriction ever falls due —
        // a carve-out wired in after the fact is a carve-out that ends up half-applied.
        openSales.Open(sale.Id, sale.OpenedAt, windows.InFlightSaleWindow);

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
/// <param name="SaleLineId">
/// The line's identity, or <c>null</c> to mint one here. A terminal that rang this line up offline
/// supplies the id it already used, which makes the replay idempotent — re-sending the same id returns
/// the existing line instead of appending a second one (§4.11).
/// </param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddSaleLineCommand(
    Guid SaleId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Quantity,
    Money UnitPrice,
    Money? DiscountAmount = null,
    Guid? SaleLineId = null) : ICommand<Guid>, ISessionScopedCommand
{
    /// <inheritdoc />
    Guid ISessionScopedCommand.SessionId => SaleId;
}

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

        if (command.SaleLineId is { } replayedLineId)
        {
            SaleLine? already = sale.Lines.FirstOrDefault(line => line.Id == replayedLineId);

            if (already is not null)
            {
                // The idempotent replay (§4.11) — same shape as OpenSaleCommand's. Checked before
                // EnsureOpen so a late-arriving replay of a line the sale already has is a no-op even if
                // the sale has since completed, rather than a refusal for doing nothing new.
                return already.Id;
            }
        }

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

        if (!string.Equals(discount.Currency, command.UnitPrice.Currency, StringComparison.Ordinal))
        {
            throw PosRuleException.CurrencyMismatch(command.UnitPrice.Currency, discount.Currency);
        }

        // Round once, at the currency's own presentation scale, straight from the full-precision
        // product (Money.cs's own doc comment: "Rounding to the currency's own scale happens once").
        // Going through `command.UnitPrice * command.Quantity.Value` first would construct a Money and
        // force a 4dp round there, then round *again* to 2dp here — two sequential midpoint roundings,
        // which is §4.14: a 6dp weighed quantity lands in the `[x.xx495, x.xx5)` gap often enough to
        // bias every such line up by a cent.
        decimal extendedLessDiscount = (command.UnitPrice.Amount * command.Quantity.Value) - discount.Amount;
        Money charged = new Money(decimal.Round(extendedLessDiscount, 2, Money.Rounding), command.UnitPrice.Currency);

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
            calculation.GrossAmount,
            command.SaleLineId);

        sale.AddLine(line);

        return line.Id;
    }
}

/// <summary>Takes a line off an open sale. The line stays on the record.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="SaleLineId">The line to void.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record VoidSaleLineCommand(Guid SaleId, Guid SaleLineId) : ICommand, ISessionScopedCommand
{
    /// <inheritdoc />
    Guid ISessionScopedCommand.SessionId => SaleId;
}

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
/// <param name="SaleTenderId">
/// The tender's identity, or <c>null</c> to mint one here. A replay with the id already used returns
/// the existing tender instead of taking the payment twice (§4.11).
/// </param>
[CommandSideEffect(SideEffect.Write)]
public sealed record TenderSaleCommand(
    Guid SaleId,
    TenderType TenderType,
    Money Amount,
    string? Reference = null,
    Guid? SaleTenderId = null) : ICommand<Guid>, ISessionScopedCommand
{
    /// <inheritdoc />
    Guid ISessionScopedCommand.SessionId => SaleId;
}

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

        if (command.SaleTenderId is { } replayedTenderId)
        {
            SaleTender? already = sale.Tenders.FirstOrDefault(tender => tender.Id == replayedTenderId);

            if (already is not null)
            {
                // The idempotent replay (§4.11) — a lost acknowledgement must not take the payment
                // twice.
                return already.Id;
            }
        }

        SaleTender tender = SaleTender.Capture(
            sale.TenantId,
            sale.StoreId,
            sale.Id,
            command.TenderType,
            command.Amount,
            command.Reference,
            clock.UtcNow,
            command.SaleTenderId);

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
public sealed record CompleteSaleCommand(Guid SaleId) : ICommand<SaleCompletionResult>, ISessionScopedCommand
{
    /// <inheritdoc />
    Guid ISessionScopedCommand.SessionId => SaleId;
}

/// <summary>Rejects a malformed complete-sale command before it reaches the handler.</summary>
public sealed class CompleteSaleCommandValidator : AbstractValidator<CompleteSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public CompleteSaleCommandValidator() => RuleFor(command => command.SaleId).NotEmpty();
}

/// <summary>Delegates to <see cref="ISaleCompletionService"/> and reports what happened.</summary>
/// <param name="sales">Sale lookup.</param>
/// <param name="completion">The three steps completion always takes, in one place.</param>
/// <param name="openSales">
/// §4.10: closed the moment a sale genuinely completes — it is no longer in flight and needs no
/// further read-only carve-out.
/// </param>
public sealed class CompleteSaleCommandHandler(
    ISaleRepository sales, ISaleCompletionService completion, IOpenSessionRegistry openSales)
    : ICommandHandler<CompleteSaleCommand, SaleCompletionResult>
{
    /// <inheritdoc />
    public async Task<SaleCompletionResult> HandleAsync(
        CompleteSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        // The idempotent replay (§4.11): a lost acknowledgement on the final step of the offline
        // sequence must not strand the till — the sale is already frozen, the stock already relieved
        // and the financial event already raised, so re-running ISaleCompletionService would either
        // throw SaleIsNotOpen or, worse, double every one of those effects. Report the same result the
        // first call already committed instead.
        if (sale.Status is not SaleStatus.Completed)
        {
            await completion.CompleteAsync(sale, cancellationToken).ConfigureAwait(false);
            openSales.Close(sale.Id);
        }

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
/// <remarks>
/// §4.10: deliberately <b>not</b> <c>ISessionScopedCommand</c>. Parking is how a sale is kept open
/// indefinitely, and exempting it while read-only would let the in-flight bound be defeated by simply
/// never un-parking — refused like any other write is the correct, safe behaviour here.
/// </remarks>
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
/// <remarks>
/// §4.10: deliberately <b>not</b> <c>ISessionScopedCommand</c>, and this one is critical rather than
/// merely conservative. Parked sales are a pre-existing pool of "already open" sales with no customer
/// at the counter and no cash in hand; exempting Resume would convert that whole pool into a trading
/// allowance the instant read-only fell due — the opposite of "no new sale may start".
/// </remarks>
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
/// <remarks>
/// §4.10: in the in-flight member set — abandoning is the safe direction, and refusing it while
/// read-only would strand exactly the sale the carve-out exists to let a cashier get off the screen.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record VoidSaleCommand(Guid SaleId, string Reason) : ICommand, ISessionScopedCommand
{
    /// <inheritdoc />
    Guid ISessionScopedCommand.SessionId => SaleId;
}

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
/// <param name="openSales">§4.10: closed the moment a sale is voided — it is no longer in flight.</param>
public sealed class VoidSaleCommandHandler(ISaleRepository sales, IClock clock, IOpenSessionRegistry openSales)
    : ICommandHandler<VoidSaleCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(VoidSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        sale.Void(command.Reason, clock.UtcNow);
        openSales.Close(sale.Id);

        return Unit.Value;
    }
}
