using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Finance;

/// <summary><c>finance.tax_rules</c> — a rules engine, never a constant (CLAUDE.md §9).</summary>
internal sealed class TaxRuleConfiguration : EntityConfiguration<TaxRule>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "tax_rules";

    protected override void ConfigureEntity(EntityTypeBuilder<TaxRule> builder)
    {
        builder.Property(rule => rule.Code).IsRequired().HasMaxLength(32);
        builder.Property(rule => rule.Name).IsRequired().HasMaxLength(128);

        // A fraction, not a currency amount — deliberately not routed through ValueObjectMapping.HasMoney.
        builder.Property(rule => rule.Rate)
            .IsRequired()
            .HasColumnType("numeric(9,6)");

        builder.Property(rule => rule.Treatment)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(rule => rule.EffectiveFrom).IsRequired();
        builder.Property(rule => rule.EffectiveTo);
        builder.Property(rule => rule.IsActive).IsRequired();

        // TaxEngine's lookup: the code effective on a given date. Multiple rows may share a code over
        // time (a rate change is a new row, per TaxRule's own remarks), so this is not unique.
        builder.HasIndex(rule => new { rule.TenantId, rule.Code, rule.EffectiveFrom })
            .HasDatabaseName("ix_tax_rules_tenant_id_code_effective_from");
    }
}

/// <summary>
/// <c>finance.reconciliation_variance_flags</c> — the evidence the daily job leaves (ADR-016, ADR-063).
/// </summary>
internal sealed class ReconciliationVarianceFlagConfiguration : EntityConfiguration<ReconciliationVarianceFlag>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "reconciliation_variance_flags";

    protected override void ConfigureEntity(EntityTypeBuilder<ReconciliationVarianceFlag> builder)
    {
        builder.Property(flag => flag.AccountingPeriodId).IsRequired();
        builder.Property(flag => flag.AccountId).IsRequired();

        builder.Property(flag => flag.ControlAccountType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        // Balances the daily check compared, not a document amount of their own — see the ADR-016
        // remark on ReconciliationVarianceFlag for why these stay plain decimal rather than Money.
        builder.Property(flag => flag.GlBalance).IsRequired().HasColumnType("numeric(18,4)");
        builder.Property(flag => flag.SubLedgerBalance).IsRequired().HasColumnType("numeric(18,4)");
        builder.Property(flag => flag.Variance).IsRequired().HasColumnType("numeric(18,4)");

        builder.Property(flag => flag.CheckedAt).IsRequired();

        builder.HasIndex(flag => new { flag.AccountId, flag.CheckedAt })
            .HasDatabaseName("ix_reconciliation_variance_flags_account_id_checked_at");

        // "Was this checked yesterday, and was there a variance" — the daily job's own read.
        builder.HasIndex(flag => flag.Variance)
            .HasDatabaseName("ix_reconciliation_variance_flags_variance")
            .HasFilter("variance <> 0");
    }
}

/// <summary><c>finance.document_number_counters</c> — node-local numbering state (ADR-065).</summary>
internal sealed class DocumentNumberCounterConfiguration : EntityConfiguration<DocumentNumberCounter>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "document_number_counters";

    protected override void ConfigureEntity(EntityTypeBuilder<DocumentNumberCounter> builder)
    {
        builder.Property(counter => counter.Series).IsRequired().HasMaxLength(16);
        builder.Property(counter => counter.NextValue).IsRequired();

        builder.HasIndex(counter => new { counter.TenantId, counter.Series })
            .IsUnique()
            .HasDatabaseName("ux_document_number_counters_tenant_id_series");
    }
}
