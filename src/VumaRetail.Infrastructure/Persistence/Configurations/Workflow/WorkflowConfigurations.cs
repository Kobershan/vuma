using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Workflow;
using VumaRetail.Infrastructure.Persistence.Configurations;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Workflow;

/// <summary><c>workflow.approval_policies</c> — configured gates (ADR-019).</summary>
internal sealed class ApprovalPolicyConfiguration : EntityConfiguration<ApprovalPolicy>
{
    protected override string Schema => Schemas.Workflow;

    protected override string TableName => "approval_policies";

    protected override void ConfigureEntity(EntityTypeBuilder<ApprovalPolicy> builder)
    {
        builder.Property(policy => policy.Module).IsRequired().HasMaxLength(64);
        builder.Property(policy => policy.EntityType).IsRequired().HasMaxLength(64);
        builder.Property(policy => policy.Action).IsRequired().HasMaxLength(64);

        // Two flat nullable columns rather than ValueObjectMapping.HasMoney's complex-type form:
        // EF Core 9's relational providers do not yet support an optional complex property
        // (dotnet/efcore#31376). ThresholdAmount reassembles the two on the way out.
        builder.Property<decimal?>("ThresholdAmountValue")
            .HasColumnName("threshold_amount")
            .HasColumnType(ValueObjectMapping.MoneyColumnType);

        builder.Property<string?>("ThresholdAmountCurrency")
            .HasColumnName("threshold_currency")
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(policy => policy.RequiredPermission)
            .IsRequired()
            .HasMaxLength(Domain.Identity.PermissionKey.MaxLength);

        builder.Property(policy => policy.MinApprovals).IsRequired();
        builder.Property(policy => policy.AllowSelfApproval).IsRequired();
        builder.Property(policy => policy.IsActive).IsRequired();

        // At most one active policy per gated action. Filtered to IsActive so a deactivated policy
        // never blocks a replacement being defined for the same key.
        builder.HasIndex(policy => new { policy.TenantId, policy.Module, policy.EntityType, policy.Action })
            .IsUnique()
            .HasDatabaseName("ux_approval_policies_tenant_key")
            .HasFilter("is_active AND deleted_at IS NULL");
    }
}

/// <summary><c>workflow.approval_requests</c> — pending and decided gates.</summary>
internal sealed class ApprovalRequestConfiguration : EntityConfiguration<ApprovalRequest>
{
    protected override string Schema => Schemas.Workflow;

    protected override string TableName => "approval_requests";

    protected override void ConfigureEntity(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.Property(request => request.PolicyId).IsRequired();
        builder.Property(request => request.Module).IsRequired().HasMaxLength(64);
        builder.Property(request => request.EntityType).IsRequired().HasMaxLength(64);
        builder.Property(request => request.Action).IsRequired().HasMaxLength(64);
        builder.Property(request => request.SubjectEntityId).IsRequired();

        // As ApprovalPolicy.ThresholdAmount above: two flat nullable columns, because EF Core 9 does
        // not yet support an optional complex property on a relational provider (dotnet/efcore#31376).
        builder.Property<decimal?>("AmountValue")
            .HasColumnName("amount_amount")
            .HasColumnType(ValueObjectMapping.MoneyColumnType);

        builder.Property<string?>("AmountCurrency")
            .HasColumnName("amount_currency")
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(request => request.RequestedBy).IsRequired().HasMaxLength(128);
        builder.Property(request => request.RequestedAt).IsRequired();
        builder.Property(request => request.Reason).HasMaxLength(2048);

        builder.Property(request => request.RequiredPermission)
            .IsRequired()
            .HasMaxLength(Domain.Identity.PermissionKey.MaxLength);

        builder.Property(request => request.MinApprovals).IsRequired();
        builder.Property(request => request.AllowSelfApproval).IsRequired();
        builder.Property(request => request.ApprovalCount).IsRequired();
        builder.Property(request => request.RejectionCount).IsRequired();

        builder.Property(request => request.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(request => request.DecidedAt);

        // The unified inbox's hot query: what is pending, in this tenant (and often, this store).
        // Partial, because a decided request is not read by the inbox again.
        builder.HasIndex(request => new { request.TenantId, request.StoreId, request.Status })
            .HasDatabaseName("ix_approval_requests_pending")
            .HasFilter("status = 'Pending'");
    }
}

/// <summary><c>workflow.approval_decision_entries</c> — the append-only decision history.</summary>
internal sealed class ApprovalDecisionEntryConfiguration : EntityConfiguration<ApprovalDecisionEntry>
{
    protected override string Schema => Schemas.Workflow;

    protected override string TableName => "approval_decision_entries";

