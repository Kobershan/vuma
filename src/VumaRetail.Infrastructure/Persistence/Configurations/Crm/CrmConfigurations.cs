using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Crm;
using VumaRetail.Infrastructure.Persistence.Configurations;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Crm;

/// <summary><c>crm.leads</c> — unqualified prospects (Stage 19).</summary>
internal sealed class LeadConfiguration : EntityConfiguration<Lead>
{
    protected override string Schema => Schemas.Crm;

    protected override string TableName => "leads";

    protected override void ConfigureEntity(EntityTypeBuilder<Lead> builder)
    {
        builder.Property(lead => lead.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(lead => lead.LastName).IsRequired().HasMaxLength(100);
        builder.Property(lead => lead.Email).IsRequired().HasMaxLength(254);
        builder.Property(lead => lead.Phone).HasMaxLength(32);
        builder.Property(lead => lead.Company).HasMaxLength(200);
        builder.Property(lead => lead.Source).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(lead => lead.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(lead => lead.AssignedTo);
        builder.Property(lead => lead.CustomerId);
        builder.Property(lead => lead.ConvertedAt);

        // Natural key: one live lead per email per store. Partial, so converted/disqualified
        // history never blocks a returning prospect from being captured again.
        builder.HasIndex(lead => new { lead.TenantId, lead.StoreId, lead.Email })
            .IsUnique()
            .HasDatabaseName("ux_crm_leads_email_store")
            .HasFilter("deleted_at IS NULL AND status <> 'Converted'");
    }
}

/// <summary><c>crm.opportunities</c> — qualified deals (Stage 19).</summary>
internal sealed class OpportunityConfiguration : EntityConfiguration<Opportunity>
{
    protected override string Schema => Schemas.Crm;

    protected override string TableName => "opportunities";

    protected override void ConfigureEntity(EntityTypeBuilder<Opportunity> builder)
    {
        builder.Property(opportunity => opportunity.Title).IsRequired().HasMaxLength(200);
        builder.Property(opportunity => opportunity.Description).HasMaxLength(2000);
        builder.HasMoney(opportunity => opportunity.ExpectedValue, "expected_value");
        builder.Property(opportunity => opportunity.Stage).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(opportunity => opportunity.Probability).IsRequired();
        builder.Property(opportunity => opportunity.CloseDate);
        builder.Property(opportunity => opportunity.LossReason).HasMaxLength(1000);
        builder.Property(opportunity => opportunity.LeadId);
        builder.Property(opportunity => opportunity.CustomerId);
        builder.Property(opportunity => opportunity.AssignedTo);

        builder.HasIndex(opportunity => new { opportunity.TenantId, opportunity.CompanyId, opportunity.CustomerId })
            .HasDatabaseName("ix_crm_opportunities_customer_id");

        builder.HasIndex(opportunity => new { opportunity.TenantId, opportunity.CompanyId, opportunity.LeadId })
            .HasDatabaseName("ix_crm_opportunities_lead_id");
    }
}

/// <summary><c>crm.activities</c> — append-only interaction log (Stage 19).</summary>
internal sealed class ActivityConfiguration : EntityConfiguration<Activity>
{
    protected override string Schema => Schemas.Crm;

    protected override string TableName => "activities";

    protected override void ConfigureEntity(EntityTypeBuilder<Activity> builder)
    {
        builder.Property(activity => activity.ActivityType).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(activity => activity.Direction).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(activity => activity.Subject).IsRequired().HasMaxLength(300);
        builder.Property(activity => activity.Body).HasMaxLength(10000);
        builder.Property(activity => activity.HappenedAt).IsRequired();
        builder.Property(activity => activity.DurationMinutes);
        builder.Property(activity => activity.LeadId);
        builder.Property(activity => activity.OpportunityId);
        builder.Property(activity => activity.CustomerId);

        builder.HasIndex(activity => new { activity.TenantId, activity.CompanyId, activity.LeadId })
            .HasDatabaseName("ix_crm_activities_lead_id");

        builder.HasIndex(activity => new { activity.TenantId, activity.CompanyId, activity.CustomerId })
            .HasDatabaseName("ix_crm_activities_customer_id");
    }
}

/// <summary><c>crm.segments</c> — customer/lead groupings (Stage 19).</summary>
internal sealed class SegmentConfiguration : EntityConfiguration<Segment>
{
    protected override string Schema => Schemas.Crm;

    protected override string TableName => "segments";

    protected override void ConfigureEntity(EntityTypeBuilder<Segment> builder)
    {
        builder.Property(segment => segment.Name).IsRequired().HasMaxLength(200);
        builder.Property(segment => segment.Description).HasMaxLength(1000);
        builder.Property(segment => segment.Kind).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(segment => segment.QueryExpression).HasMaxLength(4000);
        builder.Property(segment => segment.IsActive).IsRequired();
    }
}

/// <summary><c>crm.segment_members</c> — static membership rows (Stage 19).</summary>
internal sealed class SegmentMemberConfiguration : EntityConfiguration<SegmentMember>
{
    protected override string Schema => Schemas.Crm;

    protected override string TableName => "segment_members";

    protected override void ConfigureEntity(EntityTypeBuilder<SegmentMember> builder)
    {
        builder.Property(member => member.SegmentId).IsRequired();
        builder.Property(member => member.MemberType).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(member => member.MemberId).IsRequired();
        builder.Property(member => member.AddedBy).IsRequired().HasMaxLength(128);
        builder.Property(member => member.AddedAt).IsRequired();

        builder.HasIndex(member => new { member.TenantId, member.SegmentId, member.MemberType, member.MemberId })
            .IsUnique()
            .HasDatabaseName("ux_crm_segment_members_member");
    }
}

/// <summary><c>crm.consents</c> — per-purpose consent records (Stage 19).</summary>
internal sealed class ConsentConfiguration : EntityConfiguration<Consent>
{
    protected override string Schema => Schemas.Crm;

    protected override string TableName => "consents";

    protected override void ConfigureEntity(EntityTypeBuilder<Consent> builder)
    {
        builder.Property(consent => consent.CustomerId).IsRequired();
        builder.Property(consent => consent.Type).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(consent => consent.State).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(consent => consent.GrantedAt);
        builder.Property(consent => consent.WithdrawnAt);
        builder.Property(consent => consent.ExpiresAt);
        builder.Property(consent => consent.WithdrawalReason).HasMaxLength(1000);
        builder.Property(consent => consent.Source).HasMaxLength(300);

        // Exactly one row per purpose per customer — the POPIA specificity rule in the schema.
        builder.HasIndex(consent => new { consent.TenantId, consent.CompanyId, consent.CustomerId, consent.Type })
            .IsUnique()
            .HasDatabaseName("ux_crm_consents_customer_type");
    }
}
