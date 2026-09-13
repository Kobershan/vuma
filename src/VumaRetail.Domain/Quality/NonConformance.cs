#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Quality;

public enum NonConformanceStatus { Open, CorrectiveAction, Closed }
public enum NonConformanceSeverity { Minor, Major, Critical }

/// <summary>Traceable quality failure requiring investigation and corrective action.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class NonConformance : Entity, IImmutableRecord
{
    private NonConformance(Guid tenantId, Guid? storeId, Guid companyId, Guid operationId, Guid holdId,
        NonConformanceSeverity severity, string description, DateTimeOffset openedAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        OperationId = operationId;
        HoldId = holdId;
        Severity = severity;
        Description = description;
        OpenedAt = openedAt;
        Status = NonConformanceStatus.Open;
    }

    private NonConformance() { }
    public Guid OperationId { get; private set; }
    public Guid HoldId { get; private set; }
    public Guid? CorrectiveActionOperationId { get; private set; }
    public Guid? ClosureOperationId { get; private set; }
    public NonConformanceSeverity Severity { get; private set; }
    public NonConformanceStatus Status { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? Resolution { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public static NonConformance Open(Guid tenantId, Guid? storeId, Guid companyId, Guid operationId,
        Guid holdId, NonConformanceSeverity severity, string description, DateTimeOffset openedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || operationId == Guid.Empty || holdId == Guid.Empty)
        {
            throw new ArgumentException("A non-conformance requires tenant, company, operation and hold identities.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (description.Trim().Length > 2000)
        {
            throw new ArgumentException("Description must be 2000 characters or fewer.", nameof(description));
        }
        return new NonConformance(tenantId, storeId, companyId, operationId, holdId, severity, description.Trim(), openedAt);
    }

    public void StartCorrectiveAction(Guid operationId)
    {
        if (Status != NonConformanceStatus.Open)
        {
            throw new InvalidOperationException("Only an open non-conformance can start corrective action.");
        }
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("Corrective-action operation is required.", nameof(operationId));
        }
        CorrectiveActionOperationId = operationId;
        Status = NonConformanceStatus.CorrectiveAction;
    }

    public void Close(Guid operationId, DateTimeOffset at, string resolution)
    {
        if (Status != NonConformanceStatus.CorrectiveAction)
        {
            throw new InvalidOperationException("Corrective action must be started before closure.");
        }
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("Closure operation is required.", nameof(operationId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution);
        ClosureOperationId = operationId;
        Status = NonConformanceStatus.Closed;
        Resolution = resolution.Trim();
        ClosedAt = at;
    }
}
