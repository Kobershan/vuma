using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Assembles consolidated reports from each company's published period figures.
/// Read-only, labelled, carries watermark/AsAt, names stale contributors (ADR-106, ADR-119).
/// No consolidated number can be mistaken for a filing (ADR-106).
/// </summary>
public sealed class ConsolidationService : IConsolidationService
{
    private readonly ICompanyFanOut _companyFanOut;
    private readonly IGroupReceiptRepository _repository;

    public ConsolidationService(
        ICompanyFanOut companyFanOut,
        IGroupReceiptRepository repository)
    {
        _companyFanOut = companyFanOut;
        _repository = repository;
    }

    public async Task<ConsolidatedTrialBalance> GetTrialBalanceAsync(
        Guid tenantId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        // Fan out to each company and collect period figures
        IReadOnlyList<CompanyPeriodFigure> figures = await _companyFanOut.GetPeriodFiguresAsync(
            tenantId, asOf, cancellationToken);

        List<CompanyContributorInfo> contributors = [];
        List<ConsolidatedAccountLine> accounts = [];

        foreach (CompanyPeriodFigure figure in figures)
        {
            contributors.Add(new CompanyContributorInfo
            {
                CompanyId = figure.CompanyId,
                CompanyName = figure.CompanyName,
                AsAt = figure.AsAt,
                IsStale = figure.IsStale,
                StaleReason = figure.StaleReason,
            });

            // Aggregate accounts across companies, eliminating inter-company clearing
            foreach (CompanyAccountBalance balance in figure.Accounts)
            {
                if (balance.AccountCode.StartsWith("ICCLR", StringComparison.OrdinalIgnoreCase))
                    continue; // Eliminate inter-company clearing accounts

                ConsolidatedAccountLine? existing = accounts.FirstOrDefault(a => a.AccountCode == balance.AccountCode);
                if (existing is not null)
                {
                    accounts.Remove(existing);
                    accounts.Add(new ConsolidatedAccountLine
                    {
                        AccountCode = balance.AccountCode,
                        AccountName = balance.AccountName,
                        AccountType = balance.AccountType,
                        Debit = existing.Debit + balance.Debit,
                        Credit = existing.Credit + balance.Credit,
                    });
                }
                else
                {
                    accounts.Add(new ConsolidatedAccountLine
                    {
                        AccountCode = balance.AccountCode,
                        AccountName = balance.AccountName,
                        AccountType = balance.AccountType,
                        Debit = balance.Debit,
                        Credit = balance.Credit,
                    });
                }
            }
        }

        return new ConsolidatedTrialBalance
        {
            AsOf = asOf,
            Accounts = accounts,
            Contributors = contributors,
        };
    }

    public async Task<ConsolidatedIncomeStatement> GetIncomeStatementAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CompanyPeriodFigure> figures = await _companyFanOut.GetPeriodFiguresAsync(
            tenantId, to, cancellationToken);

        List<CompanyContributorInfo> contributors = [];
        List<ConsolidatedAccountLine> incomeAccounts = [];
        List<ConsolidatedAccountLine> expenseAccounts = [];

        foreach (CompanyPeriodFigure figure in figures)
        {
            contributors.Add(new CompanyContributorInfo
            {
                CompanyId = figure.CompanyId,
                CompanyName = figure.CompanyName,
                AsAt = figure.AsAt,
                IsStale = figure.IsStale,
                StaleReason = figure.StaleReason,
            });

            foreach (CompanyAccountBalance balance in figure.Accounts)
            {
                if (balance.AccountCode.StartsWith("ICCLR", StringComparison.OrdinalIgnoreCase))
                    continue; // Eliminate inter-company clearing

                ConsolidatedAccountLine line = new()
                {
                    AccountCode = balance.AccountCode,
                    AccountName = balance.AccountName,
                    AccountType = balance.AccountType,
                    Debit = balance.Debit,
                    Credit = balance.Credit,
                };

                if (balance.AccountType is "Income")
                    incomeAccounts.Add(line);
                else if (balance.AccountType is "Expense")
                    expenseAccounts.Add(line);
            }
        }

        Money netIncome = incomeAccounts.Aggregate(
            Money.Zero("ZAR"),
            (sum, a) => sum + a.Credit - a.Debit);

