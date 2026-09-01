using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Workflow;

/// <summary>
/// One message to one recipient on one channel.
/// </summary>
/// <remarks>
/// A fan-out to three recipients on two channels each is six rows, not one row with a recipient list —
/// the same reasoning <c>identity.role_permissions</c> uses (<c>DATA_MODEL.md</c> §4b): each row syncs,
/// reads and audits as one self-contained fact, "this person was told this, on this channel, and here
/// is what happened when we tried".
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.LastWriterWins)]
public sealed class Notification : Entity
{
    private Notification(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Notification()
    {
    }

    /// <summary>Who it is for.</summary>
    public Guid RecipientUserId { get; private set; }

    /// <summary>
    /// A namespaced category — <c>workflow.approval.pending</c>, <c>sync.conflict.raised</c> — for
    /// filtering and for a client that wants to render different categories differently.
    /// </summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>A short summary, shown in a list.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The full message.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>How urgently it should be shown.</summary>
    public NotificationSeverity Severity { get; private set; } = NotificationSeverity.Info;

    /// <summary>Which channel this row travels on.</summary>
    public NotificationChannel Channel { get; private set; } = NotificationChannel.InApp;

    /// <summary>The module that raised it, for a deep link back to the source.</summary>
    public string? RelatedModule { get; private set; }

    /// <summary>What kind of thing it relates to, alongside <see cref="RelatedModule"/>.</summary>
    public string? RelatedEntityType { get; private set; }

    /// <summary>The id of the thing it relates to.</summary>
    public Guid? RelatedEntityId { get; private set; }

    /// <summary>Where to send a user who acts on this notification, if there is a next step.</summary>
    public string? ActionUrl { get; private set; }

    /// <summary>Whether the channel has accepted this notification for delivery.</summary>
    public NotificationDeliveryStatus DeliveryStatus { get; private set; } = NotificationDeliveryStatus.Pending;

    /// <summary>When the channel accepted it, UTC.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Why the channel could not send it, if it could not.</summary>
    public string? DeliveryError { get; private set; }

    /// <summary>
    /// Whether the recipient has read it. Meaningful for <see cref="NotificationChannel.InApp"/> only —
    /// an email or a push notification has no "read" state this system observes.
    /// </summary>
    public bool IsRead { get; private set; }

    /// <summary>When it was read, UTC.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>The longest title that will be stored.</summary>
    public const int MaxTitleLength = 256;

    /// <summary>The longest body that will be stored.</summary>
    public const int MaxBodyLength = 4096;

    /// <summary>Raises a notification, pending delivery.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store, if the recipient's session is store-scoped.</param>
    /// <param name="recipientUserId">Who it is for.</param>
    /// <param name="category">A namespaced category.</param>
    /// <param name="title">A short summary.</param>
    /// <param name="body">The full message.</param>
    /// <param name="severity">How urgently it should be shown.</param>
    /// <param name="channel">Which channel this row travels on.</param>
    /// <param name="now">The instant, UTC.</param>
    /// <param name="relatedModule">The module that raised it.</param>
    /// <param name="relatedEntityType">What kind of thing it relates to.</param>
    /// <param name="relatedEntityId">The id of the thing it relates to.</param>
    /// <param name="actionUrl">Where to send a user who acts on it.</param>
    /// <returns>The notification.</returns>
    public static Notification Raise(
        Guid tenantId,
        Guid? storeId,
        Guid recipientUserId,
        string category,
        string title,
        string body,
        NotificationSeverity severity,
        NotificationChannel channel,
        DateTimeOffset now,
        string? relatedModule = null,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        string? actionUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new Notification(tenantId, storeId)
        {
            RecipientUserId = recipientUserId,
            Category = category,
            Title = Truncate(title, MaxTitleLength),
            Body = Truncate(body, MaxBodyLength),
            Severity = severity,
            Channel = channel,
            RelatedModule = relatedModule,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            ActionUrl = actionUrl,
            DeliveryStatus = NotificationDeliveryStatus.Pending,
        };
    }

    /// <summary>Records that the channel accepted this notification.</summary>
    /// <param name="now">The instant, UTC.</param>
    public void MarkSent(DateTimeOffset now)
    {
        DeliveryStatus = NotificationDeliveryStatus.Sent;
        SentAt = now;
        DeliveryError = null;
    }

    /// <summary>Records that the channel could not send this notification.</summary>
    /// <param name="error">Why, for diagnostics. Never a stack trace or a credential.</param>
    public void MarkFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        DeliveryStatus = NotificationDeliveryStatus.Failed;
        DeliveryError = Truncate(error, MaxBodyLength);
    }

    /// <summary>Marks an in-app notification read. Idempotent.</summary>
    /// <param name="now">The instant, UTC.</param>
    public void MarkRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAt = now;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
