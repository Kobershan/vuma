#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.CustomerAccounts.Queries;

public sealed record StatementLine(
    DateOnly Date,
    string Description,
    Money Debit,
    Money Credit,
    Money RunningBalance);

public sealed record GetAccountStatementQuery(Guid AccountId, DateOnly From, DateOnly To)
    : IQuery<IReadOnlyList<StatementLine>>;

public sealed class GetAccountStatementQueryHandler(
    ICustomerAccountRepository accounts,
    IArInvoiceRepository arInvoices)
    : IQueryHandler<GetAccountStatementQuery, IReadOnlyList<StatementLine>>
{
    public async Task<IReadOnlyList<StatementLine>> HandleAsync(GetAccountStatementQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var account = await accounts.FindAsync(query.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new CustomerAccountExceptions("ACCOUNT_NOT_FOUND", $"No account with id {query.AccountId}.");
        IReadOnlyList<Domain.Finance.ArInvoice> open =
            await arInvoices.ListOpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<StatementLine>();
        Money running = Money.Zero(account.CreditLimit.Currency);
        foreach (var invoice in open
            .Where(i => i.PartnerId.Value == account.PartnerId
                && i.InvoiceDate >= query.From
                && i.InvoiceDate <= query.To)
            .OrderBy(i => i.InvoiceDate))
        {
            running += invoice.OutstandingBalance;
            rows.Add(new StatementLine(
                invoice.InvoiceDate,
                $"Invoice {invoice.InvoiceNumber}",
                invoice.Total,
                invoice.Total - invoice.OutstandingBalance,
                running));
        }

        return rows;
    }
}

public sealed record AgeingBucket(string Bucket, Money Balance);

public sealed record GetAccountAgeingQuery(Guid AccountId, DateOnly AsAt)
    : IQuery<IReadOnlyList<AgeingBucket>>;

public sealed class GetAccountAgeingQueryHandler(
    ICustomerAccountRepository accounts,
    IArInvoiceRepository arInvoices)
    : IQueryHandler<GetAccountAgeingQuery, IReadOnlyList<AgeingBucket>>
{
    public async Task<IReadOnlyList<AgeingBucket>> HandleAsync(GetAccountAgeingQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var account = await accounts.FindAsync(query.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new CustomerAccountExceptions("ACCOUNT_NOT_FOUND", $"No account with id {query.AccountId}.");
        IReadOnlyList<Domain.Finance.ArInvoice> open =
            await arInvoices.ListOpenAsync(cancellationToken).ConfigureAwait(false);

        string currency = account.CreditLimit.Currency;
        var buckets = new Dictionary<string, Money>
        {
            ["Current"] = Money.Zero(currency),
            ["30"] = Money.Zero(currency),
            ["60"] = Money.Zero(currency),
            ["90"] = Money.Zero(currency),
            ["120+"] = Money.Zero(currency),
        };
        foreach (var invoice in open.Where(i => i.PartnerId.Value == account.PartnerId))
        {
            int overdue = query.AsAt.DayNumber - invoice.DueDate.DayNumber;
            string bucket = overdue <= 0 ? "Current" : overdue <= 30 ? "30" : overdue <= 60 ? "60" : overdue <= 90 ? "90" : "120+";
            buckets[bucket] += invoice.OutstandingBalance;
        }

        return buckets.Select(kvp => new AgeingBucket(kvp.Key, kvp.Value)).ToList();
    }
}

public sealed record CheckCreditLimitQuery(
    Guid AccountId,
    decimal TenderAmount,
    string Currency,
    decimal QueuedOfflineAmount,
    Guid? HolderUserId = null) : IQuery<CreditCheckResult>;

public sealed record CreditCheckResult(bool Approved, Money Available, string? RefusalReason);

public sealed class CheckCreditLimitQueryHandler(
    ICustomerAccountRepository accounts,
    IAccountHolderRepository holders,
    IArInvoiceRepository arInvoices)
    : IQueryHandler<CheckCreditLimitQuery, CreditCheckResult>
{
    public async Task<CreditCheckResult> HandleAsync(CheckCreditLimitQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var account = await accounts.FindAsync(query.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new CustomerAccountExceptions("ACCOUNT_NOT_FOUND", $"No account with id {query.AccountId}.");
        if (account.Status != AccountStatus.Active)
        {
            return new CreditCheckResult(false, Money.Zero(query.Currency), $"Account is {account.Status}.");
        }

        var tender = new Money(query.TenderAmount, query.Currency);
        if (query.HolderUserId.HasValue)
        {
            IReadOnlyList<AccountHolder> authorised =
                await holders.ListForAccountAsync(query.AccountId, cancellationToken).ConfigureAwait(false);
            AccountHolder? holder = authorised.FirstOrDefault(h => h.UserId == query.HolderUserId.Value);
            if (holder is null)
            {
                return new CreditCheckResult(false, Money.Zero(query.Currency), "Buyer is not authorised on this account.");
            }

            if (tender.Amount > holder.ChargeLimit.Amount)
            {
                return new CreditCheckResult(false, Money.Zero(query.Currency), "Charge exceeds the buyer's personal limit.");
            }
        }
        IReadOnlyList<Domain.Finance.ArInvoice> open =
            await arInvoices.ListOpenAsync(cancellationToken).ConfigureAwait(false);
        Money outstanding = open
            .Where(i => i.PartnerId.Value == account.PartnerId)
            .Aggregate(Money.Zero(query.Currency), (sum, i) => sum + i.OutstandingBalance);
        Money available = account.CreditLimit - outstanding - new Money(query.QueuedOfflineAmount, query.Currency);
        if (tender.Amount > available.Amount)
        {
            return new CreditCheckResult(false, available, "Tender exceeds the available credit.");
        }

        return new CreditCheckResult(true, available, null);
    }
}
