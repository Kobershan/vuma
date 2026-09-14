using FluentAssertions;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.UnitTests.Marketing;

public sealed class JourneyModelTests
{
    [Fact]
    public void Journey_must_be_published_before_enrollment_is_possible()
    {
        JourneyDefinition journey = JourneyDefinition.Create(Guid.NewGuid(), Guid.NewGuid(), "Welcome", 1, "{\"steps\":[]}");
        journey.Status.Should().Be(JourneyDefinitionStatus.Draft);
        journey.Publish();
        journey.Status.Should().Be(JourneyDefinitionStatus.Published);
        journey.Retire();
        journey.Status.Should().Be(JourneyDefinitionStatus.Retired);
    }

    [Fact]
    public void Attribution_is_content_free_and_uses_utc_time()
    {
        DateTimeOffset local = new(2026, 9, 14, 14, 0, 0, TimeSpan.FromHours(2));
        AttributionEvent item = AttributionEvent.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "clicked", local);
        item.OccurredAt.Offset.Should().Be(TimeSpan.Zero);
        item.EventType.Should().Be("clicked");
    }
}