        return new ConsolidatedIncomeStatement
        {
            From = from,
            To = to,
            IncomeAccounts = incomeAccounts,
            ExpenseAccounts = expenseAccounts,
            NetIncome = netIncome,
            Contributors = contributors,
        };
    }

    public async Task<ConsolidatedBalanceSheet> GetBalanceSheetAsync(
        Guid tenantId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CompanyPeriodFigure> figures = await _companyFanOut.GetPeriodFiguresAsync(
            tenantId, asOf, cancellationToken);

        List<CompanyContributorInfo> contributors = [];
        List<ConsolidatedAccountLine> assetAccounts = [];
        List<ConsolidatedAccountLine> liabilityAccounts = [];
        List<ConsolidatedAccountLine> equityAccounts = [];

        foreach (CompanyPeriodFigure figure in figures)
        {
            contributors.Add(new CompanyContributorInfo
            {
                CompanyId = figure.CompanyId,
                CompanyName = figure.CompanyName,
                AsAt = figure.AsAt,
                IsStale = figure.IsStale,
                StaleReason = figure.StaleReason,
            });

            foreach (CompanyAccountBalance balance in figure.Accounts)
            {
                if (balance.AccountCode.StartsWith("ICCLR", StringComparison.OrdinalIgnoreCase))
                    continue; // Eliminate inter-company clearing

                ConsolidatedAccountLine line = new()
                {
                    AccountCode = balance.AccountCode,
                    AccountName = balance.AccountName,
                    AccountType = balance.AccountType,
                    Debit = balance.Debit,
                    Credit = balance.Credit,
                };

                switch (balance.AccountType)
                {
                    case "Asset":
                        assetAccounts.Add(line);
                        break;
                    case "Liability":
                        liabilityAccounts.Add(line);
                        break;
                    case "Equity":
                        equityAccounts.Add(line);
                        break;
                }
            }
        }

        return new ConsolidatedBalanceSheet
        {
            AsOf = asOf,
            AssetAccounts = assetAccounts,
            LiabilityAccounts = liabilityAccounts,
            EquityAccounts = equityAccounts,
            Contributors = contributors,
        };
    }

    public async Task<IReadOnlyList<UnappliedLegDto>> GetUnappliedLegsAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InterCompanyClearingIntent> outstandingIntents =
            await _repository.GetOutstandingIntentsAsync(tenantId, cancellationToken);

        List<UnappliedLegDto> legs = [];

        foreach (InterCompanyClearingIntent intent in outstandingIntents)
        {
            foreach (InterCompanyClearingLeg leg in intent.Legs.Where(l =>
                l.State is InterCompanyClearingLegState.Pending or InterCompanyClearingLegState.Failed))
            {
                legs.Add(new UnappliedLegDto
                {
                    IntentId = intent.Id,
                    IntentType = intent.GroupDocumentType,
                    CompanyId = leg.CompanyId,
                    Amount = new Money(leg.Amount.Amount, leg.Currency),
                    Direction = leg.Direction,
                    CreatedAt = intent.CreatedAt,
                    AgeHours = (int)(DateTimeOffset.UtcNow - intent.CreatedAt).TotalHours,
                    LastError = leg.ErrorMessage,
                });
            }
        }

        return legs;
    }
}

/// <summary>Period figures published by one company database.</summary>
public sealed class CompanyPeriodFigure
{
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public DateTimeOffset AsAt { get; init; }
    public bool IsStale { get; init; }
    public string? StaleReason { get; init; }
    public IReadOnlyList<CompanyAccountBalance> Accounts { get; init; } = [];
}

/// <summary>One account balance from a company's period figures.</summary>
public sealed class CompanyAccountBalance
{
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public Domain.Primitives.Money Debit { get; init; }
    public Domain.Primitives.Money Credit { get; init; }
}

/// <summary>
/// Fan-out read across company databases for period figures (ADR-119).
/// Returns per-company results including failures — a fan-out where one company is down
/// returns that company as an error, not the whole call.
/// </summary>
public interface ICompanyFanOut
{
    Task<IReadOnlyList<CompanyPeriodFigure>> GetPeriodFiguresAsync(
        Guid tenantId, DateOnly asOf, CancellationToken cancellationToken = default);
}
