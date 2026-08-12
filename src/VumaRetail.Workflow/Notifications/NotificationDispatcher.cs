using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Workflow.Notifications;

/// <summary>
/// Creates one persisted <see cref="Notification"/> row per requested channel and dispatches each
/// through the matching <see cref="INotificationChannel"/>.
/// </summary>
/// <param name="notifications">Where notifications are recorded.</param>
/// <param name="channels">Every registered channel, one per <see cref="NotificationChannel"/> kind.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="clock">The only source of time.</param>
public sealed class NotificationDispatcher(
    INotificationRepository notifications,
    IEnumerable<INotificationChannel> channels,
    ITenantContext tenant,
    IClock clock) : INotificationDispatcher
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> NotifyAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Channels.Count == 0)
        {
            throw new ArgumentException("A notification must name at least one channel.", nameof(request));
        }

        List<Guid> created = [];

        foreach (NotificationChannel kind in request.Channels.Distinct())
        {
            DateTimeOffset now = clock.UtcNow;

            Notification notification = Notification.Raise(
                tenant.TenantId,
                tenant.StoreId,
                request.RecipientUserId,
                request.Category,
                request.Title,
                request.Body,
                request.Severity,
                kind,
                now,
                request.RelatedModule,
                request.RelatedEntityType,
                request.RelatedEntityId,
                request.ActionUrl);

            notifications.Add(notification);
            created.Add(notification.Id);

            INotificationChannel? channel = channels.FirstOrDefault(candidate => candidate.Kind == kind);

            if (channel is null)
            {
                notification.MarkFailed($"No {kind} channel is registered.");
                continue;
            }

            NotificationDeliveryOutcome outcome = await channel
                .SendAsync(notification, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Success)
            {
                notification.MarkSent(clock.UtcNow);
            }
            else
            {
                notification.MarkFailed(outcome.Error ?? "The channel refused the notification without a reason.");
            }
        }

        return created;
    }
}
