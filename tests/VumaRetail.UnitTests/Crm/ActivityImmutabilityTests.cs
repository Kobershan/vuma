using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Activity immutability: content frozen, metadata stamped by persistence.
/// </summary>
public sealed class ActivityImmutabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Logged_activity_body_cannot_be_changed()
    {
        var activity = new Activity(
            Guid.NewGuid(), Guid.NewGuid(), ActivityType.Email, "Follow-up call", "Discussed pricing", Now);
        Action act = () => activity.UpdateBody("New body");
        act.Should().Throw<ActivityImmutableException>();
    }

    [Fact]
    public void Logged_activity_subject_cannot_be_changed()
    {
        var activity = new Activity(
            Guid.NewGuid(), Guid.NewGuid(), ActivityType.Call, "Initial contact", null, Now);
        Action act = () => activity.UpdateSubject("Changed");
        act.Should().Throw<ActivityImmutableException>();
    }

    [Fact]
    public void UpdatedAt_and_UpdatedBy_may_change()
    {
        var activity = new Activity(
            Guid.NewGuid(), Guid.NewGuid(), ActivityType.Note, "Meeting notes", "body", Now);
        var original = activity.UpdatedAt;
        activity.MarkUpdated("user:operator2", Now.AddMinutes(5));
        activity.UpdatedAt.Should().NotBe(original);
        activity.UpdatedBy.Should().Be("user:operator2");
    }

    [Fact]
    public void Deleting_an_activity_is_refused()
    {
        var activity = new Activity(
            Guid.NewGuid(), Guid.NewGuid(), ActivityType.Visit, "Store visit", "body", Now);
        Action act = () => activity.MarkDeleted();
        act.Should().Throw<ActivityImmutableException>();
    }
}
