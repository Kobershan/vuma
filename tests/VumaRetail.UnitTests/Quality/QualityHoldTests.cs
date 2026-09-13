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

    [Fact]
    public async Task Inspection_is_idempotent_and_requires_an_active_hold()
    {
        Guid tenantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        QualityHold hold = QualityHold.Place(tenantId, null, companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            new Quantity(2m, "EA"), "inspection", DateTimeOffset.UtcNow, Guid.NewGuid());
        Guid holdId = hold.Id;
        IQualityHoldRepository holds = Substitute.For<IQualityHoldRepository>();
        holds.FindAsync(holdId, Arg.Any<CancellationToken>()).Returns(hold);
        IInspectionResultRepository inspections = Substitute.For<IInspectionResultRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        RecordInspectionCommand command = new(operationId, companyId, holdId, true, 2, "seal intact");

        Guid result = await new RecordInspectionCommandHandler(inspections, holds, tenant, company, clock).HandleAsync(command);

        result.Should().NotBeEmpty();
        inspections.Received(1).Add(Arg.Any<InspectionResult>());
    }

    [Fact]
    public void Rejected_hold_records_a_terminal_disposition()
    {
        QualityHold hold = QualityHold.Place(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            new Quantity(1m, "EA"), "failed inspection", DateTimeOffset.UtcNow, Guid.NewGuid());

        hold.Reject(DateTimeOffset.UtcNow, "failed inspection");

        Assert.Equal(QualityHoldStatus.Rejected, hold.Status);
        Assert.Throws<InvalidOperationException>(() => hold.Release(DateTimeOffset.UtcNow, "pass"));
    }

    [Fact]
    public void Non_conformance_requires_corrective_action_before_closure()
    {
        NonConformance issue = NonConformance.Open(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            NonConformanceSeverity.Major, "seal broken", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => issue.Close(DateTimeOffset.UtcNow, "discarded"));
        issue.StartCorrectiveAction();
        issue.Close(DateTimeOffset.UtcNow, "discarded and supplier notified");

        Assert.Equal(NonConformanceStatus.Closed, issue.Status);
    }
}
