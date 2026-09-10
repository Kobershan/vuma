using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Segment membership: static vs dynamic, never mixed.
/// </summary>
public sealed class SegmentMembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Static_segment_accepts_members()
    {
        var segment = new Segment(Guid.NewGuid(), Guid.NewGuid(), "Gold Members", SegmentKind.Static);
        var member = new SegmentMember(
            Guid.NewGuid(), Guid.NewGuid(), segment, MemberType.Customer, Guid.NewGuid(),
            "user:operator", Now);
        member.SegmentId.Should().Be(segment.Id);
    }

    [Fact]
    public void Dynamic_segment_write_is_refused()
    {
        var segment = new Segment(
            Guid.NewGuid(), Guid.NewGuid(), "Recent Buyers", SegmentKind.Dynamic,
            queryExpression: "lastPurchase < 30d");
        Action act = () => segment.RefuseMemberWrite();
        act.Should().Throw<DynamicSegmentWriteNotAllowedException>();
    }

    [Fact]
    public void Dynamic_member_row_is_refused()
    {
        var segment = new Segment(
            Guid.NewGuid(), Guid.NewGuid(), "Recent Buyers", SegmentKind.Dynamic,
            queryExpression: "lastPurchase < 30d");
        Action act = () => new SegmentMember(
            Guid.NewGuid(), Guid.NewGuid(), segment, MemberType.Customer, Guid.NewGuid(),
            "user:operator", Now);
        act.Should().Throw<DynamicSegmentWriteNotAllowedException>();
    }

    [Fact]
    public void Mixed_static_and_dynamic_is_refused()
    {
        Action act = () => new Segment(
            Guid.NewGuid(), Guid.NewGuid(), "Impossible", SegmentKind.Static,
            queryExpression: "lastPurchase < 30d");
        act.Should().Throw<SegmentMixedKindException>();
    }

    [Fact]
    public void Inactive_segment_matches_nobody()
    {
        var segment = new Segment(Guid.NewGuid(), Guid.NewGuid(), "Dormant", SegmentKind.Static);
        segment.Deactivate();
        segment.IsActive.Should().BeFalse();
    }
}
