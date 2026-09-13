using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Quality;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Quality;

namespace VumaRetail.UnitTests.Quality;

public sealed class QualityHoldTests
{
    [Fact]
    public void A_hold_can_only_be_disposed_once()
    {
        QualityHold hold = QualityHold.Place(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), null, new Quantity(20m, "EA"), "inspection", DateTimeOffset.UtcNow, Guid.NewGuid());

        hold.Release(DateTimeOffset.UtcNow, "passed inspection");

        Assert.Equal(QualityHoldStatus.Released, hold.Status);
        Assert.Throws<InvalidOperationException>(() => hold.Reject(DateTimeOffset.UtcNow, "late rejection"));
    }

    [Fact]
    public async Task A_short_quality_hold_releases_partial_reservation_and_does_not_persist()
    {
        Guid companyId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        IQualityHoldRepository holds = Substitute.For<IQualityHoldRepository>();
        IReservationService reservations = Substitute.For<IReservationService>();
        reservations.ReserveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Quantity>(),
            ReservationSource.QualityHold, operationId, Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>(), Arg.Any<DateOnly?>(), Arg.Any<string?>())
            .Returns(new ReserveOutcome(Guid.NewGuid(), new Quantity(3m, "EA"), new Quantity(2m, "EA"), new Quantity(0m, "EA"), DateTimeOffset.UtcNow));
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        PlaceQualityHoldCommand command = new(operationId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, 5m, "EA", "inspection");
        Func<Task> action = () => new PlaceQualityHoldCommandHandler(holds, reservations, tenant, company, clock).HandleAsync(command);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        await reservations.Received(1).ReleaseAsync(Arg.Any<Guid>(), "Quality hold shortfall", Arg.Any<CancellationToken>());
        holds.DidNotReceive().Add(Arg.Any<QualityHold>());
    }
}
