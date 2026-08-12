using VumaRetail.Domain.Workflow;

namespace VumaRetail.UnitTests.Workflow;

/// <summary>Notification state transitions: sent, failed, read (<c>docs/stages/STAGE-05-workflow.md</c>).</summary>
public sealed class NotificationTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Recipient = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_raised_notification_starts_pending_and_unread()
    {
        Notification notification = Raise();

        notification.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Pending);
        notification.IsRead.Should().BeFalse();
        notification.SentAt.Should().BeNull();
        notification.ReadAt.Should().BeNull();
    }

    [Fact]
    public void MarkSent_records_when_and_clears_any_prior_error()
    {
        Notification notification = Raise();
        notification.MarkFailed("channel unavailable");

        notification.MarkSent(Now.AddMinutes(1));

        notification.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Sent);
        notification.SentAt.Should().Be(Now.AddMinutes(1));
        notification.DeliveryError.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_records_why_and_does_not_touch_SentAt()
    {
        Notification notification = Raise();

        notification.MarkFailed("no email provider configured");

        notification.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Failed);
        notification.DeliveryError.Should().Be("no email provider configured");
        notification.SentAt.Should().BeNull();
    }

    [Fact]
    public void MarkRead_is_idempotent_and_keeps_the_first_ReadAt()
    {
        Notification notification = Raise();

        notification.MarkRead(Now.AddMinutes(5));
        notification.MarkRead(Now.AddMinutes(9));

        notification.IsRead.Should().BeTrue();
        notification.ReadAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void A_title_longer_than_the_limit_is_truncated_not_refused()
    {
        string longTitle = new('x', Notification.MaxTitleLength + 50);

        Notification notification = Notification.Raise(
            Tenant, null, Recipient, "workflow.approval.pending", longTitle, "body",
            NotificationSeverity.Info, NotificationChannel.InApp, Now);

        notification.Title.Should().HaveLength(Notification.MaxTitleLength);
    }

    [Fact]
    public void A_body_longer_than_the_limit_is_truncated_not_refused()
    {
        string longBody = new('y', Notification.MaxBodyLength + 500);

        Notification notification = Notification.Raise(
            Tenant, null, Recipient, "workflow.approval.pending", "title", longBody,
            NotificationSeverity.Info, NotificationChannel.InApp, Now);

        notification.Body.Should().HaveLength(Notification.MaxBodyLength);
    }

    [Fact]
    public void An_empty_category_title_or_body_is_refused()
    {
        Action noCategory = () => Notification.Raise(
            Tenant, null, Recipient, "", "title", "body", NotificationSeverity.Info, NotificationChannel.InApp, Now);
        Action noTitle = () => Notification.Raise(
            Tenant, null, Recipient, "cat", "", "body", NotificationSeverity.Info, NotificationChannel.InApp, Now);
        Action noBody = () => Notification.Raise(
            Tenant, null, Recipient, "cat", "title", "", NotificationSeverity.Info, NotificationChannel.InApp, Now);

        noCategory.Should().Throw<ArgumentException>();
        noTitle.Should().Throw<ArgumentException>();
        noBody.Should().Throw<ArgumentException>();
    }

    private static Notification Raise() => Notification.Raise(
        Tenant,
        null,
        Recipient,
        "workflow.approval.pending",
        "An approval is waiting",
        "Purchase order PO-1042 needs your decision.",
        NotificationSeverity.Info,
        NotificationChannel.InApp,
        Now);
}
