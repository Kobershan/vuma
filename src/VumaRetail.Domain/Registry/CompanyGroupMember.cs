using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>One company's membership of one <see cref="CompanyGroup"/>.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.LastWriterWins)]
public sealed class CompanyGroupMember : Entity
{
    private CompanyGroupMember(Guid tenantId, Guid companyGroupId, Guid memberCompanyId)
        : base(tenantId)
    {
        CompanyGroupId = companyGroupId;
        MemberCompanyId = memberCompanyId;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private CompanyGroupMember()
    {
    }

    /// <summary>The group this membership belongs to.</summary>
    public Guid CompanyGroupId { get; private set; }

    /// <summary>The company that is a member. Not a foreign key — a different table in the same
    /// database, but conceptually a reference to a row that could, in principle, be deactivated
    /// independently of this membership.</summary>
    public Guid MemberCompanyId { get; private set; }

    /// <summary>Adds a company to a group.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyGroupId">The group.</param>
    /// <param name="memberCompanyId">The company joining it.</param>
    public static CompanyGroupMember Create(Guid tenantId, Guid companyGroupId, Guid memberCompanyId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A membership must belong to a tenant.", nameof(tenantId));
        }

        if (companyGroupId == Guid.Empty)
        {
            throw new ArgumentException("A membership must reference a company group.", nameof(companyGroupId));
        }

        if (memberCompanyId == Guid.Empty)
        {
            throw new ArgumentException("A membership must reference a member company.", nameof(memberCompanyId));
        }

        return new CompanyGroupMember(tenantId, companyGroupId, memberCompanyId);
    }
}
