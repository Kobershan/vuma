using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Finance;

/// <summary><c>finance.accounts</c> — the chart of accounts (ADR-016).</summary>
internal sealed class AccountConfiguration : EntityConfiguration<Account>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "accounts";

    protected override void ConfigureEntity(EntityTypeBuilder<Account> builder)
    {
        builder.Property(account => account.Code).IsRequired().HasMaxLength(32);
        builder.Property(account => account.Name).IsRequired().HasMaxLength(128);

        builder.Property(account => account.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(account => account.ControlAccountType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(account => account.ParentAccountId);

        builder.Property(account => account.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(account => account.IsActive).IsRequired();

        // Tenant-wide, per Account's own remarks — no store scoping on the chart of accounts itself.
        builder.HasIndex(account => new { account.TenantId, account.Code })
            .IsUnique()
            .HasDatabaseName("ux_accounts_tenant_id_code")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(account => account.ControlAccountType)
            .HasDatabaseName("ix_accounts_control_account_type")
            .HasFilter("control_account_type <> 'None'");
    }
}

/// <summary><c>finance.accounting_periods</c> (ADR-016).</summary>
internal sealed class AccountingPeriodConfiguration : EntityConfiguration<AccountingPeriod>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "accounting_periods";

    protected override void ConfigureEntity(EntityTypeBuilder<AccountingPeriod> builder)
    {
        builder.Property(period => period.PeriodStart).IsRequired();
        builder.Property(period => period.PeriodEnd).IsRequired();

        builder.Property(period => period.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(period => period.ClosedAt);
        builder.Property(period => period.ClosedBy).HasMaxLength(128);

        // A tenant's periods never overlap — the open-period lookup depends on that being true.
        builder.HasIndex(period => new { period.TenantId, period.PeriodStart, period.PeriodEnd })
            .HasDatabaseName("ix_accounting_periods_tenant_id_range");

        builder.HasIndex(period => period.Status)
            .HasDatabaseName("ix_accounting_periods_status")
            .HasFilter("status = 'Open'");
    }
}
