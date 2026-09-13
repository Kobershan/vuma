#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Ecommerce;

/// <summary>A tenant-owned storefront identity used to scope public catalogue requests.</summary>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class ChannelConnection : Entity
{
    private ChannelConnection(Guid tenantId, Guid companyId, string code, string host)
        : base(tenantId)
    {
        AssignCompany(companyId);
        Code = code.Trim();
        Host = host.Trim().ToLowerInvariant();
        Status = ChannelConnectionStatus.Draft;
    }

    private ChannelConnection() { }

    public Guid CompanyIdValue => CompanyId ?? Guid.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Host { get; private set; } = string.Empty;
    public ChannelConnectionStatus Status { get; private set; }

    public static ChannelConnection Register(Guid tenantId, Guid companyId, string code, string host)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("A channel requires tenant and company identities.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (code.Trim().Length > 64 || host.Trim().Length > 255)
        {
            throw new ArgumentException("Channel code or host exceeds its maximum length.");
        }
        return new ChannelConnection(tenantId, companyId, code, host);
    }

    public void Activate()
    {
        if (Status == ChannelConnectionStatus.Ended)
        {
            throw new InvalidOperationException("An ended channel cannot be activated.");
        }
        Status = ChannelConnectionStatus.Active;
    }

    public void Suspend()
    {
        if (Status != ChannelConnectionStatus.Active)
        {
            throw new InvalidOperationException("Only an active channel can be suspended.");
        }
        Status = ChannelConnectionStatus.Suspended;
    }
}
