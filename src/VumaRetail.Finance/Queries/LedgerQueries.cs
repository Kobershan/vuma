using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Finance.Queries;

/// <summary>One account, as read for the chart-of-accounts screen.</summary>
/// <param name="Id">The account.</param>
/// <param name="Code">The account code.</param>
/// <param name="Name">The account's name.</param>
/// <param name="Type">Which of the five classes it belongs to.</param>
/// <param name="ControlAccountType">What sub-ledger, if any, this reconciles to.</param>
/// <param name="Currency">The ISO 4217 currency it is denominated in.</param>
/// <param name="IsActive">Whether the account may still be posted to.</param>
public sealed record AccountView(
    Guid Id, string Code, string Name, AccountType Type, ControlAccountType ControlAccountType, string Currency, bool IsActive);

/// <summary>Lists the chart of accounts.</summary>
public sealed record ListAccountsQuery : IQuery<IReadOnlyList<AccountView>>;

/// <summary>Handles <see cref="ListAccountsQuery"/>.</summary>
/// <param name="accounts">The chart of accounts.</param>
public sealed class ListAccountsQueryHandler(IAccountRepository accounts)
    : IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountView>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountView>> HandleAsync(
        ListAccountsQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Account> all = await accounts.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return [.. all.Select(a => new AccountView(a.Id, a.Code, a.Name, a.Type, a.ControlAccountType, a.Currency, a.IsActive))];
    }
}

/// <summary>One row of a trial balance.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Code">The account code.</param>
/// <param name="Name">The account's name.</param>
/// <param name="Type">Which of the five classes it belongs to.</param>
/// <param name="Debit">The debit balance, or zero if the account carries a credit balance.</param>
/// <param name="Credit">The credit balance, or zero if the account carries a debit balance.</param>
public sealed record TrialBalanceLine(Guid AccountId, string Code, string Name, AccountType Type, decimal Debit, decimal Credit);

/// <summary>The trial balance as of a date: every account's balance, split into its debit or credit column.</summary>
/// <param name="AsOf">Only journals posted on or before this date are included.</param>
public sealed record GetTrialBalanceQuery(DateOnly AsOf) : IQuery<IReadOnlyList<TrialBalanceLine>>;

/// <summary>Handles <see cref="GetTrialBalanceQuery"/>.</summary>
/// <param name="accounts">The chart of accounts.</param>
/// <param name="journals">The general ledger.</param>
public sealed class GetTrialBalanceQueryHandler(IAccountRepository accounts, IJournalRepository journals)
    : IQueryHandler<GetTrialBalanceQuery, IReadOnlyList<TrialBalanceLine>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TrialBalanceLine>> HandleAsync(
        GetTrialBalanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Account> all = await accounts.ListAllAsync(cancellationToken).ConfigureAwait(false);
        List<TrialBalanceLine> lines = [];

        foreach (Account account in all)
        {
            decimal balance = await journals
                .GetAccountBalanceAsync(account.Id, query.AsOf, cancellationToken).ConfigureAwait(false);

            lines.Add(new TrialBalanceLine(
                account.Id, account.Code, account.Name, account.Type,
                Debit: balance > 0m ? balance : 0m,
                Credit: balance < 0m ? -balance : 0m));
        }

        return lines;
    }
}

/// <summary>The GL balance of a single account as of a date.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="AsOf">Only journals posted on or before this date are included.</param>
public sealed record GetAccountBalanceQuery(Guid AccountId, DateOnly AsOf) : IQuery<decimal>;

/// <summary>Handles <see cref="GetAccountBalanceQuery"/>.</summary>
/// <param name="journals">The general ledger.</param>
public sealed class GetAccountBalanceQueryHandler(IJournalRepository journals)
    : IQueryHandler<GetAccountBalanceQuery, decimal>
{
    /// <inheritdoc />
    public Task<decimal> HandleAsync(GetAccountBalanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return journals.GetAccountBalanceAsync(query.AccountId, query.AsOf, cancellationToken);
    }
}

