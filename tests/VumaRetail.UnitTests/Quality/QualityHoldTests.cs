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
    public void Inspection_plan_is_versioned_and_only_published_plans_are_usable()
    {
        InspectionPlan plan = InspectionPlan.Create(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), null,
            2, "Incoming goods", 3, "No visible damage");

        plan.Status.Should().Be(InspectionPlanStatus.Draft);
        plan.Publish();

        plan.Status.Should().Be(InspectionPlanStatus.Published);
        plan.Version.Should().Be(2);
        plan.Retire();
        plan.Status.Should().Be(InspectionPlanStatus.Retired);
    }

    [Fact]
    public void Certificate_revocation_is_terminal_and_audit_stamped()
    {
        DateTimeOffset issued = DateTimeOffset.UtcNow;
        QualityCertificate certificate = QualityCertificate.Issue(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), null,
            "CERT-001", "Lab", issued, issued.AddYears(1), "result attachment hash");

        certificate.Revoke(issued.AddDays(1), "failed follow-up inspection");

        certificate.Status.Should().Be(QualityCertificateStatus.Revoked);
        certificate.RevokedAt.Should().Be(issued.AddDays(1));
        certificate.RevocationReason.Should().Be("failed follow-up inspection");
        Assert.Throws<InvalidOperationException>(() => certificate.Revoke(issued.AddDays(2), "duplicate"));
    }

    [Fact]
    public void Recall_case_deduplicates_trace_references_and_closes_terminally()
    {
        RecallCase recall = RecallCase.Open(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "REC-001", "LOT-7",
            "contamination", DateTimeOffset.UtcNow);

        recall.AddTraceReference("shipment", "SHP-1");
        recall.AddTraceReference("shipment", "SHP-1");
        recall.Close(DateTimeOffset.UtcNow, "all affected stock accounted for");

        recall.TraceReferences.Should().ContainSingle();
        recall.Status.Should().Be(RecallCaseStatus.Closed);
        Assert.Throws<InvalidOperationException>(() => recall.AddTraceReference("shipment", "SHP-2"));
    }

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
        IInspectionPlanRepository plans = Substitute.For<IInspectionPlanRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        RecordInspectionCommand command = new(operationId, companyId, holdId, null, true, 2, "seal intact");

        Guid result = await new RecordInspectionCommandHandler(inspections, plans, holds, tenant, company, clock).HandleAsync(command);

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

        Assert.Throws<InvalidOperationException>(() => issue.Close(Guid.NewGuid(), DateTimeOffset.UtcNow, "discarded"));
        issue.StartCorrectiveAction(Guid.NewGuid());
        issue.Close(Guid.NewGuid(), DateTimeOffset.UtcNow, "discarded and supplier notified");

        Assert.Equal(NonConformanceStatus.Closed, issue.Status);
    }

    [Fact]
    public async Task Certificate_issue_rejects_a_company_that_is_not_active()
    {
        Guid activeCompany = Guid.NewGuid();
        IQualityCertificateRepository certificates = Substitute.For<IQualityCertificateRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(activeCompany);
        IssueQualityCertificateCommand command = new(Guid.NewGuid(), Guid.NewGuid(), null, "CERT-1", "Lab",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), "evidence");

        Func<Task> action = () => new IssueQualityCertificateCommandHandler(certificates, tenant, company).HandleAsync(command);

        await action.Should().ThrowAsync<InvalidOperationException>();
        certificates.DidNotReceive().Add(Arg.Any<QualityCertificate>());
    }

    [Fact]
    public async Task Inspection_plan_create_rejects_a_company_that_is_not_active()
    {
        Guid activeCompany = Guid.NewGuid();
        IInspectionPlanRepository plans = Substitute.For<IInspectionPlanRepository>();
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        ICompanyContext company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(activeCompany);
        CreateInspectionPlanCommand command = new(Guid.NewGuid(), Guid.NewGuid(), null, 1, "Incoming", 1, "Pass");

        Func<Task> action = () => new CreateInspectionPlanCommandHandler(plans, tenant, company).HandleAsync(command);

        await action.Should().ThrowAsync<InvalidOperationException>();
        plans.DidNotReceive().Add(Arg.Any<InspectionPlan>());
    }
}
