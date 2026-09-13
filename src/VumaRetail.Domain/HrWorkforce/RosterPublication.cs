#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.HrWorkforce;

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class RosterPublication : Entity
{
    private RosterPublication(Guid tenantId, Guid? storeId, Guid companyId, DateTimeOffset from, DateTimeOffset to,
        int shiftCount, string snapshotHash, DateTimeOffset publishedAt) : base(tenantId, storeId)
    { AssignCompany(companyId); From = from.ToUniversalTime(); To = to.ToUniversalTime(); ShiftCount = shiftCount; SnapshotHash = snapshotHash.Trim().ToLowerInvariant(); PublishedAt = publishedAt.ToUniversalTime(); }
    private RosterPublication() { }
    public DateTimeOffset From { get; private set; }
    public DateTimeOffset To { get; private set; }
    public int ShiftCount { get; private set; }
    public string SnapshotHash { get; private set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; private set; }
    public static RosterPublication Publish(Guid tenantId, Guid? storeId, Guid companyId, DateTimeOffset from, DateTimeOffset to, int shiftCount, string snapshotHash, DateTimeOffset publishedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and company are required.");
        }
        if (to <= from)
        {
            throw new ArgumentException("Roster window must end after it starts.", nameof(to));
        }
        if (shiftCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shiftCount));
        }
        if (string.IsNullOrWhiteSpace(snapshotHash) || snapshotHash.Trim().Length != 64)
        {
            throw new ArgumentException("A SHA-256 roster hash is required.", nameof(snapshotHash));
        }
        return new RosterPublication(tenantId, storeId, companyId, from, to, shiftCount, snapshotHash, publishedAt);
    }
}
