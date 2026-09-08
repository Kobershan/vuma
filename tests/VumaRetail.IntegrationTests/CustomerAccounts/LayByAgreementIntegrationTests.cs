using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.CustomerAccounts.Commands.LayBy;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.CustomerAccounts;

/// <summary>
/// Stage 10b lay-by against real PostgreSQL: frozen totals across a reprice, exactly-once
/// completion economics, cancellation accounting, and offline capture flags.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LayByAgreementIntegrationTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Full_lifecycle_posts_once_and_consumes_the_holds()
    {
        await using var scope = await LayByScope.CreateAsync(fixture);
        // 2 × R100 net plus R30 stored tax: agreed R230. Deposit R100, instalment R130.
        Guid agreementId = await scope.OpenAsync(quantity: 2m, deposit: 100m);
        await scope.PayAsync(agreementId, 130m, "RCPT-LAY-2");

        await scope.CompleteAsync(agreementId);

        LayByAgreement? agreement = await scope.Laybys.FindAsync(agreementId);
        agreement.Should().NotBeNull();
        agreement!.Status.Should().Be(LayByStatus.Completed);
        agreement.PaidToDate.Amount.Should().Be(230m);

        IReadOnlyList<IFinancialEvent> completions = scope.Posted("layby.completed");
        completions.Should().ContainSingle();
        completions[0].Amounts["Principal"].Amount.Should().Be(230m);
        completions[0].Amounts["Net"].Amount.Should().Be(200m);
        completions[0].Amounts["Tax"].Amount.Should().Be(30m);

        scope.Posted("layby.deposit.received").Should().ContainSingle();
        scope.Posted("layby.instalment.received").Should().ContainSingle();

        await scope.Reservations.Received(1).ConsumeAsync(
            scope.HoldChainId, agreementId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_keeps_the_fee_and_releases_the_holds()
    {
        await using var scope = await LayByScope.CreateAsync(fixture);
        Guid agreementId = await scope.OpenAsync(quantity: 2m, deposit: 100m);

        await scope.CancelAsync(agreementId);

        LayByAgreement? agreement = await scope.Laybys.FindAsync(agreementId);
        agreement!.Status.Should().Be(LayByStatus.Cancelled);
        agreement.CancelRefund!.Value.Amount.Should().Be(0m);
        agreement.CancelFee!.Value.Amount.Should().Be(100m);

        IReadOnlyList<IFinancialEvent> cancelled = scope.Posted("layby.cancelled");
        cancelled.Should().ContainSingle();
        cancelled[0].Amounts["Refund"].Amount.Should().Be(0m);
        cancelled[0].Amounts["Fee"].Amount.Should().Be(100m);

        await scope.Reservations.Received(1).ReleaseAsync(
            scope.HoldChainId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agreed_total_survives_a_shelf_price_change_mid_term()
    {
        await using var scope = await LayByScope.CreateAsync(fixture);
        Guid agreementId = await scope.OpenAsync(quantity: 2m, deposit: 100m);

        scope.Reprice(unitPrice: 120m, taxPerUnit: 18m);
        await scope.PayAsync(agreementId, 130m, "RCPT-LAY-2");
        await scope.CompleteAsync(agreementId);

        IReadOnlyList<IFinancialEvent> completions = scope.Posted("layby.completed");
        completions.Should().ContainSingle();
        completions[0].Amounts["Principal"].Amount.Should().Be(230m);
    }

    [Fact]
    public async Task Offline_instalment_is_flagged_on_its_row()
    {
        await using var scope = await LayByScope.CreateAsync(fixture);
        Guid agreementId = await scope.OpenAsync(quantity: 2m, deposit: 100m);

        await scope.PayAsync(agreementId, 50m, "RCPT-LAY-OFF-3", takenOffline: true);

        LayByAgreement? agreement = await scope.Laybys.FindAsync(agreementId);
        agreement!.Instalments.Should().HaveCount(2);
        agreement.Instalments[1].TakenOffline.Should().BeTrue();
        agreement.Instalments[0].TakenOffline.Should().BeFalse();
    }

    private sealed class LayByScope : IAsyncDisposable
    {
        private readonly VumaRetailDbContext _context;
        private readonly TestClock _clock = new(Now);
        private readonly Guid _tenantId;
        private readonly Guid _storeId;
        private readonly Guid _companyId = UuidV7.NewGuid();
        private readonly Guid _partnerId = UuidV7.NewGuid();
        private readonly Guid _itemId = UuidV7.NewGuid();
        private readonly Guid _locationId = UuidV7.NewGuid();
        private readonly List<IFinancialEvent> _posted = [];

        private decimal _unitPrice = 100m;
        private decimal _taxPerUnit = 15m;

        private LayByScope(VumaRetailDbContext context, Guid tenantId, Guid storeId)
        {
            _context = context;
            _tenantId = tenantId;
            _storeId = storeId;
            Laybys = new LayByAgreementRepository(context);
            Terms = new CustomerFinanceTermsRepository(context);
        }

        public LayByAgreementRepository Laybys { get; }
        public CustomerFinanceTermsRepository Terms { get; }
        public IReservationService Reservations { get; private set; } = null!;
        public Guid HoldChainId { get; private set; }
        private TestTenantContext Tenant { get; set; } = null!;

        public static async Task<LayByScope> CreateAsync(PostgresFixture fixture)
        {
            string connectionString = await fixture.CreateDatabaseAsync().ConfigureAwait(false);
            var clock = new TestClock(Now);
            var tenant = TestTenantContext.Unfiltered();
            var principal = new TestPrincipalAccessor("user:layby");
            Guid tenantId = UuidV7.NewGuid();
            Guid storeId = UuidV7.NewGuid();
            tenant.SetTenant(tenantId, storeId);
            tenant.EndBypass();
            var context = TestDbContextFactory.For(connectionString, clock, principal, tenant);
            var scope = new LayByScope(context, tenantId, storeId);
            scope.Tenant = tenant;

            context.CustomerFinanceTerms.Add(CustomerFinanceTerms.Seed(scope._tenantId, "ZAR"));
            await context.CommitAsync().ConfigureAwait(false);

            var company = Substitute.For<ICompanyContext>();
            company.RequireCompany().Returns(scope._companyId);

            var numbers = Substitute.For<IDocumentNumberSequence>();
            numbers.NextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => $"{call.Arg<string>()}-000001");

            var catalog = Substitute.For<ISellableItemResolver>();
            catalog.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(new SellableItem(scope._itemId, null, "Milk 2L", "EA", "STANDARD"));

            var prices = Substitute.For<IPriceResolver>();
            prices.ResolveAsync(Arg.Any<PriceResolutionRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var request = call.Arg<PriceResolutionRequest>();
                    var net = new Money(scope._unitPrice * request.Quantity, "ZAR");
                    return new PriceResolution(
                        null, null, null, false,
                        new Money(scope._unitPrice, "ZAR"), net,
                        new Money(0m, "ZAR"), net,
                        [], "test price");
                });

            var tax = Substitute.For<ITaxCalculator>();
            tax.CalculateAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var net = call.Arg<Money>();
                    decimal rate = net.Amount == 0m ? 0m : scope._taxPerUnit / scope._unitPrice;
                    var taxAmount = new Money(net.Amount * rate, net.Currency);
                    return new TaxCalculation("STANDARD", net, taxAmount, net + taxAmount, rate);
                });

            var packs = Substitute.For<IPackSizeResolver>();
            packs.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
                .Returns(new PackSizeSnapshot("Each"));

            var location = StockLocation.Create(scope._tenantId, scope._storeId, "LAYBY", "Lay-by holding", StockLocationType.Other);
            var locations = Substitute.For<IStockLocationRepository>();
            locations.FindByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(location);

            var reservations = Substitute.For<IReservationService>();
            reservations.ReserveAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Quantity>(),
                    Arg.Any<ReservationSource>(), Arg.Any<Guid>(), Arg.Any<string?>(),
                    Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                    Arg.Any<CancellationToken>())
                .Returns(new ReserveOutcome(
                    UuidV7.NewGuid(), new Quantity(2m, "EA"), new Quantity(0m, "EA"),
                    new Quantity(0m, "EA"), Now));
            var events = Substitute.For<IFinancialEventPoster>();
            events.PostAsync(Arg.Do<IFinancialEvent>(e => scope._posted.Add(e)), Arg.Any<CancellationToken>())
                .Returns(UuidV7.NewGuid());

            scope.Reservations = reservations;
            scope.Handlers = new HandlerSet(
                new OpenLayByAgreementCommandHandler(
                    scope.Laybys, scope.Terms, numbers, catalog, prices, tax, packs,
                    locations, reservations, events, tenant, company, clock),
                new RecordLayByInstalmentCommandHandler(
                    scope.Laybys, events, tenant, clock));
            scope.HoldRowFactory = agreementId => StockReservation.Hold(
                scope._tenantId, scope._storeId, scope._companyId, scope._locationId,
                scope._itemId, null, new Quantity(2m, "EA"),
                ReservationSource.LayBy, agreementId, "LAY-1");
            scope.Events = events;
            return scope;
        }

        public HandlerSet Handlers { get; private set; } = null!;
        public IFinancialEventPoster Events { get; private set; } = null!;
        public Func<Guid, StockReservation> HoldRowFactory { get; private set; } = null!;

        public void Reprice(decimal unitPrice, decimal taxPerUnit)
        {
            _unitPrice = unitPrice;
            _taxPerUnit = taxPerUnit;
        }

        public async Task<Guid> OpenAsync(decimal quantity, decimal deposit)
        {
            Guid id = await Handlers.Open.HandleAsync(
                new OpenLayByAgreementCommand(
                    _partnerId, "ZAR",
                    [new LayByLineInput(_itemId, null, quantity, "EA")],
                    deposit, "Till", 3, "LAYBY", _companyId));
            await _context.CommitAsync().ConfigureAwait(false);
            return id;
        }

        public async Task PayAsync(Guid agreementId, decimal amount, string receipt, bool takenOffline = false)
        {
            await Handlers.Pay.HandleAsync(
                new RecordLayByInstalmentCommand(agreementId, amount, "ZAR", "Till", receipt, takenOffline));
            await _context.CommitAsync().ConfigureAwait(false);
        }

        public async Task CompleteAsync(Guid agreementId)
        {
            var hold = HoldRowFactory(agreementId);
            var holds = Substitute.For<IStockReservationRepository>();
            holds.ListOpenByGroupRefAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<StockReservation> { hold });
            HoldChainId = hold.ReservationId;

            var complete = new CompleteLayByAgreementCommandHandler(
                Laybys, holds, Reservations, Events, Tenant, TestCompany(), _clock);
            await complete.HandleAsync(new CompleteLayByAgreementCommand(agreementId));
            await _context.CommitAsync().ConfigureAwait(false);
        }

        public async Task CancelAsync(Guid agreementId)
        {
            var hold = HoldRowFactory(agreementId);
            var holds = Substitute.For<IStockReservationRepository>();
            holds.ListOpenByGroupRefAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new List<StockReservation> { hold });
            HoldChainId = hold.ReservationId;

            var cancel = new CancelLayByAgreementCommandHandler(
                Laybys, holds, Reservations, Events, Tenant, TestCompany(), _clock);
            await cancel.HandleAsync(new CancelLayByAgreementCommand(agreementId));
            await _context.CommitAsync().ConfigureAwait(false);
        }

        public IReadOnlyList<IFinancialEvent> Posted(string eventType)
            => _posted.Where(e => e.EventType == eventType).ToList();

        private static ICompanyContext TestCompany()
        {
            var company = Substitute.For<ICompanyContext>();
            company.CompanyId.Returns((Guid?)null);
            return company;
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        public sealed record HandlerSet(
            OpenLayByAgreementCommandHandler Open,
            RecordLayByInstalmentCommandHandler Pay);
    }
}
