#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Quality;

public enum QualityCertificateStatus
{
    Valid,
    Revoked,
}

/// <summary>Company-scoped quality certificate with an append-only revocation state.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class QualityCertificate : Entity
{
    private QualityCertificate(Guid tenantId, Guid? storeId, Guid companyId, Guid? itemId, Guid? itemVariantId,
        string certificateNumber, string issuer, DateTimeOffset issuedAt, DateTimeOffset expiresAt, string evidence) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        CertificateNumber = certificateNumber;
        Issuer = issuer;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        Evidence = evidence;
        Status = QualityCertificateStatus.Valid;
    }

    private QualityCertificate() { }

    public Guid? ItemId { get; private set; }
    public Guid? ItemVariantId { get; private set; }
    public string CertificateNumber { get; private set; } = string.Empty;
    public string Issuer { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string Evidence { get; private set; } = string.Empty;
    public QualityCertificateStatus Status { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }

    public static QualityCertificate Issue(Guid tenantId, Guid? storeId, Guid companyId, Guid? itemId, Guid? itemVariantId,
        string certificateNumber, string issuer, DateTimeOffset issuedAt, DateTimeOffset expiresAt, string evidence)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || itemId is null && itemVariantId is null)
        {
            throw new ArgumentException("A certificate requires tenant, company and an item target.");
        }
        if (expiresAt <= issuedAt)
        {
            throw new ArgumentException("A certificate must expire after it is issued.");
        }
        Require(certificateNumber, 128, nameof(certificateNumber));
        Require(issuer, 256, nameof(issuer));
        Require(evidence, 2000, nameof(evidence));
        return new QualityCertificate(tenantId, storeId, companyId, itemId, itemVariantId, certificateNumber.Trim(), issuer.Trim(), issuedAt, expiresAt, evidence.Trim());
    }

    public void Revoke(DateTimeOffset at, string reason)
    {
        if (Status != QualityCertificateStatus.Valid)
        {
            throw new InvalidOperationException("Only a valid certificate can be revoked.");
        }
        Require(reason, 512, nameof(reason));
        Status = QualityCertificateStatus.Revoked;
        RevokedAt = at;
        RevocationReason = reason.Trim();
    }

    private static void Require(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Trim().Length > maxLength)
        {
            throw new ArgumentException($"Certificate text must be {maxLength} characters or fewer.", parameterName);
        }
    }
}
