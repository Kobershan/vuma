#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Reporting;

public enum ReportDefinitionStatus { Draft, Published, Retired }

[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class ReportDefinition : Entity
{
    private ReportDefinition(Guid tenantId, Guid? storeId, string code, string name) : base(tenantId, storeId)
    { Code = code.Trim().ToUpperInvariant(); Name = name.Trim(); Status = ReportDefinitionStatus.Draft; }
    private ReportDefinition() { }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public ReportDefinitionStatus Status { get; private set; }
    public static ReportDefinition Create(Guid tenantId, Guid? storeId, string code, string name)
    { if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required."); ArgumentException.ThrowIfNullOrWhiteSpace(code); ArgumentException.ThrowIfNullOrWhiteSpace(name); return new(tenantId, storeId, code, name); }
    public void Publish() { if (Status != ReportDefinitionStatus.Draft) throw new InvalidOperationException("Only a draft report can be published."); Status = ReportDefinitionStatus.Published; }
    public void Retire() { if (Status != ReportDefinitionStatus.Published) throw new InvalidOperationException("Only a published report can be retired."); Status = ReportDefinitionStatus.Retired; }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ProjectionCheckpoint : Entity
{
    private ProjectionCheckpoint(Guid tenantId, Guid? storeId, Guid companyId, string source, long generation, string cursor) : base(tenantId, storeId)
    { AssignCompany(companyId); Source = source.Trim(); Generation = generation; Cursor = cursor; }
    private ProjectionCheckpoint() { }
    public string Source { get; private set; } = string.Empty;
    public long Generation { get; private set; }
    public string Cursor { get; private set; } = string.Empty;
    public static ProjectionCheckpoint Create(Guid tenantId, Guid? storeId, Guid companyId, string source, long generation = 1, string cursor = "")
    { if (tenantId == Guid.Empty || companyId == Guid.Empty || generation <= 0) throw new ArgumentException("Checkpoint identity is invalid."); ArgumentException.ThrowIfNullOrWhiteSpace(source); return new(tenantId, storeId, companyId, source, generation, cursor); }
    public void Advance(long generation, string cursor)
    { if (generation < Generation) throw new InvalidOperationException("A projection checkpoint cannot move to an older generation."); if (generation == Generation && string.CompareOrdinal(cursor, Cursor) <= 0) return; Generation = generation; Cursor = cursor; }
}

public sealed record ReportFreshness(string Contributor, DateTimeOffset LastSyncedAtUtc, bool IsStale);

public sealed record DashboardSnapshot(Guid CompanyId, DateOnly BusinessDate, DateTimeOffset AsAtUtc,
    IReadOnlyList<ReportFreshness> Contributors, IReadOnlyDictionary<string, decimal> Measures)
{
    public bool IsLive => Contributors.All(x => !x.IsStale);
}
