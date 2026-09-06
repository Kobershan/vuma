using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Workflow.Notifications;

/// <summary>The caller's own notifications, newest first.</summary>
/// <param name="UnreadOnly">Restrict to unread, in-app notifications.</param>
/// <param name="Limit">How many to return. Defaults to 50, capped at 200 (<c>API_STANDARDS.md</c> §8).</param>
public sealed record ListMyNotificationsQuery(bool UnreadOnly = false, int Limit = 50)
    : IQuery<IReadOnlyList<NotificationSummary>>;

/// <summary>Lists the caller's own notifications.</summary>
/// <param name="notifications">Where notifications are recorded.</param>
/// <param name="principal">Who is asking.</param>
public sealed class ListMyNotificationsQueryHandler(
    INotificationRepository notifications,
    IPrincipalAccessor principal) : IQueryHandler<ListMyNotificationsQuery, IReadOnlyList<NotificationSummary>>
{
    private const int MaxLimit = 200;

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationSummary>> HandleAsync(
        ListMyNotificationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Guid userId = WorkflowPrincipal.RequireUserId(principal.Principal);
        int limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, MaxLimit);

        IReadOnlyList<Notification> found = await notifications
            .ListForRecipientAsync(userId, query.UnreadOnly, limit, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. found.Select(notification => new NotificationSummary(
                notification.Id,
                notification.Category,
                notification.Title,
                notification.Body,
                notification.Severity,
                notification.Channel,
                notification.RelatedModule,
                notification.RelatedEntityType,
                notification.RelatedEntityId,
                notification.ActionUrl,
                notification.DeliveryStatus,
                notification.IsRead,
                notification.CreatedAt)),
        ];
    }
}

/// <summary>How many unread in-app notifications the caller has — the inbox badge count.</summary>
public sealed record GetUnreadNotificationCountQuery : IQuery<int>;

/// <summary>Counts the caller's own unread notifications.</summary>
/// <param name="notifications">Where notifications are recorded.</param>
/// <param name="principal">Who is asking.</param>
public sealed class GetUnreadNotificationCountQueryHandler(
    INotificationRepository notifications,
    IPrincipalAccessor principal) : IQueryHandler<GetUnreadNotificationCountQuery, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(
        GetUnreadNotificationCountQuery query,
        CancellationToken cancellationToken = default)
        => notifications.CountUnreadAsync(WorkflowPrincipal.RequireUserId(principal.Principal), cancellationToken);
}
