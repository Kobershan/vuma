using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Finance;

/// <summary>
/// <c>finance.posting_rules</c> — the mechanism CLAUDE.md §7 rule 12 names (ADR-016).
/// </summary>
internal sealed class PostingRuleConfiguration : EntityConfiguration<PostingRule>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "posting_rules";

    protected override void ConfigureEntity(EntityTypeBuilder<PostingRule> builder)
    {
        builder.Property(rule => rule.EventType).IsRequired().HasMaxLength(64);
        builder.Property(rule => rule.Description).IsRequired().HasMaxLength(256);
        builder.Property(rule => rule.IsActive).IsRequired();

        // Only one active rule per (tenant, event type) is meaningful (PostingRule's own remarks);
        // this index makes the ambiguity visible rather than silently picking one.
        builder.HasIndex(rule => new { rule.TenantId, rule.EventType })
            .HasDatabaseName("ix_posting_rules_tenant_id_event_type")
            .HasFilter("is_active");

        builder.HasMany(rule => rule.Lines)
            .WithOne()
            .HasForeignKey(line => line.PostingRuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(rule => rule.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>finance.posting_rule_lines</c>.</summary>
internal sealed class PostingRuleLineConfiguration : EntityConfiguration<PostingRuleLine>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "posting_rule_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<PostingRuleLine> builder)
    {
        builder.Property(line => line.PostingRuleId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.AccountId).IsRequired();

        builder.Property(line => line.Side)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(8);

        builder.Property(line => line.AmountKey).IsRequired().HasMaxLength(32);
        builder.Property(line => line.InheritDimensions).IsRequired();
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);

        builder.HasIndex(line => line.PostingRuleId)
            .HasDatabaseName("ix_posting_rule_lines_posting_rule_id");
    }
}
