using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.CustomerAccounts;

namespace VumaRetail.Infrastructure.Persistence.Configurations.CustomerAccounts;

/// <summary><c>customer_accounts.accounts</c> — credit limits, terms and standing. Balances live in
/// Stage 07's AR sub-ledger, never here.</summary>
internal sealed class CustomerAccountConfiguration : EntityConfiguration<CustomerAccount>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "accounts";

    protected override void ConfigureEntity(EntityTypeBuilder<CustomerAccount> builder)
    {
        builder.Property(a => a.AccountNumber).IsRequired().HasMaxLength(32);
        builder.Property(a => a.PartnerId).IsRequired();
        builder.HasMoney(a => a.CreditLimit, "credit_limit");
        builder.Property(a => a.TermsDays).IsRequired();
        builder.Property(a => a.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.HoldReason).HasMaxLength(256);

        builder.HasIndex(a => new { a.TenantId, a.AccountNumber })
            .IsUnique()
            .HasDatabaseName("ux_accounts_tenant_id_number")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(a => a.PartnerId)
            .HasDatabaseName("ix_accounts_partner_id");
    }
}

/// <summary><c>customer_accounts.account_holders</c> — named buyers on a business account.</summary>
internal sealed class AccountHolderConfiguration : EntityConfiguration<AccountHolder>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "account_holders";

    protected override void ConfigureEntity(EntityTypeBuilder<AccountHolder> builder)
    {
        builder.Property(h => h.AccountId).IsRequired();
        builder.Property(h => h.UserId).IsRequired();
        builder.Property(h => h.DisplayName).IsRequired().HasMaxLength(128);
        builder.HasMoney(h => h.ChargeLimit, "charge_limit");

        builder.HasIndex(h => h.AccountId)
            .HasDatabaseName("ix_account_holders_account_id");
    }
}

/// <summary><c>customer_accounts.terms</c> — the tenant's customer-money policy, one row.</summary>
internal sealed class CustomerFinanceTermsConfiguration : EntityConfiguration<CustomerFinanceTerms>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "terms";

    protected override void ConfigureEntity(EntityTypeBuilder<CustomerFinanceTerms> builder)
    {
        builder.Property(t => t.InterestMonthlyRate).HasColumnType("numeric(9,6)").IsRequired();
        builder.Property(t => t.SettlementDiscountRate).HasColumnType("numeric(9,6)").IsRequired();
        builder.Property(t => t.SettlementDiscountDays).IsRequired();
        builder.HasMoney(t => t.LayByAdminFee, "layby_admin_fee");
        builder.Property(t => t.LayByMaxTermMonths).IsRequired();
        builder.Property(t => t.StaleBalanceMinutes).IsRequired();

        builder.HasIndex(t => t.TenantId)
            .IsUnique()
            .HasDatabaseName("ux_terms_tenant_id")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>customer_accounts.layby_agreements</c> — frozen price, payment plan, held stock.</summary>
internal sealed class LayByAgreementConfiguration : EntityConfiguration<LayByAgreement>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "layby_agreements";

    protected override void ConfigureEntity(EntityTypeBuilder<LayByAgreement> builder)
    {
        builder.Property(a => a.AgreementNumber).IsRequired().HasMaxLength(32);
        builder.Property(a => a.PartnerId).IsRequired();
        builder.HasMoney(a => a.AgreedTotal, "agreed_total");
        builder.HasMoney(a => a.DepositRequired, "deposit_required");
        builder.HasMoney(a => a.PaidToDate, "paid_to_date");
        builder.Property(a => a.TermMonths).IsRequired();
        builder.Property(a => a.ExpiryDate).IsRequired();
        builder.HasMoney(a => a.AdminFee, "admin_fee");
        builder.Property(a => a.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.CompletedAt);
        builder.Property(a => a.CancelledAt);
        builder.Property(a => a.CancelRefundAmount).HasColumnType("numeric(18,4)");
        builder.Property(a => a.CancelRefundCurrency).HasMaxLength(3).IsFixedLength();
        builder.Property(a => a.CancelFeeAmount).HasColumnType("numeric(18,4)");
        builder.Property(a => a.CancelFeeCurrency).HasMaxLength(3).IsFixedLength();
        builder.Ignore(a => a.CancelRefund);
        builder.Ignore(a => a.CancelFee);

        builder.HasMany(a => a.Lines)
            .WithOne()
            .HasForeignKey(line => line.AgreementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(a => a.Instalments)
            .WithOne()
            .HasForeignKey(instalment => instalment.AgreementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Instalments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => new { a.TenantId, a.AgreementNumber })
            .IsUnique()
            .HasDatabaseName("ux_layby_agreements_tenant_id_number")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(a => new { a.TenantId, a.Status, a.ExpiryDate })
            .HasDatabaseName("ix_layby_agreements_tenant_id_status_expiry");
    }
}

/// <summary><c>customer_accounts.layby_agreement_lines</c> — frozen price, tax and pack snapshots.</summary>
internal sealed class LayByAgreementLineConfiguration : EntityConfiguration<LayByAgreementLine>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "layby_agreement_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<LayByAgreementLine> builder)
    {
        builder.Property(line => line.AgreementId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);

        builder.Property(line => line.QuantityValue)
            .HasColumnName("quantity_value")
            .HasColumnType(ValueObjectMapping.QuantityColumnType)
            .IsRequired();
        builder.Property(line => line.QuantityUom)
            .HasColumnName("quantity_uom")
            .HasMaxLength(16)
            .IsRequired();
        builder.HasMoney(line => line.AgreedUnitPrice, "agreed_unit_price");
        builder.HasMoney(line => line.DiscountAmount, "discount_amount");
        builder.HasMoney(line => line.TaxAmount, "tax_amount");
        builder.HasMoney(line => line.Net, "net");

        builder.Property(line => line.PackSizeDescription).IsRequired().HasMaxLength(128);
        builder.Property(line => line.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(line => line.PriceListId);

        builder.HasIndex(line => line.AgreementId)
            .HasDatabaseName("ix_layby_agreement_lines_agreement_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_layby_agreement_lines_quantity_positive", "quantity_value > 0");
            table.HasCheckConstraint("ck_layby_agreement_lines_pack_size_required", "pack_size_description <> ''");
        });
    }
}

/// <summary><c>customer_accounts.layby_instalments</c> — append-only payments. No update path.</summary>
internal sealed class LayByInstalmentConfiguration : EntityConfiguration<LayByInstalment>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "layby_instalments";

    protected override void ConfigureEntity(EntityTypeBuilder<LayByInstalment> builder)
    {
        builder.Property(i => i.AgreementId).IsRequired();
        builder.Property(i => i.Sequence).IsRequired();
        builder.HasMoney(i => i.Amount, "amount");
        builder.Property(i => i.ReceiptReference).IsRequired().HasMaxLength(64);
        builder.Property(i => i.PaidAt).IsRequired();
        builder.Property(i => i.Channel).IsRequired().HasMaxLength(64);
        builder.Property(i => i.TakenOffline).IsRequired();

        builder.HasIndex(i => new { i.AgreementId, i.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_layby_instalments_agreement_id_sequence");

        builder.HasIndex(i => i.AgreementId)
            .HasDatabaseName("ix_layby_instalments_agreement_id");
    }
}
