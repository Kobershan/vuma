using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Workflow.Notifications;

/// <summary>
/// The in-app channel. Delivery is trivial: the notification row is the delivery, so this channel
/// only has to say so.
/// </summary>
/// <remarks>
/// The one channel this product ships without a vendor integration — a tenant's staff always have a
/// working notification inbox, whatever is or is not configured for email and push.
/// </remarks>
public sealed class InAppNotificationChannel : INotificationChannel
{
    /// <inheritdoc />
    public NotificationChannel Kind => NotificationChannel.InApp;

    /// <inheritdoc />
    public Task<NotificationDeliveryOutcome> SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
        => Task.FromResult(NotificationDeliveryOutcome.Delivered);
}

/// <summary>
/// A working log implementation for a channel with no real provider configured yet.
/// </summary>
/// <remarks>
/// The <c>InProcessControlPlane</c> pattern from Stage 04b: rather than a silent no-op
/// or a channel that pretends to be a real inbox provider, this one does something real and
/// observable — it logs exactly what would have been sent, at a level an operator can find. The real
/// providers are Deferred (<c>docs/PROGRESS.md</c> §3) until a market and a budget choose one.
/// </remarks>
/// <param name="kind">Which channel this stands in for.</param>
/// <param name="logger">Where the would-be delivery is recorded.</param>
public abstract class LogNotificationChannel(NotificationChannel kind, ILogger logger) : INotificationChannel
{
    /// <inheritdoc />
    public NotificationChannel Kind { get; } = kind;

    /// <inheritdoc />
    public Task<NotificationDeliveryOutcome> SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // The message body is not a credential, but it is a tenant's business content — logged at
        // Information rather than the message payload rule docs/API_STANDARDS.md §11 applies to the
        // command pipeline, because this line stands in for a delivery receipt, not a request trace.
        logger.LogInformation(
            "{Channel} notification to {RecipientUserId}: [{Category}] {Title} — {Body}",
            Kind,
            notification.RecipientUserId,
            notification.Category,
            notification.Title,
            notification.Body);

        return Task.FromResult(NotificationDeliveryOutcome.Delivered);
    }
}

/// <summary>The email channel's working log stand-in.</summary>
/// <param name="logger">Where the would-be delivery is recorded.</param>
public sealed class LogEmailNotificationChannel(ILogger<LogEmailNotificationChannel> logger)
    : LogNotificationChannel(NotificationChannel.Email, logger);

/// <summary>The push channel's working log stand-in.</summary>
/// <param name="logger">Where the would-be delivery is recorded.</param>
public sealed class LogPushNotificationChannel(ILogger<LogPushNotificationChannel> logger)
    : LogNotificationChannel(NotificationChannel.Push, logger);
