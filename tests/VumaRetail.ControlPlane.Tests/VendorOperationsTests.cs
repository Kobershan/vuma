using FluentAssertions;
using Xunit;
using VumaRetail.ControlPlane;

namespace VumaRetail.ControlPlane.Tests;

public sealed class VendorOperationsTests
{
    [Fact]
    public void Abuse_signals_are_queued_for_review_and_not_auto_disabled()
    {
        var detector = new AbuseDetector();
        DateTimeOffset time = DateTimeOffset.UtcNow;
        detector.Observe(new DeviceObservation("licence", "fp-a", "node-a", 5, time, 0, 0, "INV"));
        AbuseCase signal = detector.Observe(new DeviceObservation("licence", "fp-b", "node-b", 1,
            time.AddMinutes(30), 20, 20, "INV")).Should().ContainSingle(x => x.Signal.Code == "duplicate-install").Subject;
        signal.Action.Should().BeNull();
        detector.Resolve(signal.Id, "false-positive", "none").Action.Should().Be("none");
    }

    [Fact]
    public void Counter_rollback_is_detected()
    {
        var detector = new AbuseDetector();
        DateTimeOffset time = DateTimeOffset.UtcNow;
        detector.Observe(new DeviceObservation("licence", "fp", "node", 5, time, 0, 0, "INV"));
        detector.Observe(new DeviceObservation("licence", "fp", "node", 4, time.AddMinutes(1), 0, 0, "INV"))
            .Should().Contain(x => x.Signal.Code == "counter-rollback");
    }

    [Fact]
    public void Partners_can_only_view_their_own_tenants()
    {
        var principal = new VendorPrincipal("p", VendorRole.Support, "partner-a");
        VendorScope.CanViewTenant(principal, "tenant-a", new Dictionary<string, string> { ["tenant-a"] = "partner-a", ["tenant-b"] = "partner-b" }).Should().BeTrue();
        VendorScope.CanViewTenant(principal, "tenant-b", new Dictionary<string, string> { ["tenant-b"] = "partner-b" }).Should().BeFalse();
    }

    [Fact]
    public void Support_requires_tenant_approval_and_expires()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SupportGrant grant = SupportGrantPolicy.Create(Guid.NewGuid(), "tenant", "agent", now.AddHours(1), true, now);
        SupportGrantPolicy.IsActive(grant, now).Should().BeTrue();
        SupportGrantPolicy.IsActive(grant, now.AddHours(1)).Should().BeFalse();
    }
}
