#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Reporting;

public enum ReportExportStatus { Queued, Completed, Failed }

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ReportExport : Entity
{
    private ReportExport(Guid tenantId, Guid? storeId, Guid companyId, Guid operationId, string reportCode,
        DateTimeOffset requestedAtUtc) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        OperationId = operationId;
        ReportCode = reportCode.Trim().ToUpperInvariant();
        RequestedAtUtc = requestedAtUtc;
        Status = ReportExportStatus.Queued;
    }

    private ReportExport() { }
    public Guid OperationId { get; private set; }
    public string ReportCode { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public ReportExportStatus Status { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }
    public string? ArtifactReference { get; private set; }

    public static ReportExport Queue(Guid tenantId, Guid? storeId, Guid companyId, Guid operationId,
        string reportCode, DateTimeOffset requestedAtUtc)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || operationId == Guid.Empty)
        {
            throw new ArgumentException("Export tenant, company and operation identities are required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reportCode);
        return new ReportExport(tenantId, storeId, companyId, operationId, reportCode, requestedAtUtc);
    }

    public void Complete(DateTimeOffset completedAtUtc, string artifactReference)
    {
        if (Status != ReportExportStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued export can complete.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactReference);
        Status = ReportExportStatus.Completed;
        CompletedAtUtc = completedAtUtc;
        ArtifactReference = artifactReference.Trim();
    }

    public void Fail(string reason)
    {
        if (Status != ReportExportStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued export can fail.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = ReportExportStatus.Failed;
        FailureReason = reason.Trim();
    }
}
