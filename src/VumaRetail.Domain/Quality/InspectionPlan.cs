#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Quality;

public enum InspectionPlanStatus
{
    Draft,
    Published,
    Retired,
}

/// <summary>Versioned inspection requirements for one item or variant.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class InspectionPlan : Entity
{
    private InspectionPlan(Guid tenantId, Guid? storeId, Guid companyId, Guid? itemId, Guid? itemVariantId,
        int version, string name, int sampleSize, string acceptanceCriteria) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Version = version;
        Name = name;
        SampleSize = sampleSize;
        AcceptanceCriteria = acceptanceCriteria;
        Status = InspectionPlanStatus.Draft;
    }

    private InspectionPlan() { }

    public Guid? ItemId { get; private set; }
    public Guid? ItemVariantId { get; private set; }
    public int Version { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SampleSize { get; private set; }
    public string AcceptanceCriteria { get; private set; } = string.Empty;
    public InspectionPlanStatus Status { get; private set; }

    public static InspectionPlan Create(Guid tenantId, Guid? storeId, Guid companyId, Guid? itemId, Guid? itemVariantId,
        int version, string name, int sampleSize, string acceptanceCriteria)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || version <= 0 || sampleSize <= 0)
        {
            throw new ArgumentException("An inspection plan requires tenant, company, positive version and sample size.");
        }
        if (itemId is null && itemVariantId is null)
        {
            throw new ArgumentException("An inspection plan must target an item or variant.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptanceCriteria);
        if (name.Trim().Length > 256 || acceptanceCriteria.Trim().Length > 2000)
        {
            throw new ArgumentException("Inspection plan text exceeds its maximum length.");
        }
        return new InspectionPlan(tenantId, storeId, companyId, itemId, itemVariantId, version,
            name.Trim(), sampleSize, acceptanceCriteria.Trim());
    }

    public void Publish()
    {
        if (Status != InspectionPlanStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft inspection plan can be published.");
        }
        Status = InspectionPlanStatus.Published;
    }

    public void Retire()
    {
        if (Status != InspectionPlanStatus.Published)
        {
            throw new InvalidOperationException("Only a published inspection plan can be retired.");
        }
        Status = InspectionPlanStatus.Retired;
    }
}
