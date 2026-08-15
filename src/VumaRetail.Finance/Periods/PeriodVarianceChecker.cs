using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Finance.Periods;

/// <summary>
/// Compares every control account's GL balance against its sub-ledger — the check ADR-016 requires
/// before a period may close, and the same check the daily job runs across every open period.
/// </summary>
/// <remarks>
/// Compares <em>current</em> balances rather than a point-in-time snapshot as of the period's own end
/// date: the sub-ledger repositories report what is open now, not what was open on a past date. For
/// the common case — checking or closing the period that is currently open — this is exactly right.
/// Reconciling a period that is not the most recent open one from historical sub-ledger state is a
/// documented gap; see <c>docs/PROGRESS.md</c> §3.
/// </remarks>
/// <param name="accounts">The chart of accounts.</param>
/// <param name="journals">The general ledger.</param>
/// <param name="arInvoices">The AR sub-ledger.</param>
/// <param name="apInvoices">The AP sub-ledger.</param>
/// <param name="bankAccounts">Bank accounts, paired with their GL control account.</param>
/// <param name="bankStatementLines">Bank statement lines and their matched state.</param>
public sealed class PeriodVarianceChecker(
    IAccountRepository accounts,
    IJournalRepository journals,
    IArInvoiceRepository arInvoices,
    IApInvoiceRepository apInvoices,
    IBankAccountRepository bankAccounts,
    IBankStatementLineRepository bankStatementLines)
{
    /// <summary>
    /// Checks every control account for a period, returning one entry per account — including
    /// reconciled ones, so a clean run is distinguishable from nothing having been checked.
    /// </summary>
    /// <param name="period">The period to check.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<IReadOnlyList<ControlAccountVariance>> CheckAsync(
        AccountingPeriod period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        IReadOnlyList<Account> controlAccounts = await accounts
            .ListControlAccountsAsync(cancellationToken).ConfigureAwait(false);

        List<ControlAccountVariance> results = [];

        foreach (Account account in controlAccounts)
        {
            decimal glBalance = await journals
                .GetAccountBalanceAsync(account.Id, period.PeriodEnd, cancellationToken)
                .ConfigureAwait(false);

            decimal subLedgerBalance = await GetSubLedgerBalanceAsync(account, cancellationToken)
                .ConfigureAwait(false);

            results.Add(new ControlAccountVariance(account.Id, account.ControlAccountType, glBalance, subLedgerBalance));
        }

        return results;
    }

    private async Task<decimal> GetSubLedgerBalanceAsync(Account account, CancellationToken cancellationToken)
    {
        switch (account.ControlAccountType)
        {
            case ControlAccountType.AccountsReceivable:
                IReadOnlyList<ArInvoice> openArInvoices = await arInvoices
                    .ListOpenAsync(cancellationToken).ConfigureAwait(false);
                return openArInvoices.Sum(invoice => invoice.OutstandingBalance.Amount);

            case ControlAccountType.AccountsPayable:
                IReadOnlyList<ApInvoice> openApInvoices = await apInvoices
                    .ListOpenAsync(cancellationToken).ConfigureAwait(false);
                return openApInvoices.Sum(invoice => invoice.OutstandingBalance.Amount);

            case ControlAccountType.Bank:
                BankAccount? bankAccount = await bankAccounts
                    .FindByGlAccountIdAsync(account.Id, cancellationToken).ConfigureAwait(false);
                return bankAccount is null
                    ? 0m
                    : await bankStatementLines
                        .GetReconciledBalanceAsync(bankAccount.Id, cancellationToken)
                        .ConfigureAwait(false);

            case ControlAccountType.None:
            default:
                return 0m;
        }
    }
}