    protected override void ConfigureEntity(EntityTypeBuilder<ApprovalDecisionEntry> builder)
    {
        builder.Property(entry => entry.ApprovalRequestId).IsRequired();
        builder.Property(entry => entry.DecidedBy).IsRequired().HasMaxLength(128);

        builder.Property(entry => entry.Outcome)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(entry => entry.Comment).HasMaxLength(ApprovalDecisionEntry.MaxCommentLength);
        builder.Property(entry => entry.DecidedAt).IsRequired();

        builder.HasIndex(entry => entry.ApprovalRequestId)
            .HasDatabaseName("ix_approval_decision_entries_request");

        // Enforces the one-decision-per-person guard at the database as well as in the engine — two
        // application instances racing to decide the same request as the same principal must not both
        // land, whatever the check-then-act window looked like.
        builder.HasIndex(entry => new { entry.ApprovalRequestId, entry.DecidedBy })
            .IsUnique()
            .HasDatabaseName("ux_approval_decision_entries_request_decider");
    }
}

/// <summary><c>workflow.notifications</c> — one message to one recipient on one channel.</summary>
internal sealed class NotificationConfiguration : EntityConfiguration<Notification>
{
    protected override string Schema => Schemas.Workflow;

    protected override string TableName => "notifications";

    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(notification => notification.RecipientUserId).IsRequired();
        builder.Property(notification => notification.Category).IsRequired().HasMaxLength(128);
        builder.Property(notification => notification.Title).IsRequired().HasMaxLength(Notification.MaxTitleLength);
        builder.Property(notification => notification.Body).IsRequired().HasMaxLength(Notification.MaxBodyLength);

        builder.Property(notification => notification.Severity)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(notification => notification.Channel)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(notification => notification.RelatedModule).HasMaxLength(64);
        builder.Property(notification => notification.RelatedEntityType).HasMaxLength(64);
        builder.Property(notification => notification.RelatedEntityId);
        builder.Property(notification => notification.ActionUrl).HasMaxLength(512);

        builder.Property(notification => notification.DeliveryStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(notification => notification.SentAt);
        builder.Property(notification => notification.DeliveryError).HasMaxLength(Notification.MaxBodyLength);
        builder.Property(notification => notification.IsRead).IsRequired();
        builder.Property(notification => notification.ReadAt);

        // A recipient's own inbox, newest first — the query every list and badge-count call makes.
        // Partial on unread so the badge count (the hottest of the two) stays cheap as history grows.
        builder.HasIndex(notification => new { notification.RecipientUserId, notification.CreatedAt })
            .HasDatabaseName("ix_notifications_recipient");

        builder.HasIndex(notification => notification.RecipientUserId)
            .HasDatabaseName("ix_notifications_recipient_unread")
            .HasFilter("channel = 'InApp' AND NOT is_read");
    }
}

/// <summary><c>workflow.documents</c> — attachment and generation metadata, never the bytes themselves.</summary>
internal sealed class DocumentConfiguration : EntityConfiguration<Document>
{
    protected override string Schema => Schemas.Workflow;

    protected override string TableName => "documents";

    protected override void ConfigureEntity(EntityTypeBuilder<Document> builder)
    {
        builder.Property(document => document.Module).IsRequired().HasMaxLength(64);
        builder.Property(document => document.EntityType).IsRequired().HasMaxLength(64);
        builder.Property(document => document.EntityId).IsRequired();
        builder.Property(document => document.Kind).IsRequired().HasMaxLength(64);
        builder.Property(document => document.Title).IsRequired().HasMaxLength(Document.MaxTitleLength);
        builder.Property(document => document.ContentType).IsRequired().HasMaxLength(128);
        builder.Property(document => document.CurrentVersionNumber).IsRequired();
        builder.Property(document => document.IsGenerated).IsRequired();

        // Every document attached to one business record — the call every module's "attachments" tab
        // makes.
        builder.HasIndex(document => new { document.TenantId, document.Module, document.EntityType, document.EntityId })
            .HasDatabaseName("ix_documents_entity");
    }
}

/// <summary><c>workflow.document_versions</c> — the append-only version history.</summary>
internal sealed class DocumentVersionConfiguration : EntityConfiguration<DocumentVersion>
{
    protected override string Schema => Schemas.Workflow;

    protected override string TableName => "document_versions";

    protected override void ConfigureEntity(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.Property(version => version.DocumentId).IsRequired();
        builder.Property(version => version.VersionNumber).IsRequired();
        builder.Property(version => version.StorageKey).IsRequired().HasMaxLength(512);

        builder.Property(version => version.ContentHash)
            .IsRequired()
            .HasMaxLength(DocumentVersion.ContentHashLength);

        builder.Property(version => version.SizeBytes).IsRequired();
        builder.Property(version => version.ContentType).IsRequired().HasMaxLength(128);

        builder.Property(version => version.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(version => version.GeneratedBy).IsRequired().HasMaxLength(128);

        builder.HasIndex(version => new { version.DocumentId, version.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ux_document_versions_document_version");
    }
}
