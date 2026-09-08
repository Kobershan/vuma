using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.CustomerAccounts.Commands.Accounts;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// Limit changes and holds pass through the approval gate: no approval, no change — and the
/// gate is actually called, not just declared.
/// </summary>
public sealed class AccountCommandTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();

    private readonly ICustomerAccountRepository _accounts = Substitute.For<ICustomerAccountRepository>();
    private readonly IApprovalService _approvals = Substitute.For<IApprovalService>();

    [Fact]
    public async Task Limit_change_and_hold_need_approval()
    {
        CustomerAccount account = OpenAccount();
        _accounts.FindAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _approvals.EvaluateAsync(Arg.Any<ApprovalContext>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovalOutcome(ApprovalOutcomeKind.Pending, UuidV7.NewGuid()));

        var setLimit = new SetCreditLimitCommandHandler(_accounts, _approvals);
        Func<Task> raising = () => setLimit.HandleAsync(new SetCreditLimitCommand(account.Id, 9000m, "ZAR"));
        await raising.Should().ThrowAsync<CustomerAccountExceptions>()
            .WithMessage("*approval*");
        account.CreditLimit.Amount.Should().Be(5000m);

        var hold = new PlaceAccountHoldCommandHandler(_accounts, _approvals);
        Func<Task> freezing = () => hold.HandleAsync(new PlaceAccountHoldCommand(account.Id, "Review."));
        await freezing.Should().ThrowAsync<CustomerAccountExceptions>();

        await _approvals.Received(2).EvaluateAsync(
            Arg.Any<ApprovalContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approved_limit_change_applies()
    {
        CustomerAccount account = OpenAccount();
        _accounts.FindAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _approvals.EvaluateAsync(Arg.Any<ApprovalContext>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalOutcome.NoGate);

        var setLimit = new SetCreditLimitCommandHandler(_accounts, _approvals);
        await setLimit.HandleAsync(new SetCreditLimitCommand(account.Id, 9000m, "ZAR"));

        account.CreditLimit.Amount.Should().Be(9000m);
    }

    [Fact]
    public void Hold_release_close_lifecycle()
    {
        CustomerAccount account = OpenAccount();

        account.Hold("Review.");
        account.Status.Should().Be(AccountStatus.OnHold);
        account.HoldReason.Should().Be("Review.");
        Action charging = () => account.EnsureChargeable();
        charging.Should().Throw<CustomerAccountExceptions>();

        account.Release();
        account.Status.Should().Be(AccountStatus.Active);
        account.EnsureChargeable();

        account.SetLimit(new Money(7000m, "ZAR"));
        account.CreditLimit.Amount.Should().Be(7000m);

        account.Close();
        account.Status.Should().Be(AccountStatus.Closed);
        Action holding = () => account.Hold("Late.");
        holding.Should().Throw<CustomerAccountExceptions>();
        Action releasing = () => account.Release();
        releasing.Should().Throw<CustomerAccountExceptions>();
    }

    [Fact]
    public void Holder_and_terms_guards()
    {
        Action noHolderName = () => AccountHolder.Authorise(
            TenantId, StoreId, UuidV7.NewGuid(), UuidV7.NewGuid(), "  ", new Money(100m, "ZAR"));
        noHolderName.Should().Throw<ArgumentException>();

        Action noTermsTenant = () => CustomerFinanceTerms.Seed(Guid.Empty, "ZAR");
        noTermsTenant.Should().Throw<ArgumentException>();

        CustomerFinanceTerms terms = CustomerFinanceTerms.Seed(TenantId, "ZAR");
        terms.InterestMonthlyRate.Should().Be(0.02m);
        terms.LayByAdminFee.Amount.Should().Be(100m);
        terms.StaleBalanceMinutes.Should().Be(15);
    }

    [Fact]
    public async Task Authorising_a_holder_stores_them_for_tender_checks()
    {
        CustomerAccount account = OpenAccount();
        _accounts.FindAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        var holders = Substitute.For<IAccountHolderRepository>();
        var added = new List<AccountHolder>();
        holders.When(h => h.Add(Arg.Any<AccountHolder>()))
            .Do(call => added.Add(call.Arg<AccountHolder>()));

        var handler = new AuthoriseHolderCommandHandler(
            _accounts, holders, TenantWithIds());
        Guid holderUser = UuidV7.NewGuid();

        Guid id = await handler.HandleAsync(new AuthoriseHolderCommand(
            account.Id, holderUser, "Lerato", 300m, "ZAR"));

        added.Should().ContainSingle();
        added[0].Id.Should().Be(id);
        added[0].UserId.Should().Be(holderUser);
    }

    [Fact]
    public async Task Opening_an_account_numbers_and_stores_it()
    {
        var accounts = Substitute.For<ICustomerAccountRepository>();
        var added = new List<CustomerAccount>();
        accounts.When(a => a.Add(Arg.Any<CustomerAccount>()))
            .Do(call => added.Add(call.Arg<CustomerAccount>()));
        var numbers = Substitute.For<IDocumentNumberSequence>();
        numbers.NextAsync("ACT", Arg.Any<CancellationToken>()).Returns("ACT-000001");
        var tenant = TenantWithIds();
        var company = Substitute.For<ICompanyContext>();
        company.RequireCompany().Returns(StoreId);

        var handler = new OpenCustomerAccountCommandHandler(accounts, numbers, tenant, company);

        Guid id = await handler.HandleAsync(new OpenCustomerAccountCommand(
            UuidV7.NewGuid(), 5000m, "ZAR", 30));

        added.Should().ContainSingle();
        added[0].Id.Should().Be(id);
        added[0].AccountNumber.Should().Be("ACT-000001");
        added[0].Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public async Task Payment_against_a_missing_invoice_refuses()
    {
        CustomerAccount account = OpenAccount();
        _accounts.FindAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        var ar = Substitute.For<IArInvoiceRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var handler = new RecordAccountPaymentCommandHandler(
            _accounts, ar,
            Substitute.For<IArReceiptRepository>(),
            Substitute.For<IFinancialEventPoster>(),
            Substitute.For<IDocumentNumberSequence>(),
            tenant,
            Substitute.For<IClock>());

        Func<Task> paying = () => handler.HandleAsync(new RecordAccountPaymentCommand(
            account.Id, 100m, "ZAR", "Till", "RCPT-1",
            [new PaymentAllocationInput(UuidV7.NewGuid(), 100m)]));

        await paying.Should().ThrowAsync<CustomerAccountExceptions>()
            .WithMessage("*AR invoice*");
    }

    private static CustomerAccount OpenAccount()
    {
        return CustomerAccount.Open(
            TenantId, StoreId, $"ACT-{UuidV7.NewGuid():N}", UuidV7.NewGuid(),
            new Money(5000m, "ZAR"), 30, UuidV7.NewGuid());
    }

    private static ITenantContext TenantWithIds()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        tenant.StoreId.Returns(StoreId);
        return tenant;
    }
}
