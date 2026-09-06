using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementations of the Stage 05 workflow ports.
/// </summary>
/// <remarks>
/// None of these commit — the pipeline owns the transaction (ADR-044) — and none of them filter by
/// tenant or soft delete, because both are global query filters applied to every entity.
/// </remarks>

/// <summary>Configured gates.</summary>
/// <param name="context">The database context.</param>
public sealed class ApprovalPolicyRepository(VumaRetailDbContext context) : IApprovalPolicyRepository
{
    /// <inheritdoc />
    public Task<ApprovalPolicy?> FindActiveAsync(
        string module,
        string entityType,
        string action,
        CancellationToken cancellationToken = default)
        => context.ApprovalPolicies
            .Where(policy => policy.Module == module
                && policy.EntityType == entityType
                && policy.Action == action
                && policy.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ApprovalPolicy?> FindAsync(Guid policyId, CancellationToken cancellationToken = default)
        => context.ApprovalPolicies.FirstOrDefaultAsync(policy => policy.Id == policyId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalPolicy>> ListAsync(CancellationToken cancellationToken = default)
        => await context.ApprovalPolicies
            .OrderBy(policy => policy.Module)
            .ThenBy(policy => policy.EntityType)
            .ThenBy(policy => policy.Action)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ApprovalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        context.ApprovalPolicies.Add(policy);
    }
}

/// <summary>Pending and decided approval requests.</summary>
/// <param name="context">The database context.</param>
public sealed class ApprovalRequestRepository(VumaRetailDbContext context) : IApprovalRequestRepository
{
    /// <inheritdoc />
    public Task<ApprovalRequest?> FindAsync(Guid requestId, CancellationToken cancellationToken = default)
        => context.ApprovalRequests.FirstOrDefaultAsync(request => request.Id == requestId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
        Guid? storeId = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ApprovalRequest> query = context.ApprovalRequests
            .Where(request => request.Status == ApprovalRequestStatus.Pending);

        if (storeId is { } scoped)
        {
            query = query.Where(request => request.StoreId == scoped);
        }

        return await query
            .OrderBy(request => request.RequestedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        context.ApprovalRequests.Add(request);
    }
}

/// <summary>The append-only decision history.</summary>
/// <param name="context">The database context.</param>
public sealed class ApprovalDecisionRepository(VumaRetailDbContext context) : IApprovalDecisionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalDecisionEntry>> ListForRequestAsync(
        Guid approvalRequestId,
        CancellationToken cancellationToken = default)
        => await context.ApprovalDecisionEntries
            .Where(entry => entry.ApprovalRequestId == approvalRequestId)
            .OrderBy(entry => entry.DecidedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<bool> HasDecidedAsync(
        Guid approvalRequestId,
        string decidedBy,
        CancellationToken cancellationToken = default)
        => context.ApprovalDecisionEntries.AnyAsync(
            entry => entry.ApprovalRequestId == approvalRequestId && entry.DecidedBy == decidedBy,
            cancellationToken);

    /// <inheritdoc />
    public void Add(ApprovalDecisionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.ApprovalDecisionEntries.Add(entry);
    }
}

/// <summary>Notifications.</summary>
/// <param name="context">The database context.</param>
public sealed class NotificationRepository(VumaRetailDbContext context) : INotificationRepository
{
    /// <inheritdoc />
    public Task<Notification?> FindAsync(Guid notificationId, CancellationToken cancellationToken = default)
        => context.Notifications.FirstOrDefaultAsync(notification => notification.Id == notificationId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Notification>> ListForRecipientAsync(
        Guid recipientUserId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Notification> query = context.Notifications
            .Where(notification => notification.RecipientUserId == recipientUserId);

        if (unreadOnly)
        {
            query = query.Where(notification => notification.Channel == NotificationChannel.InApp && !notification.IsRead);
        }

        return await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default)
        => context.Notifications.CountAsync(
            notification => notification.RecipientUserId == recipientUserId
                && notification.Channel == NotificationChannel.InApp
                && !notification.IsRead,
            cancellationToken);

    /// <inheritdoc />
    public void Add(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        context.Notifications.Add(notification);
    }
}

/// <summary>Document metadata.</summary>
/// <param name="context">The database context.</param>
public sealed class DocumentRepository(VumaRetailDbContext context) : IDocumentRepository
{
    /// <inheritdoc />
    public Task<Document?> FindAsync(Guid documentId, CancellationToken cancellationToken = default)
        => context.Documents.FirstOrDefaultAsync(document => document.Id == documentId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> ListForEntityAsync(
        string module,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
        => await context.Documents
            .Where(document => document.Module == module
                && document.EntityType == entityType
                && document.EntityId == entityId)
            .OrderByDescending(document => document.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        context.Documents.Add(document);
    }
}

/// <summary>The append-only version history.</summary>
/// <param name="context">The database context.</param>
public sealed class DocumentVersionRepository(VumaRetailDbContext context) : IDocumentVersionRepository
{
    /// <inheritdoc />
    public Task<DocumentVersion?> FindAsync(
        Guid documentId,
        int versionNumber,
        CancellationToken cancellationToken = default)
        => context.DocumentVersions.FirstOrDefaultAsync(
            version => version.DocumentId == documentId && version.VersionNumber == versionNumber,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentVersion>> ListForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
        => await context.DocumentVersions
            .Where(version => version.DocumentId == documentId)
            .OrderBy(version => version.VersionNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(DocumentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        context.DocumentVersions.Add(version);
    }
}
