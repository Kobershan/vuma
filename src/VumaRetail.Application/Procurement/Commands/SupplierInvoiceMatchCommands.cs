using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement.Commands;

/// <summary>One line off a supplier's invoice, as it is being keyed in.</summary>
/// <param name="PurchaseOrderLineId">
/// The order line the supplier says it is for, or <c>null</c> when it is not on the order at all.
/// </param>
/// <param name="Description">What the supplier called it.</param>
/// <param name="Quantity">How much they are claiming.</param>
/// <param name="UnitCost">What they are charging per unit.</param>
public sealed record SupplierInvoiceLineInput(
    Guid? PurchaseOrderLineId, string Description, Quantity Quantity, Money UnitCost);

/// <summary>What a three-way match hands back to whoever ran it.</summary>
/// <param name="SupplierInvoiceMatchId">The match document, whatever its verdict.</param>
/// <param name="Status">The verdict.</param>
/// <param name="Variances">Which comparisons failed, when any did.</param>
/// <param name="ClaimedNet">What the supplier is claiming, excluding tax.</param>
/// <param name="MatchedNet">What the order's own costs support.</param>
/// <param name="PriceVariance">The difference. Positive means the supplier is charging more.</param>
/// <param name="IsPayable">True when the verdict permits a release (business rule 13).</param>
public sealed record ThreeWayMatchResult(
    Guid SupplierInvoiceMatchId,
    ThreeWayMatchStatus Status,
    ThreeWayMatchVarianceKind Variances,
    Money ClaimedNet,
    Money MatchedNet,
    Money PriceVariance,
    bool IsPayable);

/// <summary>Runs a three-way match of a supplier's invoice against an order and its receipts.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="SupplierInvoiceNumber">The supplier's own invoice number.</param>
/// <param name="InvoiceDate">The date on their invoice.</param>
/// <param name="ClaimedNet">What they are claiming, excluding tax.</param>
/// <param name="ClaimedTax">The tax they are claiming.</param>
/// <param name="Lines">Their invoice's lines.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record MatchSupplierInvoiceCommand(
    Guid PurchaseOrderId,
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    Money ClaimedNet,
    Money ClaimedTax,
    IReadOnlyList<SupplierInvoiceLineInput> Lines) : ICommand<ThreeWayMatchResult>;

/// <summary>Rejects a malformed match command before it reaches the handler.</summary>
public sealed class MatchSupplierInvoiceCommandValidator : AbstractValidator<MatchSupplierInvoiceCommand>
{
    /// <summary>Builds the rules.</summary>
    public MatchSupplierInvoiceCommandValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();
        RuleFor(command => command.SupplierInvoiceNumber).NotEmpty().MaximumLength(64);
        RuleFor(command => command.ClaimedNet.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.ClaimedTax.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.Lines).NotEmpty();

        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(input => input.Description).NotEmpty().MaximumLength(256);
            line.RuleFor(input => input.Quantity.Value).GreaterThan(0m);
            line.RuleFor(input => input.UnitCost.Amount).GreaterThanOrEqualTo(0m);
        });
    }
}

