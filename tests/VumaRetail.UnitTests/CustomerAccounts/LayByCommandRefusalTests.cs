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

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// Refusal paths: offline completion, company mismatch, short stock, missing configuration —
/// every guard a handler owns is executed here, not just documented.
/// </summary>
public sealed class LayByCommandRefusalTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly ILayByAgreementRepository _laybys = Substitute.For<ILayByAgreementRepository>();
    private readonly ICustomerFinanceTermsRepository _terms = Substitute.For<ICustomerFinanceTermsRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ICompanyContext _company = Substitute.For<ICompanyContext>();

    public LayByCommandRefusalTests()
    {
        _clock.UtcNow.Returns(Now);
        _company.CompanyId.Returns((Guid?)CompanyId);
        _company.RequireCompany().Returns(CompanyId);
        _terms.FindAsync(Arg.Any<CancellationToken>())
            .Returns(CustomerFinanceTerms.Seed(TenantId, "ZAR"));
    }

    [Fact]
    public async Task Offline_completion_refuses_before_touching_anything()
    {
        var handler = new CompleteLayByAgreementCommandHandler(
            _laybys,
            Substitute.For<IStockReservationRepository>(),
            Substitute.For<IReservationService>(),
            Substitute.For<IFinancialEventPoster>(),
            Substitute.For<ITenantContext>(),
            _company,
            _clock);

        Func<Task> completing = () => handler.HandleAsync(
            new CompleteLayByAgreementCommand(UuidV7.NewGuid(), CapturedOffline: true));

        await completing.Should().ThrowAsync<LayByExceptions>()
            .WithMessage("*connectivity*");
    }

    [Fact]
    public async Task Company_mismatch_refuses_loudly()
    {
        var other = Substitute.For<ICompanyContext>();
        other.CompanyId.Returns(UuidV7.NewGuid());
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        tenant.StoreId.Returns(StoreId);
        var handler = new OpenLayByAgreementCommandHandler(
            _laybys, _terms,
            Substitute.For<IDocumentNumberSequence>(),
            Catalog(), Prices(), Tax(), Packs(),
            Locations(),
            Substitute.For<IReservationService>(),
            Substitute.For<IFinancialEventPoster>(),
            tenant,
            other,
            _clock);

        Func<Task> opening = () => handler.HandleAsync(new OpenLayByAgreementCommand(
            UuidV7.NewGuid(), "ZAR",
            [new LayByLineInput(UuidV7.NewGuid(), null, 1m, "EA")],
            50m, "Till", 3, "LAYBY", CompanyId));

        await opening.Should().ThrowAsync<LayByExceptions>()
            .WithMessage("*different company*");
    }

    [Fact]
    public async Task Short_stock_refuses_the_agreement()
    {
        var laybys = Substitute.For<ILayByAgreementRepository>();
        var numbers = Substitute.For<IDocumentNumberSequence>();
        numbers.NextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("LAY-000001");
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        tenant.StoreId.Returns(StoreId);
        var reservations = Substitute.For<IReservationService>();
        reservations.ReserveAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Quantity>(),
                Arg.Any<ReservationSource>(), Arg.Any<Guid>(), Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ReserveOutcome(
                UuidV7.NewGuid(), new Quantity(0m, "EA"), new Quantity(1m, "EA"),
                new Quantity(0m, "EA"), Now));

        var handler = new OpenLayByAgreementCommandHandler(
            laybys, _terms, numbers, Catalog(), Prices(), Tax(), Packs(),
            Locations(), reservations, Substitute.For<IFinancialEventPoster>(),
            tenant, _company, _clock);

        Func<Task> opening = () => handler.HandleAsync(new OpenLayByAgreementCommand(
            UuidV7.NewGuid(), "ZAR",
            [new LayByLineInput(UuidV7.NewGuid(), null, 1m, "EA")],
            50m, "Till", 3, "LAYBY"));

        await opening.Should().ThrowAsync<LayByExceptions>()
            .WithMessage("*shelf*");
    }

    [Fact]
    public async Task Missing_terms_and_unknown_location_refuse_with_codes()
    {
        var noTerms = Substitute.For<ICustomerFinanceTermsRepository>();
        var handler = new OpenLayByAgreementCommandHandler(
            _laybys, noTerms,
            Substitute.For<IDocumentNumberSequence>(),
            Substitute.For<ISellableItemResolver>(),
            Substitute.For<IPriceResolver>(),
            Substitute.For<ITaxCalculator>(),
            Substitute.For<IPackSizeResolver>(),
            Substitute.For<IStockLocationRepository>(),
            Substitute.For<IReservationService>(),
            Substitute.For<IFinancialEventPoster>(),
            Substitute.For<ITenantContext>(),
            _company,
            _clock);

        Func<Task> opening = () => handler.HandleAsync(new OpenLayByAgreementCommand(
            UuidV7.NewGuid(), "ZAR",
            [new LayByLineInput(UuidV7.NewGuid(), null, 1m, "EA")],
            50m, "Till", 3, "LAYBY"));

        await opening.Should().ThrowAsync<LayByExceptions>()
            .WithMessage("*terms*");
    }

    [Fact]
    public async Task Overlong_term_unknown_location_and_missing_company_refuse()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        tenant.StoreId.Returns(StoreId);

        var tooLong = new OpenLayByAgreementCommandHandler(
            _laybys, _terms,
            Substitute.For<IDocumentNumberSequence>(),
            Catalog(), Prices(), Tax(), Packs(),
            Locations(),
            Substitute.For<IReservationService>(),
            Substitute.For<IFinancialEventPoster>(),
            tenant, _company, _clock);

        Func<Task> longTerm = () => tooLong.HandleAsync(new OpenLayByAgreementCommand(
            UuidV7.NewGuid(), "ZAR",
            [new LayByLineInput(UuidV7.NewGuid(), null, 1m, "EA")],
            50m, "Till", 99, "LAYBY"));
        await longTerm.Should().ThrowAsync<LayByExceptions>()
            .WithMessage("*6 months*");

        var unbound = Substitute.For<ICompanyContext>();
        var noCompany = new OpenLayByAgreementCommandHandler(
            _laybys, _terms,
            Substitute.For<IDocumentNumberSequence>(),
            Catalog(), Prices(), Tax(), Packs(),
            Locations(),
            Substitute.For<IReservationService>(),
            Substitute.For<IFinancialEventPoster>(),
            tenant, unbound, _clock);

        Func<Task> companyLess = () => noCompany.HandleAsync(new OpenLayByAgreementCommand(
            UuidV7.NewGuid(), "ZAR",
            [new LayByLineInput(UuidV7.NewGuid(), null, 1m, "EA")],
            50m, "Till", 3, "LAYBY"));
        await companyLess.Should().ThrowAsync<LayByExceptions>()
            .WithMessage("*company is required*");
    }

    private static ISellableItemResolver Catalog()
    {
        var catalog = Substitute.For<ISellableItemResolver>();
        catalog.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new SellableItem(UuidV7.NewGuid(), null, "Milk", "EA", "STANDARD"));
        return catalog;
    }

    private static IPriceResolver Prices()
    {
        var prices = Substitute.For<IPriceResolver>();
        prices.ResolveAsync(Arg.Any<PriceResolutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<PriceResolutionRequest>();
                var net = new Money(100m * request.Quantity, "ZAR");
                return new PriceResolution(
                    null, null, null, false, new Money(100m, "ZAR"), net,
                    new Money(0m, "ZAR"), net, [], "test");
            });
        return prices;
    }

    private static ITaxCalculator Tax()
    {
        var tax = Substitute.For<ITaxCalculator>();
        tax.CalculateAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var net = call.Arg<Money>();
                var taxAmount = new Money(net.Amount * 0.15m, net.Currency);
                return new TaxCalculation("STANDARD", net, taxAmount, net + taxAmount, 0.15m);
            });
        return tax;
    }

    private static IPackSizeResolver Packs()
    {
        var packs = Substitute.For<IPackSizeResolver>();
        packs.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new PackSizeSnapshot("Each"));
        return packs;
    }

    private static IStockLocationRepository Locations()
    {
        var locations = Substitute.For<IStockLocationRepository>();
        locations.FindByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(StockLocation.Create(TenantId, StoreId, "LAYBY", "Lay-by", StockLocationType.Other));
        return locations;
    }
}
