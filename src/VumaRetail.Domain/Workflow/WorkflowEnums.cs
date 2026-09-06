namespace VumaRetail.Domain.Workflow;

/// <summary>What evaluating a gated action against its policy produced.</summary>
public enum ApprovalOutcomeKind
{
    /// <summary>No policy is configured, or the proposed amount is below its threshold. Nothing to wait for.</summary>
    AutoApproved = 0,

    /// <summary>A policy applies. An <see cref="ApprovalRequest"/> now exists, awaiting decisions.</summary>
    Pending = 1,
}

/// <summary>Where an <see cref="ApprovalRequest"/> stands.</summary>
public enum ApprovalRequestStatus
{
    /// <summary>Waiting for enough approvals, or for a single rejection.</summary>
    Pending = 0,

    /// <summary><see cref="ApprovalPolicy.MinApprovals"/> approvals were recorded. The action may proceed.</summary>
    Approved = 1,

    /// <summary>One holder of the required permission said no. A veto model — one refusal is enough.</summary>
    Rejected = 2,

    /// <summary>Withdrawn by the requester, or by somebody entitled to, before it was decided.</summary>
    Cancelled = 3,
}

/// <summary>What a single decision on an <see cref="ApprovalRequest"/> said.</summary>
public enum ApprovalDecisionOutcome
{
    /// <summary>The decider approved it.</summary>
    Approved = 0,

    /// <summary>The decider rejected it. Ends the request outright (the veto model).</summary>
    Rejected = 1,
}

/// <summary>Which channel a <see cref="Notification"/> travels on.</summary>
public enum NotificationChannel
{
    /// <summary>Persisted and read through the API — the one channel this product ships without a vendor integration.</summary>
    InApp = 0,

    /// <summary>Email. Stands in on a working log channel until a real provider is configured (Deferred).</summary>
    Email = 1,

    /// <summary>Push. Stands in on a working log channel until a real provider is configured (Deferred).</summary>
    Push = 2,
}

/// <summary>How urgently a <see cref="Notification"/> should be shown.</summary>
public enum NotificationSeverity
{
    /// <summary>Informational. Most notifications.</summary>
    Info = 0,

    /// <summary>Worth a person's attention soon.</summary>
    Warning = 1,

    /// <summary>Needs attention now — an approval waiting on a person, a failed import, an expiring lease.</summary>
    Critical = 2,
}

/// <summary>Whether a channel delivery attempt succeeded.</summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Created, not yet dispatched.</summary>
    Pending = 0,

    /// <summary>The channel accepted it. For <see cref="NotificationChannel.InApp"/> this is immediate.</summary>
    Sent = 1,

    /// <summary>The channel could not send it. <see cref="Notification.DeliveryError"/> carries why.</summary>
    Failed = 2,
}

/// <summary>Where a <see cref="DocumentVersion"/>'s bytes came from.</summary>
public enum DocumentSource
{
    /// <summary>A person or another system uploaded it.</summary>
    Uploaded = 0,

    /// <summary>An <c>IDocumentRenderer</c> produced it from a template and a model.</summary>
    Generated = 1,
}
