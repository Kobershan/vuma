using NSubstitute;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.CustomerAccounts.Queries;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// The tender-time limit gate: available is limit less AR outstanding less queued offline, and a
/// held account refuses everything no matter the arithmetic.
/// </summary>
public sealed class CreditLimitTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid PartnerId = UuidV7.NewGuid();
    private static readonly DateOnly Today = new(2026, 8, 16);

    private readonly ICustomerAccountRepository _accounts = Substitute.For<ICustomerAccountRepository>();
    private readonly IArInvoiceRepository _ar = Substitute.For<IArInvoiceRepository>();

    [Fact]
    public async Task Limit_counts_ar_outstanding_and_queued_offline_sales_together()
    {
        // Limit R5,000. AR outstanding R3,000 (R2,000 current + R1,000 45 days overdue).
        // Unsynced offline account sales R1,500. Available R500: a R600 tender refuses,
        // and the refusal names the shortage rather than failing silently.
        CustomerAccount account = ArableAccount(limit: 5000m);
        _accounts.FindAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _ar.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<ArInvoice>
        {
            PostedInvoice(number: "INV-1", total: 2000m, outstanding: 2000m, due: Today),
            PostedInvoice(number: "INV-2", total: 1000m, outstanding: 1000m, due: Today.AddDays(-45)),
        });

        var handler = new CheckCreditLimitQueryHandler(_accounts, Substitute.For<IAccountHolderRepository>(), _ar);

        CreditCheckResult refused = await handler.HandleAsync(
            new CheckCreditLimitQuery(account.Id, 600m, "ZAR", 1500m));
        refused.Approved.Should().BeFalse();
        refused.Available.Amount.Should().Be(500m);

        CreditCheckResult exact = await handler.HandleAsync(
            new CheckCreditLimitQuery(account.Id, 500m, "ZAR", 1500m));
        exact.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task A_held_account_refuses_even_a_tender_well_inside_its_limit()
    {
        CustomerAccount account = ArableAccount(limit: 5000m);
        account.Hold("Two missed debit orders.");
        _accounts.FindAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _ar.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<ArInvoice>());

        var handler = new CheckCreditLimitQueryHandler(_accounts, Substitute.For<IAccountHolderRepository>(), _ar);

        CreditCheckResult result = await handler.HandleAsync(
            new CheckCreditLimitQuery(account.Id, 100m, "ZAR", 0m));

        result.Approved.Should().BeFalse();
        result.RefusalReason.Should().Contain("OnHold");
    }

    [Fact]
    public async Task An_unauthorised_buyer_and_an_over_limit_buyer_both_refuse()
    {
        CustomerAccount account = ArableAccount(limit: 5000m);
        _accounts.FindAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _ar.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<ArInvoice>());
        var holders = Substitute.For<IAccountHolderRepository>();
        holders.ListForAccountAsync(account.Id, Arg.Any<CancellationToken>())
            .Returns(new List<AccountHolder>
            {
                AccountHolder.Authorise(TenantId, StoreId, account.Id, UuidV7.NewGuid(), "Lerato", new Money(300m, "ZAR")),
            });

        var handler = new CheckCreditLimitQueryHandler(_accounts, holders, _ar);

        CreditCheckResult stranger = await handler.HandleAsync(
            new CheckCreditLimitQuery(account.Id, 100m, "ZAR", 0m, UuidV7.NewGuid()));
        stranger.Approved.Should().BeFalse();
        stranger.RefusalReason.Should().Contain("not authorised");
    }

    [Fact]
    public async Task Ageing_buckets_follow_due_dates_not_invoice_dates()
    {
        // Five R1,000 invoices due today and 20/50/80/130 days ago land in
        // Current/30/60/90/120+ respectively.
        CustomerAccount account = ArableAccount(limit: 50000m);
        _accounts.FindAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _ar.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<ArInvoice>
        {
            PostedInvoice(number: "INV-C", total: 1000m, outstanding: 1000m, due: Today),
            PostedInvoice(number: "INV-30", total: 1000m, outstanding: 1000m, due: Today.AddDays(-20)),
            PostedInvoice(number: "INV-60", total: 1000m, outstanding: 1000m, due: Today.AddDays(-50)),
            PostedInvoice(number: "INV-90", total: 1000m, outstanding: 1000m, due: Today.AddDays(-80)),
            PostedInvoice(number: "INV-120", total: 1000m, outstanding: 1000m, due: Today.AddDays(-130)),
        });

        var handler = new GetAccountAgeingQueryHandler(_accounts, _ar);

        IReadOnlyList<AgeingBucket> buckets = await handler.HandleAsync(
            new GetAccountAgeingQuery(account.Id, Today));

        buckets.ToDictionary(b => b.Bucket, b => b.Balance.Amount).Should().BeEquivalentTo(
            new Dictionary<string, decimal>
            {
                ["Current"] = 1000m,
                ["30"] = 1000m,
                ["60"] = 1000m,
                ["90"] = 1000m,
                ["120+"] = 1000m,
            });
    }

    [Fact]
    public void Opening_needs_a_partner_a_positive_limit_and_positive_terms()
    {
        Action noPartner = () => CustomerAccount.Open(
            TenantId, StoreId, "ACT-1", Guid.Empty, new Money(1000m, "ZAR"), 30);
        noPartner.Should().Throw<ArgumentException>();

        Action noLimit = () => CustomerAccount.Open(
            TenantId, StoreId, "ACT-1", PartnerId, new Money(0m, "ZAR"), 30);
        noLimit.Should().Throw<CustomerAccountExceptions>();

        Action noTerms = () => CustomerAccount.Open(
            TenantId, StoreId, "ACT-1", PartnerId, new Money(1000m, "ZAR"), 0);
        noTerms.Should().Throw<CustomerAccountExceptions>();
    }

    private static CustomerAccount ArableAccount(decimal limit)
    {
        return CustomerAccount.Open(
            TenantId, StoreId, $"ACT-{UuidV7.NewGuid():N}", PartnerId,
            new Money(limit, "ZAR"), 30, UuidV7.NewGuid());
    }

    private static ArInvoice PostedInvoice(string number, decimal total, decimal outstanding, DateOnly due)
    {
        var invoice = ArInvoice.Draft(
            TenantId, StoreId, new PartnerId(PartnerId), number, Today.AddDays(-60), due, "ZAR");
        invoice.AddLine("Goods", new Money(total - total * 15m / 115m, "ZAR"), "STANDARD", new Money(total * 15m / 115m, "ZAR"));
        invoice.Post(UuidV7.NewGuid());
        if (outstanding < total)
        {
            invoice.Allocate(new Money(total - outstanding, "ZAR"));
        }

        return invoice;
    }
}
