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

/// <summary><c>customer_accounts.stokvel_groups</c> — saving circles with a cycle and a constitution.</summary>
internal sealed class StokvelGroupConfiguration : EntityConfiguration<StokvelGroup>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "stokvel_groups";

    protected override void ConfigureEntity(EntityTypeBuilder<StokvelGroup> builder)
    {
        builder.Property(g => g.GroupNumber).IsRequired().HasMaxLength(32);
        builder.Property(g => g.Name).IsRequired().HasMaxLength(128);
        builder.Property(g => g.Type).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(g => g.Constitution).IsRequired().HasMaxLength(2000);
        builder.Property(g => g.CycleStart).IsRequired();
        builder.Property(g => g.CycleEnd).IsRequired();
        builder.Property(g => g.StoreScopeId).IsRequired();
        builder.Property(g => g.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Ignore(g => g.MembersSeeAll);

        builder.HasIndex(g => new { g.TenantId, g.GroupNumber })
            .IsUnique()
            .HasDatabaseName("ux_stokvel_groups_tenant_id_number")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(g => new { g.TenantId, g.Status })
            .HasDatabaseName("ix_stokvel_groups_tenant_id_status");
    }
}

/// <summary><c>customer_accounts.stokvel_members</c> — roles, join/leave instants, obligations.</summary>
internal sealed class StokvelMemberConfiguration : EntityConfiguration<StokvelMember>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "stokvel_members";

    protected override void ConfigureEntity(EntityTypeBuilder<StokvelMember> builder)
    {
        builder.Property(m => m.GroupId).IsRequired();
        builder.Property(m => m.PartnerId).IsRequired();
        builder.Property(m => m.Role).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.JoinedAt).IsRequired();
        builder.Property(m => m.LeftAt);
        builder.HasMoney(m => m.ContributionObligation, "contribution_obligation");

        builder.HasIndex(m => m.GroupId)
            .HasDatabaseName("ix_stokvel_members_group_id");

        builder.HasIndex(m => new { m.GroupId, m.PartnerId })
            .HasDatabaseName("ix_stokvel_members_group_id_partner_id");
    }
}

/// <summary><c>customer_accounts.stokvel_contributions</c> — append-only member receipts. No update path.</summary>
internal sealed class StokvelContributionConfiguration : EntityConfiguration<StokvelContribution>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "stokvel_contributions";

    protected override void ConfigureEntity(EntityTypeBuilder<StokvelContribution> builder)
    {
        builder.Property(c => c.GroupId).IsRequired();
        builder.Property(c => c.MemberId).IsRequired();
        builder.HasMoney(c => c.Amount, "amount");
        builder.Property(c => c.ReceiptReference).IsRequired().HasMaxLength(64);
        builder.Property(c => c.PaidAt).IsRequired();
        builder.Property(c => c.Channel).IsRequired().HasMaxLength(64);
        builder.Property(c => c.TakenOffline).IsRequired();

        // Offline replay idempotency at the storage layer: the same receipt for the same member
        // is one contribution, however many times the terminal retries it.
        builder.HasIndex(c => new { c.MemberId, c.ReceiptReference })
            .IsUnique()
            .HasDatabaseName("ux_stokvel_contributions_member_id_receipt");

        builder.HasIndex(c => c.GroupId)
            .HasDatabaseName("ix_stokvel_contributions_group_id");

        builder.HasIndex(c => c.MemberId)
            .HasDatabaseName("ix_stokvel_contributions_member_id");
    }
}

/// <summary><c>customer_accounts.stokvel_benefit_allocations</c> — append-only shares. No update path.</summary>
internal sealed class StokvelBenefitAllocationConfiguration : EntityConfiguration<StokvelBenefitAllocation>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "stokvel_benefit_allocations";

    protected override void ConfigureEntity(EntityTypeBuilder<StokvelBenefitAllocation> builder)
    {
        builder.Property(b => b.GroupId).IsRequired();
        builder.Property(b => b.MemberId).IsRequired();
        builder.HasMoney(b => b.Amount, "amount");
        builder.Property(b => b.Basis).IsRequired().HasMaxLength(256);
        builder.Property(b => b.AllocatedAt).IsRequired();

        builder.HasIndex(b => b.GroupId)
            .HasDatabaseName("ix_stokvel_benefit_allocations_group_id");

        builder.HasIndex(b => b.MemberId)
            .HasDatabaseName("ix_stokvel_benefit_allocations_member_id");
    }
}

/// <summary><c>customer_accounts.stokvel_payouts</c> — requested, approved, settled exactly once.</summary>
internal sealed class StokvelPayoutConfiguration : EntityConfiguration<StokvelPayout>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "stokvel_payouts";

    protected override void ConfigureEntity(EntityTypeBuilder<StokvelPayout> builder)
    {
        builder.Property(p => p.GroupId).IsRequired();
        builder.Property(p => p.MemberId).IsRequired();
        builder.Property(p => p.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.HasMoney(p => p.Amount, "amount");
        builder.Property(p => p.HamperBasketId);
        builder.Property(p => p.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.RequestedAt).IsRequired();
        builder.Property(p => p.ApprovedAt);
        builder.Property(p => p.SettledAt);
        builder.Property(p => p.SaleId);

        builder.HasIndex(p => p.GroupId)
            .HasDatabaseName("ix_stokvel_payouts_group_id");

        builder.HasIndex(p => p.MemberId)
            .HasDatabaseName("ix_stokvel_payouts_member_id");
    }
}

/// <summary><c>customer_accounts.hamper_baskets</c> — frozen group prices with a season and a location.</summary>
internal sealed class HamperBasketConfiguration : EntityConfiguration<HamperBasket>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "hamper_baskets";

    protected override void ConfigureEntity(EntityTypeBuilder<HamperBasket> builder)
    {
        builder.Property(b => b.GroupId).IsRequired();
        builder.Property(b => b.Name).IsRequired().HasMaxLength(128);
        builder.HasMoney(b => b.GroupPrice, "group_price");
        builder.Property(b => b.ValidFrom).IsRequired();
        builder.Property(b => b.ValidTo).IsRequired();
        builder.Property(b => b.LocationId).IsRequired();

        builder.HasMany(b => b.Lines)
            .WithOne()
            .HasForeignKey(line => line.BasketId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(b => b.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(b => b.GroupId)
            .HasDatabaseName("ix_hamper_baskets_group_id");
    }
}

/// <summary><c>customer_accounts.hamper_basket_lines</c> — frozen contents with substitution rules.</summary>
internal sealed class HamperBasketLineConfiguration : EntityConfiguration<HamperBasketLine>
{
    protected override string Schema => Schemas.CustomerAccounts;
    protected override string TableName => "hamper_basket_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<HamperBasketLine> builder)
    {
        builder.Property(line => line.BasketId).IsRequired();
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
        builder.Property(line => line.SubstitutionItemId);
        builder.Property(line => line.SubstitutionItemVariantId);

        builder.HasIndex(line => line.BasketId)
            .HasDatabaseName("ix_hamper_basket_lines_basket_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_hamper_basket_lines_quantity_positive", "quantity_value > 0");
        });
    }
}