/// <summary>One journal, summarised for a listing.</summary>
/// <param name="Id">The journal.</param>
/// <param name="JournalNumber">Its human-readable number.</param>
/// <param name="PostedAt">When it posted, UTC.</param>
/// <param name="SourceModule">The raising module, or <c>manual</c>.</param>
/// <param name="SourceEventType">The financial event type, or <c>manual</c>.</param>
/// <param name="Narration">The journal's narration.</param>
/// <param name="LineCount">How many lines it carries.</param>
public sealed record JournalSummary(
    Guid Id, string JournalNumber, DateTimeOffset PostedAt, string SourceModule, string SourceEventType,
    string Narration, int LineCount);

/// <summary>Lists the most recently posted journals.</summary>
/// <param name="Limit">The maximum number to return.</param>
public sealed record ListJournalsQuery(int Limit = 50) : IQuery<IReadOnlyList<JournalSummary>>;

/// <summary>Handles <see cref="ListJournalsQuery"/>.</summary>
/// <param name="journals">The general ledger.</param>
public sealed class ListJournalsQueryHandler(IJournalRepository journals)
    : IQueryHandler<ListJournalsQuery, IReadOnlyList<JournalSummary>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<JournalSummary>> HandleAsync(
        ListJournalsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Journal> recent = await journals
            .ListRecentAsync(query.Limit, cancellationToken).ConfigureAwait(false);

        return [.. recent.Select(j => new JournalSummary(
            j.Id, j.JournalNumber, j.PostedAt, j.SourceModule, j.SourceEventType, j.Narration, j.Lines.Count))];
    }
}

/// <summary>One line of a journal, as read for the journal detail screen.</summary>
/// <param name="LineNumber">The line's position.</param>
/// <param name="AccountId">The GL account.</param>
/// <param name="Debit">The debit amount, or <c>null</c> if this line is a credit.</param>
/// <param name="Credit">The credit amount, or <c>null</c> if this line is a debit.</param>
/// <param name="Description">A line-level narration.</param>
public sealed record JournalLineView(int LineNumber, Guid AccountId, decimal? Debit, decimal? Credit, string Description);

/// <summary>A journal in full, with its lines.</summary>
/// <param name="Id">The journal.</param>
/// <param name="JournalNumber">Its human-readable number.</param>
/// <param name="PostedAt">When it posted, UTC.</param>
/// <param name="PostedBy">Who or what posted it.</param>
/// <param name="SourceModule">The raising module, or <c>manual</c>.</param>
/// <param name="SourceEventType">The financial event type, or <c>manual</c>.</param>
/// <param name="SourceReference">A reference back to the source document.</param>
/// <param name="Narration">The journal's narration.</param>
/// <param name="ReversalOfJournalId">The journal this one reverses, if any.</param>
/// <param name="Lines">The journal's lines.</param>
public sealed record JournalDetail(
    Guid Id, string JournalNumber, DateTimeOffset PostedAt, string PostedBy, string SourceModule,
    string SourceEventType, string SourceReference, string Narration, Guid? ReversalOfJournalId,
    IReadOnlyList<JournalLineView> Lines);

/// <summary>Fetches one journal in full.</summary>
/// <param name="JournalId">The journal.</param>
public sealed record GetJournalQuery(Guid JournalId) : IQuery<JournalDetail>;

/// <summary>Handles <see cref="GetJournalQuery"/>.</summary>
/// <param name="journals">The general ledger.</param>
public sealed class GetJournalQueryHandler(IJournalRepository journals)
    : IQueryHandler<GetJournalQuery, JournalDetail>
{
    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">The journal does not exist.</exception>
    public async Task<JournalDetail> HandleAsync(GetJournalQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Journal journal = await journals.FindByIdAsync(query.JournalId, cancellationToken).ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(Journal), query.JournalId);

        return new JournalDetail(
            journal.Id,
            journal.JournalNumber,
            journal.PostedAt,
            journal.PostedBy,
            journal.SourceModule,
            journal.SourceEventType,
            journal.SourceReference,
            journal.Narration,
            journal.ReversalOfJournalId,
            [.. journal.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new JournalLineView(
                    line.LineNumber, line.AccountId, line.Debit?.Amount, line.Credit?.Amount, line.Description))]);
    }
}
