using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// One line of an imported bank statement — the reconciliation primitive.
/// </summary>
/// <remarks>
/// There is deliberately no separate "reconciliation run" entity. A statement line's own matched
/// state <em>is</em> the reconciliation: the bank control account's variance check
/// (<c>IPeriodVarianceChecker</c>) compares the GL balance against the sum of matched lines, so
/// matching a line is the whole act of reconciling it.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class BankStatementLine : Entity
{
    private BankStatementLine(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private BankStatementLine()
    {
    }

    /// <summary>The bank account this line belongs to.</summary>
    public Guid BankAccountId { get; private set; }

    /// <summary>The date the bank posted the transaction.</summary>
    public DateOnly TransactionDate { get; private set; }

    /// <summary>The bank's own description of the transaction.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// The signed amount: positive for money in, negative for money out, in the bank account's
    /// currency.
    /// </summary>
    public Money Amount { get; private set; }

    /// <summary>The bank's own reference for the transaction, used to avoid importing a line twice.</summary>
    public string ExternalReference { get; private set; } = string.Empty;

    /// <summary>The GL journal line this statement line has been matched to, once matched.</summary>
    public Guid? MatchedJournalLineId { get; private set; }

    /// <summary>When the match was made, UTC.</summary>
    public DateTimeOffset? MatchedAt { get; private set; }

    /// <summary>Who matched it.</summary>
    public string? MatchedBy { get; private set; }

    /// <summary>True once this line has been matched against a GL journal line.</summary>
    public bool IsMatched => MatchedJournalLineId is not null;

    /// <summary>Imports one statement line.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="transactionDate">The date the bank posted the transaction.</param>
    /// <param name="description">The bank's own description.</param>
    /// <param name="amount">The signed amount.</param>
    /// <param name="externalReference">The bank's own reference, for de-duplication.</param>
    public static BankStatementLine Import(
        Guid tenantId,
        Guid bankAccountId,
        DateOnly transactionDate,
        string description,
        Money amount,
        string externalReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalReference);

        return new BankStatementLine(tenantId)
        {
            BankAccountId = bankAccountId,
            TransactionDate = transactionDate,
            Description = description?.Trim() ?? string.Empty,
            Amount = amount,
            ExternalReference = externalReference.Trim(),
        };
    }

    /// <summary>Matches this line against a GL journal line, reconciling it.</summary>
    /// <param name="journalLineId">The journal line this statement line corresponds to.</param>
    /// <param name="matchedBy">Who matched it.</param>
    /// <param name="at">When, UTC.</param>
    public void Match(Guid journalLineId, string matchedBy, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchedBy);

        MatchedJournalLineId = journalLineId;
        MatchedBy = matchedBy;
        MatchedAt = at;
    }

    /// <summary>Undoes a match, for example after it is found to be wrong.</summary>
    public void Unmatch()
    {
        MatchedJournalLineId = null;
        MatchedBy = null;
        MatchedAt = null;
    }
}
