#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Quality;

/// <summary>Immutable evidence from one inspection of a quarantined quantity.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class InspectionResult : Entity, IImmutableRecord
{
    private InspectionResult(Guid tenantId, Guid? storeId, Guid companyId, Guid holdId, Guid? planId, int planVersion, Guid operationId,
        bool passed, int sampleSize, string evidence, DateTimeOffset inspectedAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        HoldId = holdId;
        PlanId = planId;
        PlanVersion = planVersion;
        OperationId = operationId;
        Passed = passed;
        SampleSize = sampleSize;
        Evidence = evidence;
        InspectedAt = inspectedAt;
    }
    private InspectionResult() { }
    public Guid HoldId { get; private set; }
    public Guid? PlanId { get; private set; }
    public int PlanVersion { get; private set; }
    public Guid OperationId { get; private set; }
    public bool Passed { get; private set; }
    public int SampleSize { get; private set; }
    public string Evidence { get; private set; } = string.Empty;
    public DateTimeOffset InspectedAt { get; private set; }

    public static InspectionResult Record(Guid tenantId, Guid? storeId, Guid companyId, Guid holdId, Guid? planId,
        int planVersion, Guid operationId, bool passed, int sampleSize, string evidence, DateTimeOffset inspectedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || holdId == Guid.Empty || operationId == Guid.Empty
            || (planId is not null && planVersion <= 0))
        {
            throw new ArgumentException("An inspection requires tenant, company, hold and operation identities.");
        }
        if (sampleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleSize), "Sample size must be positive.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        if (evidence.Trim().Length > 2000)
        {
            throw new ArgumentException("Inspection evidence must be 2000 characters or fewer.", nameof(evidence));
        }
        return new InspectionResult(tenantId, storeId, companyId, holdId, planId, planVersion, operationId, passed, sampleSize, evidence.Trim(), inspectedAt);
    }
}
