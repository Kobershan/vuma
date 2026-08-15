using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Finance.Queries;

/// <summary>Compares a bank account's GL balance against its reconciled (matched) statement lines.</summary>
/// <param name="BankAccountId">The GL balance, as of today.</param>
/// <param name="GlBalance">The GL balance, as of today.</param>
/// <param name="ReconciledBalance">The sum of every matched statement line.</param>
/// <param name="Variance">The difference. Zero means fully reconciled.</param>
public sealed record BankReconciliationSummary(Guid BankAccountId, decimal GlBalance, decimal ReconciledBalance, decimal Variance);

/// <summary>Reconciliation status for one bank account, as of today.</summary>
/// <param name="BankAccountId">The bank account.</param>
/// <param name="AsOf">The date to compare the GL balance as of.</param>
public sealed record GetBankReconciliationSummaryQuery(Guid BankAccountId, DateOnly AsOf)
    : IQuery<BankReconciliationSummary>;

/// <summary>Handles <see cref="GetBankReconciliationSummaryQuery"/>.</summary>
/// <param name="bankAccounts">Bank accounts.</param>
/// <param name="journals">The general ledger.</param>
/// <param name="statementLines">Imported statement lines.</param>
public sealed class GetBankReconciliationSummaryQueryHandler(
    IBankAccountRepository bankAccounts, IJournalRepository journals, IBankStatementLineRepository statementLines)
    : IQueryHandler<GetBankReconciliationSummaryQuery, BankReconciliationSummary>
{
    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">The bank account does not exist.</exception>
    public async Task<BankReconciliationSummary> HandleAsync(
        GetBankReconciliationSummaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        BankAccount account = await bankAccounts.FindByIdAsync(query.BankAccountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(BankAccount), query.BankAccountId);

        decimal glBalance = await journals
            .GetAccountBalanceAsync(account.GlAccountId, query.AsOf, cancellationToken).ConfigureAwait(false);

        decimal reconciled = await statementLines
            .GetReconciledBalanceAsync(query.BankAccountId, cancellationToken).ConfigureAwait(false);

        return new BankReconciliationSummary(query.BankAccountId, glBalance, reconciled, glBalance - reconciled);
    }
}
