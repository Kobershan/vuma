using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Segment membership: static vs dynamic, never mixed. Scaffolding tests for Stage 19.
/// </summary>
public sealed class SegmentMembershipTests
{
    [Fact]
    public void Static_segment_members_are_queryable()
    {
        var segment = new Segment(Guid.NewGuid(), "Gold Members", SegmentKind.Static);
        var memberId = Guid.NewGuid();
        segment.AddMember(memberId, MemberType.Customer);
        segment.IsMember(memberId, MemberType.Customer).Should().BeTrue();
    }

    [Fact]
    public void Dynamic_segment_write_is_refused()
    {
        var segment = new Segment(Guid.NewGuid(), "Recent Buyers", SegmentKind.Dynamic);
        Action act = () => segment.AddMember(Guid.NewGuid(), MemberType.Customer);
        act.Should().Throw<DynamicSegmentWriteNotAllowedException>();
    }

    [Fact]
    public void Mixed_static_and_dynamic_is_refused()
    {
        Action act = () => new Segment(Guid.NewGuid(), "Impossible", SegmentKind.Static, isDynamic: true);
        act.Should().Throw<SegmentMixedKindException>();
    }

    [Fact]
    public void Inactive_segment_returns_no_members()
    {
        var segment = new Segment(Guid.NewGuid(), "Dormant", SegmentKind.Static);
        segment.AddMember(Guid.NewGuid(), MemberType.Customer);
        segment.Deactivate();
        segment.IsMember(Guid.NewGuid(), MemberType.Customer).Should().BeFalse();
    }
}
