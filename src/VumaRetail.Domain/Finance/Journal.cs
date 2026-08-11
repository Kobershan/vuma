using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// A balanced double-entry posting to the general ledger (ADR-016, CLAUDE.md §7 rule 7).
/// </summary>
/// <remarks>
/// <para>
/// There is no draft state. A journal is assembled in memory, checked for balance, and inserted
/// once — <see cref="IImmutableRecord"/>, so the persistence layer refuses any update or delete
/// against it afterwards. A journal is corrected by <see cref="ReversalOfJournalId"/> naming a new,
/// equal-and-opposite journal, never by editing this one.
/// </para>
/// <para>
/// <see cref="Lines"/> is populated at construction and never added to afterwards; <see cref="Post"/>
/// is the only way to obtain a balanced <see cref="Journal"/>, and it throws rather than returning
/// one that does not balance.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class Journal : Entity, IImmutableRecord
{
    private readonly List<JournalLine> _lines = [];

    private Journal(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Journal()
    {
    }

    /// <summary>The accounting period this journal posts into.</summary>
    public Guid AccountingPeriodId { get; private set; }

    /// <summary>A human-readable, per-tenant sequential reference, for example <c>JNL-000042</c>.</summary>
    public string JournalNumber { get; private set; } = string.Empty;

    /// <summary>When the posting happened, UTC, from <c>IClock</c>.</summary>
    public DateTimeOffset PostedAt { get; private set; }

    /// <summary>Who or what posted it — a user, or <c>system:posting-engine</c> for an event-raised journal.</summary>
    public string PostedBy { get; private set; } = string.Empty;

    /// <summary>
    /// The module that raised the event this journal was posted for, for example <c>ar</c> or <c>pos</c>.
    /// <c>manual</c> for a journal an accountant entered directly.
    /// </summary>
    public string SourceModule { get; private set; } = string.Empty;

    /// <summary>
    /// The financial event type this journal answers, for example <c>ar.invoice.posted</c>, or
    /// <c>manual</c>.
    /// </summary>
    public string SourceEventType { get; private set; } = string.Empty;

    /// <summary>A free-text reference back to the source document — an invoice number, a sale id.</summary>
    public string SourceReference { get; private set; } = string.Empty;

    /// <summary>A narration for the journal as a whole.</summary>
    public string Narration { get; private set; } = string.Empty;

    /// <summary>
    /// The journal this one reverses, or <c>null</c>. The reversal is itself an ordinary immutable
    /// journal; this is the only link between the two.
    /// </summary>
    public Guid? ReversalOfJournalId { get; private set; }

    /// <summary>The lines making up this posting. Never empty; always balanced per currency.</summary>
    public IReadOnlyList<JournalLine> Lines => _lines;

    /// <summary>
    /// Builds and validates a balanced journal. Throws rather than returning an unbalanced one.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store this journal belongs to, if any.</param>
    /// <param name="accountingPeriodId">The open period this posts into.</param>
    /// <param name="journalNumber">The per-tenant sequential reference.</param>
    /// <param name="postedAt">When the posting happens, UTC.</param>
    /// <param name="postedBy">Who or what is posting it.</param>
    /// <param name="sourceModule">The raising module, or <c>manual</c>.</param>
    /// <param name="sourceEventType">The financial event type, or <c>manual</c>.</param>
    /// <param name="sourceReference">A reference back to the source document.</param>
    /// <param name="lines">
    /// The lines to post. Each must carry either a debit or a credit amount, never both and never
    /// neither, and every amount must share one currency.
    /// </param>
    /// <param name="narration">A narration for the journal.</param>
    /// <param name="reversalOfJournalId">The journal this reverses, if this is a reversal.</param>
    /// <exception cref="JournalNotBalancedException">Debits and credits do not agree.</exception>
    /// <exception cref="JournalHasNoLinesException">No lines were supplied.</exception>
    public static Journal Post(
        Guid tenantId,
        Guid? storeId,
        Guid accountingPeriodId,
        string journalNumber,
        DateTimeOffset postedAt,
        string postedBy,
        string sourceModule,
        string sourceEventType,
        string sourceReference,
        IReadOnlyList<JournalLineDraft> lines,
        string narration = "",
        Guid? reversalOfJournalId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(postedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceModule);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventType);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new JournalHasNoLinesException();
        }

        Dictionary<string, decimal> balancePerCurrency = [];

        foreach (JournalLineDraft line in lines)
        {
            string currency = (line.Debit ?? line.Credit!.Value).Currency;
            decimal signed = line.Debit is { } debit ? debit.Amount : -line.Credit!.Value.Amount;
            balancePerCurrency[currency] = balancePerCurrency.GetValueOrDefault(currency) + signed;
        }

        var offenders = balancePerCurrency.Where(pair => pair.Value != 0m).ToList();

        if (offenders.Count > 0)
        {
            throw new JournalNotBalancedException(offenders.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        Journal journal = new(tenantId, storeId)
        {
            AccountingPeriodId = accountingPeriodId,
            JournalNumber = journalNumber.Trim(),
            PostedAt = postedAt,
            PostedBy = postedBy,
            SourceModule = sourceModule.Trim(),
            SourceEventType = sourceEventType.Trim(),
            SourceReference = sourceReference?.Trim() ?? string.Empty,
            Narration = narration?.Trim() ?? string.Empty,
            ReversalOfJournalId = reversalOfJournalId,
        };

        int lineNumber = 1;

        foreach (JournalLineDraft line in lines)
        {
            journal._lines.Add(JournalLine.Create(
                tenantId,
                storeId,
                journal.Id,
                lineNumber++,
                line.AccountId,
                line.Debit,
                line.Credit,
                line.Description,
                line.DepartmentId,
                line.CostCentreId,
                line.ProjectId,
                line.ChannelId,
                line.EmployeeId));
        }

        return journal;
    }
}

/// <summary>One side of a journal line before it is attached to a posted <see cref="Journal"/>.</summary>
/// <param name="AccountId">The GL account.</param>
/// <param name="Debit">The debit amount, or <c>null</c> if this line is a credit.</param>
/// <param name="Credit">The credit amount, or <c>null</c> if this line is a debit.</param>
/// <param name="Description">A line-level narration.</param>
/// <param name="DepartmentId">The department analysis dimension, bare opaque id.</param>
/// <param name="CostCentreId">The cost-centre analysis dimension, bare opaque id.</param>
/// <param name="ProjectId">The project analysis dimension, bare opaque id.</param>
/// <param name="ChannelId">The channel analysis dimension, bare opaque id.</param>
/// <param name="EmployeeId">The employee analysis dimension, bare opaque id.</param>
public sealed record JournalLineDraft(
    Guid AccountId,
    Money? Debit,
    Money? Credit,
    string Description = "",
    Guid? DepartmentId = null,
    Guid? CostCentreId = null,
    Guid? ProjectId = null,
    Guid? ChannelId = null,
    Guid? EmployeeId = null)
{
    /// <summary>A debit line.</summary>
    /// <param name="accountId">The GL account.</param>
    /// <param name="amount">The debit amount.</param>
    /// <param name="description">A line-level narration.</param>
    public static JournalLineDraft DebitLine(Guid accountId, Money amount, string description = "")
        => new(accountId, amount, null, description);

    /// <summary>A credit line.</summary>
    /// <param name="accountId">The GL account.</param>
    /// <param name="amount">The credit amount.</param>
    /// <param name="description">A line-level narration.</param>
    public static JournalLineDraft CreditLine(Guid accountId, Money amount, string description = "")
        => new(accountId, null, amount, description);
}
