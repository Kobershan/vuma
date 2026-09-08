using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.CustomerAccounts.Commands.Accounts;
using VumaRetail.Application.CustomerAccounts.Queries;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.CustomerAccounts;

/// <summary>
/// Stage 10b credit against real PostgreSQL: the tender gate reads posted AR rows, and a recorded
/// payment moves the outstanding the next check sees.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CreditCheckIntegrationTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 16);

    [Fact]
    public async Task Tender_gate_reads_posted_ar_and_a_payment_moves_it()
    {
        string connectionString = await fixture.CreateDatabaseAsync().ConfigureAwait(false);
        var clock = new TestClock(Now);
        var tenant = TestTenantContext.Unfiltered();
        var principal = new TestPrincipalAccessor("user:accounts");

        Guid tenantId = UuidV7.NewGuid();
        Guid storeId = UuidV7.NewGuid();
        Guid partnerId = UuidV7.NewGuid();
        tenant.SetTenant(tenantId, storeId);
        tenant.EndBypass();

        await using var context = TestDbContextFactory.For(connectionString, clock, principal, tenant);

        var accounts = new CustomerAccountRepository(context);
        var arInvoices = new ArInvoiceRepository(context);
        var receipts = new ArReceiptRepository(context);
        var events = Substitute.For<IFinancialEventPoster>();
        events.PostAsync(Arg.Any<IFinancialEvent>(), Arg.Any<CancellationToken>())
            .Returns(UuidV7.NewGuid());
        var numbers = Substitute.For<IDocumentNumberSequence>();
        numbers.NextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => $"{call.Arg<string>()}-000001");

        var account = CustomerAccount.Open(
            tenantId, storeId, "ACT-000001", partnerId, new Money(5000m, "ZAR"), 30, UuidV7.NewGuid());
        accounts.Add(account);
        await context.CommitAsync().ConfigureAwait(false);

        // R2,000 current + R1,000 45 days overdue: R3,000 owed before any payment.
        PostInvoice(context, tenantId, storeId, partnerId, "INV-1", 2000m, Today);
        Guid inv2 = PostInvoice(context, tenantId, storeId, partnerId, "INV-2", 1000m, Today.AddDays(-45));
        await context.CommitAsync().ConfigureAwait(false);

        var check = new CheckCreditLimitQueryHandler(
            accounts, Substitute.For<IAccountHolderRepository>(), arInvoices);

        CreditCheckResult before = await check.HandleAsync(
            new CheckCreditLimitQuery(account.Id, 2500m, "ZAR", 1500m));
        // 5000 − 3000 − 1500 = 500 available: R2,500 refuses.
        before.Approved.Should().BeFalse();
        before.Available.Amount.Should().Be(500m);

        // A R1,000 payment against INV-2 leaves R2,000 owed: R2,500 still refuses, R1,500 passes.
        var pay = new RecordAccountPaymentCommandHandler(
            accounts, arInvoices, receipts, events, numbers,
            new TestTenantRef(tenantId, storeId), clock);
        await pay.HandleAsync(new RecordAccountPaymentCommand(
            account.Id, 1000m, "ZAR", "EFT", "EFT-2026-0001",
            [new PaymentAllocationInput(inv2, 1000m)]));
        await context.CommitAsync().ConfigureAwait(false);

        CreditCheckResult afterRefused = await check.HandleAsync(
            new CheckCreditLimitQuery(account.Id, 2500m, "ZAR", 1500m));
        afterRefused.Approved.Should().BeFalse();

        CreditCheckResult afterApproved = await check.HandleAsync(
            new CheckCreditLimitQuery(account.Id, 1500m, "ZAR", 1500m));
        afterApproved.Approved.Should().BeTrue();
        afterApproved.Available.Amount.Should().Be(1500m);

        ArReceipt? receipt = await context.ArReceipts
            .FirstOrDefaultAsync(r => r.ReceiptNumber.StartsWith("ARREC-"));
        receipt.Should().NotBeNull();
    }

    private static Guid PostInvoice(
        VumaRetailDbContext context,
        Guid tenantId,
        Guid? storeId,
        Guid partnerId,
        string number,
        decimal total,
        DateOnly due)
    {
        var invoice = ArInvoice.Draft(
            tenantId, storeId, new PartnerId(partnerId), number, Today.AddDays(-60), due, "ZAR");
        decimal tax = total * 15m / 115m;
        invoice.AddLine("Goods", new Money(total - tax, "ZAR"), "STANDARD", new Money(tax, "ZAR"));
        invoice.Post(UuidV7.NewGuid());
        context.ArInvoices.Add(invoice);
        return invoice.Id;
    }

    private sealed class TestTenantRef(Guid tenantId, Guid? storeId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public Guid? StoreId => storeId;
        public bool IsFilterBypassed => false;
        public void SetTenant(Guid tenantId, Guid? storeId = null) { }
        public IDisposable BypassTenantFilter(string reason) => new NoOp();
        private sealed class NoOp : IDisposable
        {
            public void Dispose() { }
        }
    }
}
