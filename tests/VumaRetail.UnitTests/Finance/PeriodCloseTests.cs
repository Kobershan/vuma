using NSubstitute;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Commands;
using VumaRetail.Finance.Periods;
using static VumaRetail.UnitTests.Finance.FinanceTestContext;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// A period cannot close while a control account disagrees with its sub-ledger (ADR-016).
/// </summary>
/// <remarks>
/// This is the rule that keeps the sub-ledgers meaningful. Without it a disagreement between the AR
/// control account and the sum of open invoices rolls silently into the next period, and by the time
/// anyone notices, the period it originated in is closed and the evidence is months old.
/// </remarks>
public sealed class PeriodCloseTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IJournalRepository _journals = Substitute.For<IJournalRepository>();
    private readonly IArInvoiceRepository _arInvoices = Substitute.For<IArInvoiceRepository>();
    private readonly IApInvoiceRepository _apInvoices = Substitute.For<IApInvoiceRepository>();
    private readonly IBankAccountRepository _bankAccounts = Substitute.For<IBankAccountRepository>();
    private readonly IBankStatementLineRepository _statementLines = Substitute.For<IBankStatementLineRepository>();
    private readonly IAccountingPeriodRepository _periods = Substitute.For<IAccountingPeriodRepository>();
    private readonly FixedClock _clock = new(Now);
    private readonly AccountingPeriod _period = OpenPeriod();

    private PeriodVarianceChecker Checker
        => new(_accounts, _journals, _arInvoices, _apInvoices, _bankAccounts, _statementLines);

    private ClosePeriodCommandHandler Handler
        => new(_periods, Checker, new FixedPrincipal(), _clock);

    private void PeriodExists()
        => _periods.FindByIdAsync(_period.Id, Arg.Any<CancellationToken>()).Returns(_period);

    /// <summary>Sets up an AR control account whose GL balance is <paramref name="glBalance"/>.</summary>
    private Account ArControlAccount(decimal glBalance)
    {
        Account account = ControlAccount("1100", AccountType.Asset, ControlAccountType.AccountsReceivable);
        _accounts.ListControlAccountsAsync(Arg.Any<CancellationToken>()).Returns([account]);
        _journals.GetAccountBalanceAsync(account.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(glBalance);
        return account;
    }

    /// <summary>Puts one posted, unpaid AR invoice of <paramref name="amount"/> in the sub-ledger.</summary>
    private void OpenArInvoiceOf(decimal amount)
    {
        ArInvoice invoice = ArInvoice.Draft(
            TenantId, StoreId, PartnerId.From(UuidV7.NewGuid()), "INV-000001",
            Today, Today.AddDays(30), "ZAR");
        invoice.AddLine("Goods", Rand(amount), "STANDARD", Rand(0m));
        invoice.Post(UuidV7.NewGuid());

        _arInvoices.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([invoice]);
    }

    [Fact]
    public async Task A_period_closes_when_every_control_account_agrees_with_its_sub_ledger()
    {
        PeriodExists();
        ArControlAccount(glBalance: 115m);
        OpenArInvoiceOf(115m);

        await Handler.HandleAsync(new ClosePeriodCommand(_period.Id));

        _period.Status.Should().Be(PeriodStatus.Closed);
        _period.ClosedBy.Should().Be("test:accountant");
        _period.ClosedAt.Should().Be(Now);
    }

    [Fact]
    public async Task A_period_will_not_close_while_a_control_account_disagrees_with_its_sub_ledger()
    {
        PeriodExists();
        Account control = ArControlAccount(glBalance: 115m);
        OpenArInvoiceOf(100m);

        Func<Task> closing = () => Handler.HandleAsync(new ClosePeriodCommand(_period.Id));

        PeriodCloseBlockedException blocked =
            (await closing.Should().ThrowAsync<PeriodCloseBlockedException>()).Which;
        blocked.Variances.Should().ContainSingle()
            .Which.Should().Match<ControlAccountVariance>(variance =>
                variance.AccountId == control.Id
                && variance.GlBalance == 115m
                && variance.SubLedgerBalance == 100m
                && variance.Variance == 15m);

        _period.Status.Should().Be(PeriodStatus.Open);
    }

    [Fact]
    public async Task A_period_that_would_not_close_does_close_once_the_disagreement_is_resolved()
    {
        // The same period, the same check, one invoice changed — this is the pair that proves the
        // gate is reading the sub-ledger rather than refusing (or allowing) unconditionally.
        PeriodExists();
        ArControlAccount(glBalance: 115m);
        OpenArInvoiceOf(100m);

        Func<Task> firstAttempt = () => Handler.HandleAsync(new ClosePeriodCommand(_period.Id));
        await firstAttempt.Should().ThrowAsync<PeriodCloseBlockedException>();

        OpenArInvoiceOf(115m);
        await Handler.HandleAsync(new ClosePeriodCommand(_period.Id));

        _period.Status.Should().Be(PeriodStatus.Closed);
    }

    [Fact]
    public async Task A_reconciled_control_account_still_appears_in_the_check()
    {
        // A clean run has to be distinguishable from nothing having been checked — otherwise an
        // empty result reads as "all clear" whether or not the checker ran.
        ArControlAccount(glBalance: 115m);
        OpenArInvoiceOf(115m);

        IReadOnlyList<ControlAccountVariance> variances = await Checker.CheckAsync(_period);

        variances.Should().ContainSingle().Which.Variance.Should().Be(0m);
    }

    [Fact]
    public async Task A_bank_control_account_reconciles_against_matched_statement_lines_only()
    {
        Account account = ControlAccount("1200", AccountType.Asset, ControlAccountType.Bank);
        BankAccount bank = BankAccount.Open(TenantId, account.Id, "Cheque account", "62001234567", "ZAR");
        _accounts.ListControlAccountsAsync(Arg.Any<CancellationToken>()).Returns([account]);
        _journals.GetAccountBalanceAsync(account.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(500m);
        _bankAccounts.FindByGlAccountIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(bank);
        _bankStatementReconciledBalanceIs(bank.Id, 500m);

        IReadOnlyList<ControlAccountVariance> variances = await Checker.CheckAsync(_period);

        variances.Should().ContainSingle().Which.Variance.Should().Be(0m);
    }

    [Fact]
    public async Task An_unmatched_statement_line_leaves_the_bank_control_account_out_of_balance()
    {
        Account account = ControlAccount("1200", AccountType.Asset, ControlAccountType.Bank);
        BankAccount bank = BankAccount.Open(TenantId, account.Id, "Cheque account", "62001234567", "ZAR");
        _accounts.ListControlAccountsAsync(Arg.Any<CancellationToken>()).Returns([account]);
        _journals.GetAccountBalanceAsync(account.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(500m);
        _bankAccounts.FindByGlAccountIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(bank);
        _bankStatementReconciledBalanceIs(bank.Id, 300m);

        IReadOnlyList<ControlAccountVariance> variances = await Checker.CheckAsync(_period);

        variances.Should().ContainSingle().Which.Variance.Should().Be(200m);
    }

    [Fact]
    public void A_period_cannot_be_closed_twice()
    {
        _period.Close("user:first", Now);

        Action closingAgain = () => _period.Close("user:second", Now);

        closingAgain.Should().Throw<PeriodAlreadyClosedException>();
        _period.ClosedBy.Should().Be("user:first");
    }

    [Fact]
    public void Reopening_a_period_clears_the_close_and_lets_it_be_closed_again()
    {
        _period.Close("user:first", Now);

        _period.Reopen();

        _period.Status.Should().Be(PeriodStatus.Open);
        _period.ClosedAt.Should().BeNull();
        _period.ClosedBy.Should().BeNull();
    }

    [Fact]
    public void A_period_covers_its_own_dates_inclusively()
    {
        _period.Covers(new DateOnly(2026, 5, 31)).Should().BeFalse();
        _period.Covers(new DateOnly(2026, 6, 1)).Should().BeTrue();
        _period.Covers(new DateOnly(2026, 6, 30)).Should().BeTrue();
        _period.Covers(new DateOnly(2026, 7, 1)).Should().BeFalse();
    }

    [Fact]
    public void A_period_cannot_be_opened_ending_before_it_starts()
    {
        Action opening = () => AccountingPeriod.Open(
            TenantId, new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 1));

        opening.Should().Throw<ArgumentException>();
    }

    private void _bankStatementReconciledBalanceIs(Guid bankAccountId, decimal balance)
        => _statementLines.GetReconciledBalanceAsync(bankAccountId, Arg.Any<CancellationToken>())
            .Returns(balance);
}
