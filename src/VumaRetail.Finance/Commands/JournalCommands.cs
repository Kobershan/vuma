using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Posting;

namespace VumaRetail.Finance.Commands;

/// <summary>One line of a manual journal, before it is posted.</summary>
/// <param name="AccountId">The GL account.</param>
/// <param name="Debit">The debit amount, or <c>null</c> if this line is a credit.</param>
/// <param name="Credit">The credit amount, or <c>null</c> if this line is a debit.</param>
/// <param name="Description">A line-level narration.</param>
/// <param name="DepartmentId">The department analysis dimension, bare opaque id.</param>
/// <param name="CostCentreId">The cost-centre analysis dimension, bare opaque id.</param>
/// <param name="ProjectId">The project analysis dimension, bare opaque id.</param>
/// <param name="ChannelId">The channel analysis dimension, bare opaque id.</param>
/// <param name="EmployeeId">The employee analysis dimension, bare opaque id.</param>
public sealed record ManualJournalLineInput(
    Guid AccountId,
    decimal? Debit,
    decimal? Credit,
    string Description = "",
    Guid? DepartmentId = null,
    Guid? CostCentreId = null,
    Guid? ProjectId = null,
    Guid? ChannelId = null,
    Guid? EmployeeId = null);

/// <summary>
/// Posts a journal an accountant entered directly, rather than one the posting rules engine produced.
/// </summary>
/// <param name="PostingDate">The date to post into. Must fall inside an open period.</param>
/// <param name="Currency">The ISO 4217 currency every line is denominated in.</param>
/// <param name="Lines">The journal's lines. Must balance.</param>
/// <param name="Narration">A narration for the journal as a whole.</param>
/// <param name="Reference">A free-text reference, for example a source document number.</param>
/// <remarks>
/// A large manual posting is exactly the kind of write Stage 05's approval engine is meant to gate,
/// per <c>docs/stages/STAGE-07-finance.md</c>'s Scope note — but Stage 05 has not landed, so this
/// command posts unconditionally today. Wiring an approval policy onto it later needs no change to
/// this command's shape, only a pipeline registration once <c>IApprovalService</c> exists.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record PostManualJournalCommand(
    DateOnly PostingDate,
    string Currency,
    IReadOnlyList<ManualJournalLineInput> Lines,
    string Narration = "",
    string Reference = "") : ICommand<Guid>;

/// <summary>Validates <see cref="PostManualJournalCommand"/>.</summary>
public sealed class PostManualJournalCommandValidator : AbstractValidator<PostManualJournalCommand>
{
    /// <summary>Builds the rules.</summary>
    public PostManualJournalCommandValidator()
    {
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Lines).Must(lines => lines.Count >= 2)
            .WithMessage("A journal needs at least two lines to balance.");
        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AccountId).NotEmpty();
            line.RuleFor(l => l)
                .Must(l => (l.Debit is > 0 && l.Credit is null) || (l.Credit is > 0 && l.Debit is null))
                .WithMessage("Each line must carry exactly one of a positive debit or a positive credit.");
        });
    }
}

