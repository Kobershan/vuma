using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Pos.Commands;

/// <summary>Opens a shift at the calling terminal with a counted float.</summary>
/// <param name="OpeningFloat">The cash placed in the drawer before trading. May be zero.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenTillSessionCommand(Money OpeningFloat) : ICommand<Guid>;

/// <summary>Rejects a malformed open-till command before it reaches the handler.</summary>
public sealed class OpenTillSessionCommandValidator : AbstractValidator<OpenTillSessionCommand>
{
    /// <summary>Builds the rules.</summary>
    public OpenTillSessionCommandValidator()
    {
        RuleFor(command => command.OpeningFloat.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.OpeningFloat.Currency).NotEmpty().Length(3);
    }
}

/// <summary>
/// Opens a till session, refusing a second one on a terminal that is already trading.
/// </summary>
/// <param name="sessions">Session lookup and insertion.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="principal">Who is at the till, and which till.</param>
/// <param name="clock">The only source of time.</param>
public sealed class OpenTillSessionCommandHandler(
    ITillSessionRepository sessions,
    ITenantContext tenant,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<OpenTillSessionCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(OpenTillSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid terminalId = PosActor.RequireTerminalId(principal);
        Guid operatorUserId = PosActor.RequireUserId(principal);

        TillSession? existing = await sessions
            .FindOpenForTerminalAsync(terminalId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            throw PosConflictException.TillSessionAlreadyOpen(terminalId);
        }

        TillSession session = TillSession.Open(
            tenant.TenantId,
            tenant.StoreId,
            terminalId,
            operatorUserId,
            command.OpeningFloat,
            clock.UtcNow);

        sessions.Add(session);

        return session.Id;
    }
}

/// <summary>
/// Counts the drawer and closes a shift. The result of the cash-up.
/// </summary>
/// <param name="ExpectedCash">What the system says should be in the drawer.</param>
/// <param name="CountedCash">What was physically counted.</param>
/// <param name="Variance">Counted less expected. Negative is a shortfall.</param>
public sealed record CashUpResult(Money ExpectedCash, Money CountedCash, Money Variance);

/// <summary>Counts the drawer and closes the shift permanently.</summary>
/// <param name="TillSessionId">The session to close.</param>
/// <param name="CountedCash">What was physically counted out of the drawer.</param>
/// <param name="Note">An optional explanation, which a non-zero variance usually wants.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CloseTillSessionCommand(
    Guid TillSessionId,
    Money CountedCash,
    string? Note = null) : ICommand<CashUpResult>;

/// <summary>Rejects a malformed close-till command before it reaches the handler.</summary>
public sealed class CloseTillSessionCommandValidator : AbstractValidator<CloseTillSessionCommand>
{
    /// <summary>Builds the rules.</summary>
    public CloseTillSessionCommandValidator()
    {
        RuleFor(command => command.TillSessionId).NotEmpty();
        RuleFor(command => command.CountedCash.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.CountedCash.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Note).MaximumLength(1000);
    }
}

/// <summary>
/// Derives the expected cash from the session's own completed sales, then closes against the count.
/// </summary>
/// <remarks>
/// The expected figure is computed here, from <see cref="Sale.CashContribution"/>, and is never an
/// input. That is the whole control: a cash-up where the expected amount can be supplied by the person
/// being counted tells you nothing. See <c>STAGE-09</c> business rule 9.
/// </remarks>
/// <param name="sessions">Session lookup.</param>
/// <param name="sales">The session's sales, which the expected cash is derived from.</param>
/// <param name="principal">Who counted the drawer.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CloseTillSessionCommandHandler(
    ITillSessionRepository sessions,
    ISaleRepository sales,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<CloseTillSessionCommand, CashUpResult>
{
    /// <inheritdoc />
    public async Task<CashUpResult> HandleAsync(
        CloseTillSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TillSession session = await sessions.FindAsync(command.TillSessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("till session", command.TillSessionId);

        session.EnsureOpen();

        int unfinished = await sales
            .CountUnfinishedForSessionAsync(session.Id, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<Sale> sessionSales = await sales
            .ListForSessionAsync(session.Id, cancellationToken)
            .ConfigureAwait(false);

        Money cashTaken = sessionSales.Aggregate(
            Money.Zero(session.Currency),
            (running, sale) => running + sale.CashContribution);

        session.Close(
            command.CountedCash,
            cashTaken,
            unfinished,
            PosActor.RequireUserId(principal),
            clock.UtcNow,
            command.Note);

        return new CashUpResult(
            session.ExpectedCash!.Value,
            session.CountedCash!.Value,
            session.Variance!.Value);
    }
}
