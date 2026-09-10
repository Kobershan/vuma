using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Activity immutability: core fields frozen, metadata mutable. Scaffolding tests for Stage 19.
/// </summary>
public sealed class ActivityImmutabilityTests
{
    [Fact]
    public void Logged_activity_body_cannot_be_changed()
    {
        var activity = new Activity(Guid.NewGuid(), ActivityType.Email, "Follow-up call", "Discussed pricing");
        Action act = () => activity.UpdateBody("New body");
        act.Should().Throw<ActivityImmutableException>();
    }

    [Fact]
    public void Logged_activity_subject_cannot_be_changed()
    {
        var activity = new Activity(Guid.NewGuid(), ActivityType.Call, "Initial contact", null);
        Action act = () => activity.UpdateSubject("Changed");
        act.Should().Throw<ActivityImmutableException>();
    }

    [Fact]
    public void UpdatedAt_and_UpdatedBy_may_change()
    {
        var activity = new Activity(Guid.NewGuid(), ActivityType.Note, "Meeting notes", "body");
        var original = activity.UpdatedAt;
        activity.MarkUpdated("user:operator2", DateTimeOffset.UtcNow.AddMinutes(5));
        activity.UpdatedAt.Should().NotBe(original);
        activity.UpdatedBy.Should().Be("user:operator2");
    }

    [Fact]
    public void Deleting_an_activity_is_refused()
    {
        var activity = new Activity(Guid.NewGuid(), ActivityType.Visit, "Store visit", "body");
        Action act = () => activity.MarkDeleted();
        act.Should().Throw<ActivityImmutableException>();
    }
}
