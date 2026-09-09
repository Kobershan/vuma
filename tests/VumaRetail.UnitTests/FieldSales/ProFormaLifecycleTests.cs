using VumaRetail.Domain.FieldSales;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.FieldSales;

/// <summary>
/// The proposal lifecycle: capture posts nothing by construction, submit/amend/reject/withdraw/
/// expire move the status machine, and approval + conversion are separate, terminal steps.
/// </summary>
public sealed class ProFormaLifecycleTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid RepId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid PartnerId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Capture_defaults_to_a_seven_day_expiry()
    {
        ProFormaOrder order = NewOrder();

        order.Status.Should().Be(ProFormaStatus.Draft);
        order.ExpiresAt.Should().Be(Now.AddDays(7));
        order.Gross.Amount.Should().Be(0m);
        order.ConvertedOrderId.Should().BeNull();
        order.ApprovalRequestId.Should().BeNull();
    }

    [Fact]
    public void Submit_needs_lines()
    {
        ProFormaOrder order = NewOrder();

        Action act = () => order.Submit(Now);

        act.Should().Throw<FieldSalesException>()
            .Where(e => e.Code == "PROFORMA_NO_LINES");
    }

    [Fact]
    public void Full_lifecycle_to_converted()
    {
        ProFormaOrder order = NewOrder();
        AddLine(order);

        order.Submit(Now);
        order.Status.Should().Be(ProFormaStatus.Submitted);

        order.RecordApproval(Guid.NewGuid());
        order.MarkApproved(new Money(5m, "ZAR"), Now);
        order.Status.Should().Be(ProFormaStatus.Approved);
        order.RepriceDelta!.Value.Amount.Should().Be(5m);

        Guid orderId = UuidV7.NewGuid();
        order.MarkConverted(orderId);
        order.Status.Should().Be(ProFormaStatus.Converted);
        order.ConvertedOrderId.Should().Be(orderId);

        Action amend = () => order.ReturnForAmendment("too late", Now);
        amend.Should().Throw<FieldSalesException>()
            .Where(e => e.Code == "PROFORMA_ILLEGAL_TRANSITION");
    }

    [Fact]
    public void Rejection_keeps_the_reason_and_freezes()
    {
        ProFormaOrder order = NewOrder();
        AddLine(order);
        order.Submit(Now);

        order.Reject("over limit", Now);

        order.Status.Should().Be(ProFormaStatus.Rejected);
        order.DecisionReason.Should().Be("over limit");
    }

    [Fact]
    public void Expired_submit_lapses_first()
    {
        ProFormaOrder order = NewOrder(expiresAt: Now.AddDays(-1));
        AddLine(order);

        order.IsExpired(Now).Should().BeTrue();
        order.Expire();
        order.Status.Should().Be(ProFormaStatus.Expired);

        Action submit = () => order.Submit(Now);
        submit.Should().Throw<FieldSalesException>()
            .Where(e => e.Code == "PROFORMA_ILLEGAL_TRANSITION");
    }

    [Fact]
    public void Credit_note_applies_into_a_return()
    {
        ProFormaCreditNote note = ProFormaCreditNote.Capture(
            TenantId, null, "PFC-000001", RepId, CompanyId, UuidV7.NewGuid(), "INV-1",
            "DAMAGED", "Arrived broken", "ZAR", "KEY-C1", Now);
        note.AddLine(UuidV7.NewGuid(), UuidV7.NewGuid(), null, 1m, "EA",
            new Money(100m, "ZAR"), new Money(13.04m, "ZAR"), new Money(86.96m, "ZAR"));

        note.Submit(Now);
        note.RecordApproval(Guid.NewGuid());
        note.MarkApproved(Now);

        Guid returnId = UuidV7.NewGuid();
        note.MarkApplied(returnId);
        note.Status.Should().Be(ProFormaStatus.Converted);
        note.ResultingReturnId.Should().Be(returnId);
    }

    [Fact]
    public void Targets_version_instead_of_rewriting()
    {
        RepTarget first = RepTarget.Set(
            TenantId, null, RepId, CompanyId, new DateOnly(2026, 8, 1),
            new Money(100000m, "ZAR"), "August plan", Now);

        RepTarget second = first.Supersede(new Money(120000m, "ZAR"), "stretch", Now);

        first.IsCurrent.Should().BeFalse();
        first.TargetNet.Amount.Should().Be(100000m);
        second.Version.Should().Be(2);
        second.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public void Snapshot_refuses_an_open_month()
    {
        Action act = () => RepPerformanceSnapshot.Snapshot(
            TenantId, null, RepId, CompanyId, new DateOnly(2026, 9, 1),
            new RepPerformanceFigures(0, 0, 0, 0, 0, 0, 0, null, 0),
            1, "close", Now, "ZAR");

        // September is still open at the September clock: only closed months snapshot.
        act.Should().Throw<ArgumentException>();
    }

    private static ProFormaOrder NewOrder(DateTimeOffset? expiresAt = null) => ProFormaOrder.Capture(
        TenantId, null, "PF-000001", RepId, CompanyId, PartnerId, "ZAR", $"KEY-{Guid.NewGuid():N}",
        Now, expiresAt);

    private static void AddLine(ProFormaOrder order) => order.AddLine(
        UuidV7.NewGuid(), null, 2m, "EA",
        new Money(799.00m, "ZAR"), Money.Zero("ZAR"), "STANDARD",
        new Money(208.43m, "ZAR"), new Money(1389.57m, "ZAR"),
        "Each", null, string.Empty,
        new Money(50m, "ZAR"), Now);
}
