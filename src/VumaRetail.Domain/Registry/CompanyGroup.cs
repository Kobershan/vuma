using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// A named set of companies — the container consolidation and group scope operate over
/// (`docs/MULTI_COMPANY.md` §1). Membership is <see cref="CompanyGroupMember"/>; nothing else about a
/// group's behaviour is built here — that is 06d.
/// </summary>
/// <remarks>Replicated <see cref="ReplicationScope.StoreToCloud"/>, the same shape as <see cref="RegistryCompany"/>.</remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.LastWriterWins)]
public sealed class CompanyGroup : Entity
{
    private CompanyGroup(Guid tenantId, string name)
        : base(tenantId)
    {
        Name = name;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private CompanyGroup()
    {
    }

    /// <summary>The group's name, for example <c>"Mkhize Spaza Group"</c>'s selling companies.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Creates a company group.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="name">The group's name.</param>
    public static CompanyGroup Create(Guid tenantId, string name)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A company group must belong to a tenant.", nameof(tenantId));
        }

        return new CompanyGroup(tenantId, Require(name, nameof(name)));
    }

    /// <summary>Renames the group.</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name) => Name = Require(name, nameof(name));

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}