/// <summary>Handles <see cref="PostManualJournalCommand"/>.</summary>
/// <param name="periods">The tenant's accounting calendar.</param>
/// <param name="journals">The general ledger.</param>
/// <param name="accounts">The chart of accounts, to validate every referenced account exists.</param>
/// <param name="numbers">Issues the journal's number.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="principal">Who is acting.</param>
/// <param name="clock">The only source of time.</param>
public sealed class PostManualJournalCommandHandler(
    IAccountingPeriodRepository periods,
    IJournalRepository journals,
    IAccountRepository accounts,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<PostManualJournalCommand, Guid>
{
    /// <inheritdoc />
    /// <exception cref="NoOpenPeriodException">No open period covers the posting date.</exception>
    /// <exception cref="AccountNotFoundException">A line references an account that does not exist.</exception>
    /// <exception cref="JournalNotBalancedException">Debits and credits do not agree.</exception>
    public async Task<Guid> HandleAsync(PostManualJournalCommand command, CancellationToken cancellationToken = default)
    {
        AccountingPeriod period = await periods.FindOpenPeriodForDateAsync(command.PostingDate, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NoOpenPeriodException(command.PostingDate);

        List<JournalLineDraft> lines = [];

        foreach (ManualJournalLineInput line in command.Lines)
        {
            if (await accounts.FindByIdAsync(line.AccountId, cancellationToken).ConfigureAwait(false) is null)
            {
                throw new AccountNotFoundException(line.AccountId);
            }

            lines.Add(new JournalLineDraft(
                line.AccountId,
                line.Debit is { } debit ? new Money(debit, command.Currency) : null,
                line.Credit is { } credit ? new Money(credit, command.Currency) : null,
                line.Description,
                line.DepartmentId,
                line.CostCentreId,
                line.ProjectId,
                line.ChannelId,
                line.EmployeeId));
        }

        string journalNumber = await numbers.NextAsync(PostingRuleEngine.JournalNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        Journal journal = Journal.Post(
            tenant.TenantId,
            tenant.StoreId,
            period.Id,
            journalNumber,
            clock.UtcNow,
            principal.Principal,
            sourceModule: "manual",
            sourceEventType: "manual",
            sourceReference: command.Reference,
            lines: lines,
            narration: command.Narration);

        journals.Add(journal);

        return journal.Id;
    }
}

/// <summary>Reverses a posted journal with an equal-and-opposite one (CLAUDE.md §7 rule 7).</summary>
/// <param name="JournalId">The journal to reverse.</param>
/// <param name="Reason">Why it is being reversed.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReverseJournalCommand(Guid JournalId, string Reason) : ICommand<Guid>;

/// <summary>Validates <see cref="ReverseJournalCommand"/>.</summary>
public sealed class ReverseJournalCommandValidator : AbstractValidator<ReverseJournalCommand>
{
    /// <summary>Builds the rules.</summary>
    public ReverseJournalCommandValidator()
    {
        RuleFor(command => command.JournalId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(512);
    }
}

/// <summary>Handles <see cref="ReverseJournalCommand"/>.</summary>
/// <param name="journals">The general ledger.</param>
/// <param name="periods">The tenant's accounting calendar.</param>
/// <param name="numbers">Issues the reversal's number.</param>
/// <param name="principal">Who is acting.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ReverseJournalCommandHandler(
    IJournalRepository journals,
    IAccountingPeriodRepository periods,
    IDocumentNumberSequence numbers,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<ReverseJournalCommand, Guid>
{
    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">The journal does not exist.</exception>
    /// <exception cref="NoOpenPeriodException">No open period covers today.</exception>
    public async Task<Guid> HandleAsync(ReverseJournalCommand command, CancellationToken cancellationToken = default)
    {
        Journal original = await journals.FindByIdAsync(command.JournalId, cancellationToken).ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(Journal), command.JournalId);

        DateOnly reversalDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        AccountingPeriod period = await periods.FindOpenPeriodForDateAsync(reversalDate, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NoOpenPeriodException(reversalDate);

        // Every line swaps sides: what was a debit becomes a credit of the same amount, and vice
        // versa. A journal built this way from a balanced journal is itself balanced automatically.
        List<JournalLineDraft> reversedLines = [.. original.Lines.Select(line => new JournalLineDraft(
            line.AccountId,
            line.Credit,
            line.Debit,
            $"Reversal: {line.Description}",
            line.DepartmentId,
            line.CostCentreId,
            line.ProjectId,
            line.ChannelId,
            line.EmployeeId))];

        string journalNumber = await numbers.NextAsync(Posting.PostingRuleEngine.JournalNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        Journal reversal = Journal.Post(
            original.TenantId,
            original.StoreId,
            period.Id,
            journalNumber,
            clock.UtcNow,
            principal.Principal,
            sourceModule: original.SourceModule,
            sourceEventType: $"{original.SourceEventType}.reversed",
            sourceReference: original.SourceReference,
            lines: reversedLines,
            narration: $"Reversal of {original.JournalNumber}: {command.Reason}",
            reversalOfJournalId: original.Id);

        journals.Add(reversal);

        return reversal.Id;
    }
}
