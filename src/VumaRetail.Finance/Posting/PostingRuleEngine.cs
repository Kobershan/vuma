using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Finance.Posting;

/// <summary>
/// The mechanism CLAUDE.md §7 rule 12 refers to: turns a raised <see cref="IFinancialEvent"/> into a
/// balanced, posted GL journal by resolving the tenant-configured <see cref="PostingRule"/> for its
/// event type.
/// </summary>
/// <remarks>
/// This is the only implementation of <see cref="IFinancialEventPoster"/> in the solution, and the
/// only code path that reads a <see cref="PostingRuleLine.AccountId"/> and puts it on a journal line.
/// A producer module hands this an event; it never sees an account id go the other way.
/// </remarks>
/// <param name="postingRules">Where posting rules are configured.</param>
/// <param name="periods">The tenant's accounting calendar.</param>
/// <param name="journals">The general ledger.</param>
/// <param name="numbers">Issues the journal's human-readable number.</param>
/// <param name="clock">The only source of time.</param>
public sealed class PostingRuleEngine(
    IPostingRuleRepository postingRules,
    IAccountingPeriodRepository periods,
    IJournalRepository journals,
    IDocumentNumberSequence numbers,
    IClock clock) : IFinancialEventPoster
{
    /// <summary>The document-number series every engine-posted journal draws from.</summary>
    public const string JournalNumberSeries = "JNL";

    /// <inheritdoc />
    /// <exception cref="PostingRuleNotFoundException">No active rule maps the event's type.</exception>
    /// <exception cref="NoOpenPeriodException">No open period covers the event's date.</exception>
    /// <exception cref="UnknownFinancialEventAmountException">
    /// A rule line references an amount key the event does not carry.
    /// </exception>
    public async Task<Guid> PostAsync(IFinancialEvent financialEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(financialEvent);

        PostingRule rule = await postingRules
            .FindActiveByEventTypeAsync(financialEvent.EventType, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PostingRuleNotFoundException(financialEvent.EventType);

        DateOnly postingDate = DateOnly.FromDateTime(financialEvent.OccurredAt.UtcDateTime);

        AccountingPeriod period = await periods.FindOpenPeriodForDateAsync(postingDate, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NoOpenPeriodException(postingDate);

        List<JournalLineDraft> lines = [];

        foreach (PostingRuleLine ruleLine in rule.Lines)
        {
            if (!financialEvent.Amounts.TryGetValue(ruleLine.AmountKey, out Money amount))
            {
                throw new UnknownFinancialEventAmountException(financialEvent.EventType, ruleLine.AmountKey);
            }

            Guid? departmentId = ruleLine.InheritDimensions ? financialEvent.DepartmentId : null;
            Guid? costCentreId = ruleLine.InheritDimensions ? financialEvent.CostCentreId : null;
            Guid? projectId = ruleLine.InheritDimensions ? financialEvent.ProjectId : null;
            Guid? channelId = ruleLine.InheritDimensions ? financialEvent.ChannelId : null;
            Guid? employeeId = ruleLine.InheritDimensions ? financialEvent.EmployeeId : null;

            lines.Add(ruleLine.Side is NormalBalance.Debit
                ? new JournalLineDraft(
                    ruleLine.AccountId, amount, null, ruleLine.Description,
                    departmentId, costCentreId, projectId, channelId, employeeId)
                : new JournalLineDraft(
                    ruleLine.AccountId, null, amount, ruleLine.Description,
                    departmentId, costCentreId, projectId, channelId, employeeId));
        }

        string journalNumber = await numbers.NextAsync(JournalNumberSeries, cancellationToken).ConfigureAwait(false);

        Journal journal = Journal.Post(
            financialEvent.TenantId,
            financialEvent.StoreId,
            period.Id,
            journalNumber,
            clock.UtcNow,
            "system:posting-engine",
            sourceModule: financialEvent.EventType.Split('.', 2)[0],
            sourceEventType: financialEvent.EventType,
            sourceReference: financialEvent.SourceReference,
            lines: lines,
            narration: rule.Description);

        journals.Add(journal);

        return journal.Id;
    }
}
