using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Workflow.Notifications;

/// <summary>Marks one of the caller's own notifications read.</summary>
/// <param name="NotificationId">The notification.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand;

/// <summary>Marks it read, refusing to touch anybody else's notification.</summary>
/// <param name="notifications">Where notifications are recorded.</param>
/// <param name="principal">Who is acting.</param>
/// <param name="clock">The only source of time.</param>
public sealed class MarkNotificationReadCommandHandler(
    INotificationRepository notifications,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<MarkNotificationReadCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        MarkNotificationReadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Notification notification = await notifications
            .FindAsync(command.NotificationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotificationNotFoundException(command.NotificationId);

        // Found is not the same as yours. The tenant filter already keeps this to one tenant's rows;
        // this is the second boundary — one recipient's own inbox — and a mismatch here is answered
        // identically to "not found" rather than a 403, for the same reason a cross-tenant lookup is
        // (docs/API_STANDARDS.md §4): distinguishing them would make every id an existence oracle
        // across other people's notifications.
        if (notification.RecipientUserId != WorkflowPrincipal.RequireUserId(principal.Principal))
        {
            throw new NotificationNotFoundException(command.NotificationId);
        }

        notification.MarkRead(clock.UtcNow);

        return Unit.Value;
    }
}
