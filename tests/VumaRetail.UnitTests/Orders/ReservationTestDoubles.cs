using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Orders;

/// <summary>
/// Test doubles for Stage 08c's reservation ledger: the echo hold grants whatever is demanded,
/// which keeps pre-rework allocation tests' expectations (they stub availability, not holds).
/// Tests that prove shortfall behaviour configure their own <see cref="ReserveOutcome"/>.
/// Scopes come bound to a fixed company, mirroring what the handlers' child scopes provide.
/// </summary>
internal static class ReservationTestDoubles
{
    /// <summary>A reservation service that holds everything demanded, with a fresh hold id per call.</summary>
    internal static IReservationService EchoHold()
    {
        IReservationService reservations = Substitute.For<IReservationService>();

        reservations
            .ReserveAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Quantity>(),
                Arg.Any<ReservationSource>(), Arg.Any<Guid>(), Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Quantity demanded = call.ArgAt<Quantity>(3);
                return new ReserveOutcome(
                    Guid.NewGuid(),
                    demanded,
                    Quantity.Zero(demanded.UnitOfMeasure),
                    Quantity.Zero(demanded.UnitOfMeasure),
                    DateTimeOffset.UtcNow);
            });

        return reservations;
    }

    /// <summary>
    /// A scope factory whose scopes resolve the echo hold plus bound tenant/company contexts —
    /// what handlers see after binding the acting company.
    /// </summary>
    internal static IServiceScopeFactory BoundScopes(IReservationService reservations)
    {
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IReservationService)).Returns(reservations);
        services.GetService(typeof(ITenantContext)).Returns(Substitute.For<ITenantContext>());
        services.GetService(typeof(ICompanyContext)).Returns(Substitute.For<ICompanyContext>());

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(services);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        return factory;
    }

    /// <summary>A company directory resolving every lookup to one fixed company.</summary>
    internal static ICompanyDirectory SingleCompany(Guid companyId)
    {
        var directory = Substitute.For<ICompanyDirectory>();
        directory.RequireSingleActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(companyId);
        return directory;
    }
}
