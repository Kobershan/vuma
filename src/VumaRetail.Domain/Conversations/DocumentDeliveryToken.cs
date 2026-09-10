#pragma warning disable CS1591
#pragma warning disable IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Conversations;

/// <summary>Revocable, expiring reference to a document owned by another module.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class DocumentDeliveryToken : Entity
{
    private DocumentDeliveryToken() { }
    public DocumentDeliveryToken(Guid tenantId, Guid bindingId, string documentReference, DateTimeOffset issuedAt, TimeSpan? lifetime = null)
        : base(tenantId)
    {
        BindingId = bindingId;
        DocumentReference = documentReference;
        IssuedAt = issuedAt;
        ExpiresAt = issuedAt.Add(lifetime ?? TimeSpan.FromHours(24));
        Token = UuidV7.NewGuid().ToString("N");
    }
    public Guid BindingId { get; private set; }
    public string DocumentReference { get; private set; } = string.Empty;
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? FetchedAt { get; private set; }
    public bool IsAvailable(DateTimeOffset at) => RevokedAt is null && FetchedAt is null && at <= ExpiresAt;
    public void Revoke(DateTimeOffset at) => RevokedAt = at;
    public void AuditFetch(DateTimeOffset at) { if (!IsAvailable(at)) { throw new InvalidOperationException("Document token is expired or revoked."); } FetchedAt = at; }
}
