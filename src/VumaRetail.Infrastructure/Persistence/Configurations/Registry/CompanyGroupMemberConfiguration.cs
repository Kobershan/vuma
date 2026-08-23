using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Registry;

/// <summary><c>registry.company_group_members</c>.</summary>
internal sealed class CompanyGroupMemberConfiguration : EntityConfiguration<CompanyGroupMember>
{
    protected override string Schema => Schemas.Registry;

    protected override string TableName => "company_group_members";

    protected override void ConfigureEntity(EntityTypeBuilder<CompanyGroupMember> builder)
    {
        builder.Property(member => member.CompanyGroupId)
            .IsRequired();

        builder.Property(member => member.MemberCompanyId)
            .IsRequired();

        // A company appears in a group at most once. Unique among live rows only, so re-adding a
        // company after a soft-deleted membership does not collide with it (§7 rule 8).
        builder.HasIndex(member => new { member.CompanyGroupId, member.MemberCompanyId })
            .IsUnique()
            .HasDatabaseName("ux_company_group_members_group_id_company_id")
            .HasFilter("deleted_at IS NULL");
    }
}
