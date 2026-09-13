using VumaRetail.Domain.HrManagement;

namespace VumaRetail.UnitTests.Hr;

public sealed class DisciplinaryCaseTests
{
    [Fact]
    public void Case_requires_investigation_before_one_way_decision()
    {
        var opened = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);
        var @case = DisciplinaryCase.Open(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 12), "Late arrival", opened);

        FluentActions.Invoking(() => @case.Decide("Written warning", opened.AddHours(1)))
            .Should().Throw<InvalidOperationException>();
        @case.StartInvestigation(opened.AddHours(1));
        @case.Decide("Written warning", opened.AddHours(2));

        @case.Status.Should().Be(DisciplinaryCaseStatus.Decided);
        @case.Decision.Should().Be("Written warning");
        FluentActions.Invoking(() => @case.Decide("Dismissed", opened.AddHours(3)))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Investigation_and_decision_cannot_predate_their_parent_event()
    {
        var opened = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);
        var @case = DisciplinaryCase.Open(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 12), "Unsafe conduct", opened);

        FluentActions.Invoking(() => @case.StartInvestigation(opened.AddMinutes(-1)))
            .Should().Throw<ArgumentException>();
        @case.StartInvestigation(opened);
        FluentActions.Invoking(() => @case.Decide("Final warning", opened.AddMinutes(-1)))
            .Should().Throw<ArgumentException>();
    }
}
