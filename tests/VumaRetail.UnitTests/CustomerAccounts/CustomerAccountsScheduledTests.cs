using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.CustomerAccounts.Commands.Scheduled;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// The scheduled runs: expiry frees stock and reminds before the date, interest accrues monthly
/// on overdue balances, and both are safe to run with nothing configured.
/// </summary>
public sealed class CustomerAccountsScheduledTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Now);

    [Fact]
    public async Task Expiry_service_expires_and_reminds()
    {
        LayByAgreement pastDue = Agreement(expiry: Now.AddDays(-1), paid: 200m);
        LayByAgreement weekOut = Agreement(expiry: Now.AddDays(7), paid: 200m);
        var laybys = Substitute.For<ILayByAgreementRepository>();
        laybys.ListExpiringAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => ((List<LayByAgreement>)[pastDue, weekOut])
                .Where(a => a.ExpiryDate <= call.Arg<DateTimeOffset>()).ToList());
        var holds = Substitute.For<IStockReservationRepository>();
        holds.ListOpenByGroupRefAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<StockReservation>());
        var reservations = Substitute.For<IReservationService>();
        var notifications = Substitute.For<INotificationDispatcher>();

        var handler = new ExpireLayByAgreementsCommandHandler(
            laybys, holds, reservations, notifications, _clock);

        ExpiryOutcome outcome = await handler.HandleAsync(new ExpireLayByAgreementsCommand());

        outcome.Expired.Should().Be(1);
        outcome.Reminded.Should().Be(1);
        pastDue.Status.Should().Be(LayByStatus.Expired);
        await notifications.Received(1).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r.Category == "customer-accounts.layby.expiring"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Interest_accrues_monthly_on_overdue()
    {
        var terms = Substitute.For<ICustomerFinanceTermsRepository>();
        terms.FindAsync(Arg.Any<CancellationToken>())
            .Returns(CustomerFinanceTerms.Seed(TenantId, "ZAR"));
        var ar = Substitute.For<IArInvoiceRepository>();
        ar.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<ArInvoice>
        {
            OverdueInvoice(outstanding: 1000m),
        });
        var added = new List<ArInvoice>();
        ar.When(r => r.Add(Arg.Any<ArInvoice>())).Do(call => added.Add(call.Arg<ArInvoice>()));
        var events = Substitute.For<IFinancialEventPoster>();
        var raised = new List<IFinancialEvent>();
        events.PostAsync(Arg.Do<IFinancialEvent>(e => raised.Add(e)), Arg.Any<CancellationToken>())
            .Returns(UuidV7.NewGuid());
        var numbers = Substitute.For<IDocumentNumberSequence>();
        numbers.NextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("ARINV-000001");
        var notifications = Substitute.For<INotificationDispatcher>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        tenant.StoreId.Returns(StoreId);

        var handler = new AccrueAccountInterestCommandHandler(
            ar, terms, events, numbers, notifications, tenant, _clock);

        int count = await handler.HandleAsync(new AccrueAccountInterestCommand());

        count.Should().Be(1);
        added.Should().ContainSingle();
        raised.Should().ContainSingle();
        raised[0].EventType.Should().Be("account.interest.raised");
        raised[0].Amounts["Interest"].Amount.Should().Be(20.00m);
        await notifications.Received(1).NotifyAsync(
            Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Interest_run_with_no_terms_raises_nothing()
    {
        var terms = Substitute.For<ICustomerFinanceTermsRepository>();
        var ar = Substitute.For<IArInvoiceRepository>();
        var events = Substitute.For<IFinancialEventPoster>();
        var tenant = Substitute.For<ITenantContext>();

        var handler = new AccrueAccountInterestCommandHandler(
            ar, terms, events,
            Substitute.For<IDocumentNumberSequence>(),
            Substitute.For<INotificationDispatcher>(),
            tenant, _clock);

        int count = await handler.HandleAsync(new AccrueAccountInterestCommand());

        count.Should().Be(0);
        await events.DidNotReceive().PostAsync(
            Arg.Any<IFinancialEvent>(), Arg.Any<CancellationToken>());
    }

    private static LayByAgreement Agreement(DateTimeOffset expiry, decimal paid)
    {
        var agreement = LayByAgreement.Open(
            TenantId, StoreId, $"LAY-{UuidV7.NewGuid():N}", UuidV7.NewGuid(),
            new Money(1200m, "ZAR"), new Money(200m, "ZAR"), 3,
            expiry, new Money(100m, "ZAR"), UuidV7.NewGuid());
        agreement.AddLine(LayByAgreementLine.Create(
            TenantId, StoreId, agreement.Id, UuidV7.NewGuid(), null,
            1m, "EA", new Money(100m, "ZAR"), new Money(0m, "ZAR"),
            new Money(15m, "ZAR"), "Each", "ZAR", null));
        agreement.Activate();
        agreement.AddInstalment(LayByInstalment.Record(
            TenantId, StoreId, agreement.Id, 1, new Money(200m, "ZAR"), "RCPT-1", Now, "Till"));
        if (paid > 200m)
        {
            agreement.AddInstalment(LayByInstalment.Record(
                TenantId, StoreId, agreement.Id, 2, new Money(paid - 200m, "ZAR"), "RCPT-2", Now, "Till"));
        }

        return agreement;
    }

    private static ArInvoice OverdueInvoice(decimal outstanding)
    {
        var invoice = ArInvoice.Draft(
            TenantId, StoreId, new PartnerId(UuidV7.NewGuid()), "INV-OD-1",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "ZAR");
        invoice.AddLine("Goods", new Money(outstanding, "ZAR"), string.Empty, Money.Zero("ZAR"));
        invoice.Post(UuidV7.NewGuid());
        return invoice;
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