/// <summary>
/// Runs the comparison and stores the result — including a blocked one (ADR-082).
/// </summary>
/// <remarks>
/// <b>The document is written whatever the verdict, and this is the one handler where that matters
/// most.</b> A blocked match is not a failed command: the comparison ran, it produced an answer, and
/// the answer is "do not pay this". Throwing would return a 422 and store nothing, leaving the only
/// record of a supplier's over-billing in whoever happened to be keying it in.
/// </remarks>
/// <param name="matches">Match lookup and insertion.</param>
/// <param name="orders">The order being matched against.</param>
/// <param name="engine">The comparison itself.</param>
public sealed class MatchSupplierInvoiceCommandHandler(
    ISupplierInvoiceMatchRepository matches,
    IPurchaseOrderRepository orders,
    IThreeWayMatchEngine engine) : ICommandHandler<MatchSupplierInvoiceCommand, ThreeWayMatchResult>
{
    /// <inheritdoc />
    public async Task<ThreeWayMatchResult> HandleAsync(
        MatchSupplierInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PurchaseOrder order = await orders
            .FindAsync(command.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", command.PurchaseOrderId);

        // The one thing that is a hard refusal rather than a variance: the same invoice matched twice is
        // how one delivery gets paid for twice, and unlike a price difference there is nothing to judge.
        bool duplicate = await matches
            .ExistsForInvoiceNumberAsync(order.Id, command.SupplierInvoiceNumber, cancellationToken)
            .ConfigureAwait(false);

        if (duplicate)
        {
            throw ProcurementConflictException.DuplicateSupplierInvoice(command.SupplierInvoiceNumber);
        }

        SupplierInvoiceMatch match = await engine
            .MatchAsync(
                order,
                command.SupplierInvoiceNumber,
                command.InvoiceDate,
                command.ClaimedNet,
                command.ClaimedTax,
                [.. command.Lines.Select(line => new SupplierInvoiceLine(
                    line.PurchaseOrderLineId, line.Description, line.Quantity, line.UnitCost))],
                cancellationToken)
            .ConfigureAwait(false);

        matches.Add(match);

        return new ThreeWayMatchResult(
            match.Id,
            match.Status,
            match.Variances,
            match.ClaimedNet,
            match.MatchedNet,
            match.PriceVariance,
            match.IsPayable);
    }
}

/// <summary>What releasing a match hands back.</summary>
/// <param name="SupplierInvoiceMatchId">The match.</param>
/// <param name="SupplierInvoiceNumber">The supplier's invoice number.</param>
/// <param name="Gross">What the supplier will be paid.</param>
/// <param name="JournalId">The GL journal that was posted, or <c>null</c> if nothing posted one.</param>
/// <param name="OrderStatus">Where the order stands now.</param>
public sealed record SupplierInvoiceReleaseResult(
    Guid SupplierInvoiceMatchId,
    string SupplierInvoiceNumber,
    Money Gross,
    Guid? JournalId,
    PurchaseOrderStatus OrderStatus);

/// <summary>Releases a matched invoice for payment. The act that raises the financial event.</summary>
/// <param name="SupplierInvoiceMatchId">The match.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReleaseSupplierInvoiceCommand(Guid SupplierInvoiceMatchId)
    : ICommand<SupplierInvoiceReleaseResult>;

/// <summary>Rejects a malformed release command before it reaches the handler.</summary>
public sealed class ReleaseSupplierInvoiceCommandValidator
    : AbstractValidator<ReleaseSupplierInvoiceCommand>
{
    /// <summary>Builds the rules.</summary>
    public ReleaseSupplierInvoiceCommandValidator()
        => RuleFor(command => command.SupplierInvoiceMatchId).NotEmpty();
}

/// <summary>
/// Releases the match: freezes it, writes its quantities back onto the order, and raises
/// <c>procurement.invoice.matched</c>.
/// </summary>
/// <remarks>
/// <para>
/// The order of the three steps is the same argument <c>GoodsReceiptCompletionService</c> makes.
/// Releasing is the step that can legitimately refuse — a blocked match is the caller's error — so it
/// goes first and a refusal writes nothing back and raises nothing.
/// </para>
/// <para>
/// Writing the invoiced quantities back onto the order lines is what makes business rule 11 cumulative
/// across several invoices for one order (business rule 14). It happens on <em>release</em> rather than
/// on match, because an unreleased match may well be the blocked one somebody is about to correct, and
/// counting it would block the correction too.
/// </para>
/// </remarks>
/// <param name="matches">Match lookup.</param>
/// <param name="orders">The order whose lines are advanced.</param>
/// <param name="financialEvents">Where the released match's financial event is raised.</param>
/// <param name="principal">Who is releasing it.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ReleaseSupplierInvoiceCommandHandler(
    ISupplierInvoiceMatchRepository matches,
    IPurchaseOrderRepository orders,
    IProcurementFinancialEventPublisher financialEvents,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<ReleaseSupplierInvoiceCommand, SupplierInvoiceReleaseResult>
{
    /// <inheritdoc />
    public async Task<SupplierInvoiceReleaseResult> HandleAsync(
        ReleaseSupplierInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid releasedBy = ProcurementActor.RequireUserId(principal);

        SupplierInvoiceMatch match = await matches
            .FindAsync(command.SupplierInvoiceMatchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException(
                "supplier invoice match", command.SupplierInvoiceMatchId);

        PurchaseOrder order = await orders
            .FindAsync(match.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("purchase order", match.PurchaseOrderId);

        match.Release(releasedBy, clock.UtcNow);

        foreach (SupplierInvoiceMatchLine line in match.Lines
            .Where(line => line.PurchaseOrderLineId is not null))
        {
            order.RecordInvoiced(line.PurchaseOrderLineId!.Value, line.InvoicedQuantity);
        }

        SupplierInvoiceMatchedEvent matched = new(
            match.TenantId,
            match.StoreId,
            match.Id,
            order.Id,
            order.OrderNumber,
            match.PartnerId,
            match.SupplierInvoiceNumber,
            match.ClaimedNet,
            match.ClaimedTax,
            match.ClaimedGross,
            match.ReleasedAt ?? clock.UtcNow);

        Guid? journalId = await financialEvents
            .PublishAsync(matched, cancellationToken)
            .ConfigureAwait(false);

        if (journalId is { } posted)
        {
            match.RecordJournal(posted);
        }

        return new SupplierInvoiceReleaseResult(
            match.Id, match.SupplierInvoiceNumber, match.ClaimedGross, journalId, order.Status);
    }
}

/// <summary>Takes a supplier scorecard snapshot for one supplier over one closed period.</summary>
/// <param name="PartnerId">The supplier.</param>
/// <param name="PeriodStart">The period's first day, inclusive.</param>
/// <param name="PeriodEnd">The period's last day, inclusive.</param>
/// <param name="Currency">The currency to report the money figures in.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record SnapshotSupplierScorecardCommand(
    Guid PartnerId, DateOnly PeriodStart, DateOnly PeriodEnd, string Currency) : ICommand<Guid>;

/// <summary>Rejects a malformed snapshot command before it reaches the handler.</summary>
public sealed class SnapshotSupplierScorecardCommandValidator
    : AbstractValidator<SnapshotSupplierScorecardCommand>
{
    /// <summary>Builds the rules.</summary>
    public SnapshotSupplierScorecardCommandValidator()
    {
        RuleFor(command => command.PartnerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);

        RuleFor(command => command.PeriodEnd)
            .GreaterThanOrEqualTo(command => command.PeriodStart)
            .WithMessage("The period's last day cannot fall before its first.");
    }
}

/// <summary>Counts the period and freezes the result (ADR-084).</summary>
/// <param name="scorecards">Scorecard lookup and insertion.</param>
/// <param name="calculator">The counting.</param>
/// <param name="tenant">The tenant and store the snapshot belongs to.</param>
/// <param name="clock">The only source of time — and of what "today" means for the closed-period rule.</param>
public sealed class SnapshotSupplierScorecardCommandHandler(
    ISupplierScorecardRepository scorecards,
    ISupplierScorecardCalculator calculator,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<SnapshotSupplierScorecardCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        SnapshotSupplierScorecardCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SupplierScorecard? existing = await scorecards
            .FindAsync(command.PartnerId, command.PeriodStart, command.PeriodEnd, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            throw ProcurementConflictException.DuplicateScorecard(command.PartnerId, command.PeriodStart);
        }

        SupplierScorecardFigures figures = await calculator
            .CalculateAsync(
                command.PartnerId, command.PeriodStart, command.PeriodEnd, command.Currency,
                cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = clock.UtcNow;

        SupplierScorecard created = SupplierScorecard.Snapshot(
            tenant.TenantId,
            tenant.StoreId,
            command.PartnerId,
            command.PeriodStart,
            command.PeriodEnd,
            DateOnly.FromDateTime(now.UtcDateTime),
            figures,
            now);

        scorecards.Add(created);

        return created.Id;
    }
}
