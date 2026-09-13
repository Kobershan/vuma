using FluentAssertions;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.UnitTests.Reporting;

public sealed class ReportingDomainTests
{
    [Fact]
    public void Checkpoint_replay_does_not_move_cursor_backwards()
    {
        ProjectionCheckpoint checkpoint = ProjectionCheckpoint.Create(Guid.NewGuid(), null, Guid.NewGuid(), "sales", cursor: "0002");
        checkpoint.Advance(1, "0002");
        checkpoint.Cursor.Should().Be("0002");
        FluentActions.Invoking(() => checkpoint.Advance(0, "9999")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Dashboard_is_not_live_when_a_contributor_is_stale()
    {
        DashboardSnapshot snapshot = new(Guid.NewGuid(), new DateOnly(2026, 9, 13), DateTimeOffset.UtcNow,
            [new ReportFreshness("store-1", DateTimeOffset.UtcNow.AddHours(-24), true)],
            new Dictionary<string, decimal> { ["sales"] = 100m });
        snapshot.IsLive.Should().BeFalse();
    }

    [Fact]
    public void Report_definition_requires_publish_before_retire()
    {
        ReportDefinition definition = ReportDefinition.Create(Guid.NewGuid(), null, "sales", "Sales");
        FluentActions.Invoking(definition.Retire).Should().Throw<InvalidOperationException>();
        definition.Publish(); definition.Retire(); definition.Status.Should().Be(ReportDefinitionStatus.Retired);
    }
}
