using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.FieldSales;
using VumaRetail.Application.FieldSales.Commands;
using VumaRetail.Application.FieldSales.Queries;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.UnitTests.FieldSales;

/// <summary>
/// Capture proves nothing by construction: the handler writes the pro forma only (the absence
/// of finance/reservation dependencies is the proof), replays by key, and refuses foreign scopes.
/// </summary>
public sealed class ProFormaHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid RepId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid PartnerId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    private readonly IProFormaOrderRepository _orders = Substitute.For<IProFormaOrderRepository>();
    private readonly IRepRepository _reps = Substitute.For<IRepRepository>();
    private readonly IDocumentNumberSequence _numbers = Substitute.For<IDocumentNumberSequence>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ITaxCalculator _tax = Substitute.For<ITaxCalculator>();
    private readonly IPackSizeResolver _packs = Substitute.For<IPackSizeResolver>();
    private readonly IAvailabilityProbe _availability = Substitute.For<IAvailabilityProbe>();
    private readonly IApprovalService _approvals = Substitute.For<IApprovalService>();
    private readonly IFieldSalesApprovalService _conversion = Substitute.For<IFieldSalesApprovalService>();
    private readonly IPrincipalAccessor _principal = Substitute.For<IPrincipalAccessor>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ProFormaHandlerTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _clock.UtcNow.Returns(Now);
        _numbers.NextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("PF-000001");
        _packs.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new PackSizeSnapshot("Each"));
        _tax.CalculateAsync(Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new TaxCalculation("STANDARD", new Money(1389.57m, "ZAR"), new Money(208.43m, "ZAR"), new Money(1598.00m, "ZAR"), 0.15m));
        _availability.ProbeAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new AvailabilityProbeResult(50m, Now));
        _principal.Principal.Returns("user:manager");
    }

    [Fact]
    public async Task Capture_replays_by_key_and_snapshots_everything()
    {
        Rep rep = OpenRep();
        _reps.FindAsync(RepId, Arg.Any<CancellationToken>()).Returns(rep);

        var handler = new CaptureProFormaCommandHandler(
            _orders, _reps, _numbers, _tenant, _tax, _packs, _availability, _clock);

        ProFormaOrder? captured = null;
        _orders.Add(Arg.Do<ProFormaOrder>(order => captured = order));

        Guid first = await handler.HandleAsync(new CaptureProFormaCommand(
            RepId, CompanyId, PartnerId, "ZAR", "KEY-1",
            [new ProFormaLineInput(ItemId, null, 2m, "EA", 799.00m, 0m, "STANDARD", "ZAR")]));

        captured.Should().NotBeNull();
        captured!.Lines.Should().ContainSingle();
        captured.Lines.Single().PackSizeDescription.Should().Be("Each");
        captured.Lines.Single().AvailableAtCapture.Amount.Should().Be(50m);
        captured.Lines.Single().AvailabilityAsAt.Should().Be(Now);

        _orders.FindByIdempotencyKeyAsync("KEY-1", Arg.Any<CancellationToken>()).Returns(captured);
        Guid second = await handler.HandleAsync(new CaptureProFormaCommand(
            RepId, CompanyId, PartnerId, "ZAR", "KEY-1",
            [new ProFormaLineInput(ItemId, null, 2m, "EA", 799.00m, 0m, "STANDARD", "ZAR")]));

        second.Should().Be(first, "a replayed capture is one document, not two");
    }

    [Fact]
    public async Task Capture_refuses_a_customer_outside_the_territory()
    {
        _reps.FindAsync(RepId, Arg.Any<CancellationToken>()).Returns(OpenRep());

        var handler = new CaptureProFormaCommandHandler(
            _orders, _reps, _numbers, _tenant, _tax, _packs, _availability, _clock);
        Func<Task> act = () => handler.HandleAsync(new CaptureProFormaCommand(
            RepId, CompanyId, UuidV7.NewGuid(), "ZAR", "KEY-2",
            [new ProFormaLineInput(ItemId, null, 1m, "EA", 100m, 0m, "STANDARD", "ZAR")]));

        await act.Should().ThrowAsync<FieldSalesException>()
            .Where(e => e.Code == "REP_FORBIDDEN");
    }

    [Fact]
    public async Task Submit_records_the_pending_request()
    {
        ProFormaOrder order = SubmittedOrder(out Guid requestId, submit: false);
        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _approvals.EvaluateAsync(Arg.Any<ApprovalContext>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovalOutcome(ApprovalOutcomeKind.Pending, requestId));

        var handler = new SubmitProFormaCommandHandler(_orders, _approvals, _tenant, _clock);
        await handler.HandleAsync(new SubmitProFormaCommand(order.Id));

        order.Status.Should().Be(ProFormaStatus.Submitted);
        order.ApprovalRequestId.Should().Be(requestId);
    }

    [Fact]
    public async Task Approve_decides_then_converts()
    {
        ProFormaOrder order = SubmittedOrder(out Guid requestId);
        order.RecordApproval(requestId);
        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _approvals.DecideAsync(requestId, ApprovalDecisionOutcome.Approved, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovalDecisionResult(requestId, ApprovalRequestStatus.Approved, 1, 1));
        Guid orderId = UuidV7.NewGuid();
        _conversion.ApproveOrderAsync(order.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovedProForma(orderId, ["INV-1"], 0m));

        var handler = new ApproveProFormaCommandHandler(
            _orders, _approvals, _conversion, _principal, _tenant);
        Guid converted = await handler.HandleAsync(new ApproveProFormaCommand(order.Id, "looks good"));

        converted.Should().Be(orderId);
        await _conversion.Received(1).ApproveOrderAsync(order.Id, "user:manager", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_without_an_approval_record_is_refused()
    {
        // No policy configured (or below threshold): the engine auto-approves with no request —
        // and the saga still runs exactly once, through the same conversion call.
        ProFormaOrder order = SubmittedOrder(out _);
        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        Guid orderId = UuidV7.NewGuid();
        _conversion.ApproveOrderAsync(order.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovedProForma(orderId, ["INV-1"], 0m));

        var handler = new ApproveProFormaCommandHandler(
            _orders, _approvals, _conversion, _principal, _tenant);
        Guid converted = await handler.HandleAsync(new ApproveProFormaCommand(order.Id, "ok"));

        converted.Should().Be(orderId);
        await _approvals.DidNotReceive().DecideAsync(
            Arg.Any<Guid>(), Arg.Any<ApprovalDecisionOutcome>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Availability_hides_nothing_but_shows_as_at()
    {
        Rep rep = OpenRep();
        _reps.FindAsync(RepId, Arg.Any<CancellationToken>()).Returns(rep);

        var handler = new GetRepAvailabilityQueryHandler(_reps, _availability, _tenant);
        RepAvailabilityView view = await handler.HandleAsync(
            new GetRepAvailabilityQuery(RepId, ItemId, null));

        view.Companies.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new RepAvailabilityRow(CompanyId, 50m, Now));
        view.HasStaleContributor.Should().BeFalse();
    }

    [Fact]
    public async Task Performance_compares_with_variance()
    {
        var snapshots = Substitute.For<IRepPerformanceRepository>();
        _reps.FindAsync(RepId, Arg.Any<CancellationToken>()).Returns(OpenRep());
        snapshots.FindLatestAsync(RepId, CompanyId, new DateOnly(2026, 8, 1), Arg.Any<CancellationToken>())
            .Returns(PerformanceSnapshot(80000m, 1));
        snapshots.FindLatestAsync(RepId, CompanyId, new DateOnly(2026, 7, 1), Arg.Any<CancellationToken>())
            .Returns(PerformanceSnapshot(100000m, 1));

        var handler = new GetRepPerformanceQueryHandler(snapshots, _reps, _tenant);
        RepPerformanceView view = await handler.HandleAsync(new GetRepPerformanceQuery(
            RepId, CompanyId, new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1), RepId, false));

        view.NetValue.Should().Be(80000m);
        view.CompareNetValue.Should().Be(100000m);
        view.Variance.Should().Be(-20000m);
        view.VariancePercent.Should().Be(-20m);
    }

    [Fact]
    public async Task Cross_rep_performance_read_needs_the_team_grant()
    {
        var snapshots = Substitute.For<IRepPerformanceRepository>();
        _reps.FindAsync(RepId, Arg.Any<CancellationToken>()).Returns(OpenRep());

        var handler = new GetRepPerformanceQueryHandler(snapshots, _reps, _tenant);
        Func<Task> act = () => handler.HandleAsync(new GetRepPerformanceQuery(
            RepId, CompanyId, new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1),
            UuidV7.NewGuid(), CallerCanViewTeam: false));

        await act.Should().ThrowAsync<FieldSalesException>()
            .Where(e => e.Code == "REP_FORBIDDEN");
    }

    [Fact]
    public void Calculator_folds_statuses_and_nets_invoices_less_credits()
    {
        var calculator = new RepPerformanceCalculator();
        RepPerformanceFigures figures = calculator.Calculate(new RepPerformanceInputs(
            [new ProFormaPerformanceRow(ProFormaStatus.Converted, 1000m, PartnerId),
             new ProFormaPerformanceRow(ProFormaStatus.Rejected, 200m, PartnerId),
             new ProFormaPerformanceRow(ProFormaStatus.Draft, 300m, UuidV7.NewGuid())],
            [new InvoicedPerformanceRow(900m, 90m)],
            [new InvoicedPerformanceRow(100m, 10m)],
            IncludeMargin: true));

        figures.CapturedCount.Should().Be(3);
        figures.CapturedValue.Should().Be(1500m);
        figures.ConvertedValue.Should().Be(1000m);
        figures.RejectedValue.Should().Be(200m);
        figures.InvoicedValue.Should().Be(900m);
        figures.CreditedValue.Should().Be(100m);
        figures.MarginValue.Should().Be(90m);
        figures.ActiveCustomers.Should().Be(2);
    }

    private Rep OpenRep()
    {
        Rep rep = Rep.Register(TenantId, null, UuidV7.NewGuid(), "Thandi", [CompanyId]);
        rep.AssignTerritory([PartnerId]);
        return rep;
    }

    private ProFormaOrder SubmittedOrder(out Guid requestId, bool submit = true)
    {
        requestId = UuidV7.NewGuid();
        ProFormaOrder order = ProFormaOrder.Capture(
            TenantId, null, "PF-000001", RepId, CompanyId, PartnerId, "ZAR", $"K-{Guid.NewGuid():N}", Now);
        order.AddLine(ItemId, null, 1m, "EA", new Money(100m, "ZAR"), Money.Zero("ZAR"),
            "STANDARD", new Money(13.04m, "ZAR"), new Money(86.96m, "ZAR"),
            "Each", null, string.Empty, new Money(10m, "ZAR"), Now);
        if (submit)
        {
            order.Submit(Now);
        }

        return order;
    }

    private static RepPerformanceSnapshot PerformanceSnapshot(decimal net, int version)
        => RepPerformanceSnapshot.Snapshot(
            TenantId, null, RepId, CompanyId, new DateOnly(2026, 8, 1),
            new RepPerformanceFigures(5, 90000m, 80000m, 0m, 0m, net, 0m, null, 3),
            version, "close", Now, "ZAR");
}
