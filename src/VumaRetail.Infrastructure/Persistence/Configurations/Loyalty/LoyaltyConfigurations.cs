using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Loyalty;
using VumaRetail.Infrastructure.Persistence.Configurations;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Loyalty;

/// <summary><c>loyalty.members</c> — Vuma's view of loyalty members (Stage 20).</summary>
internal sealed class LoyaltyMemberConfiguration : EntityConfiguration<LoyaltyMember>
{
    protected override string Schema => Schemas.Loyalty;

    protected override string TableName => "members";

    protected override void ConfigureEntity(EntityTypeBuilder<LoyaltyMember> builder)
    {
        builder.Property(member => member.CustomerId).IsRequired();
        builder.Property(member => member.OrbitMemberId).IsRequired().HasMaxLength(200);
        builder.Property(member => member.EnrolledAt).IsRequired();
        builder.Property(member => member.TierId).HasMaxLength(200);
        builder.Property(member => member.BalanceCache).IsRequired().HasColumnType("numeric(18,4)");
        builder.Property(member => member.BalanceCacheAsAt).IsRequired();
        builder.Property(member => member.LastSyncAt);
        builder.Property(member => member.LastWebhookEventId).HasMaxLength(200);
        builder.Property(member => member.LastWebhookVersion);

        // One enrolment per customer per company.
        builder.HasIndex(member => new { member.TenantId, member.CompanyId, member.CustomerId })
            .IsUnique()
            .HasDatabaseName("ux_loyalty_members_customer");

        builder.HasIndex(member => new { member.TenantId, member.CompanyId, member.OrbitMemberId })
            .IsUnique()
            .HasDatabaseName("ux_loyalty_members_orbit");
    }
}

/// <summary><c>loyalty.transactions</c> — the Vuma-side earn/burn event log (Stage 20).</summary>
internal sealed class LoyaltyTransactionConfiguration : EntityConfiguration<LoyaltyTransaction>
{
    protected override string Schema => Schemas.Loyalty;

    protected override string TableName => "transactions";

    protected override void ConfigureEntity(EntityTypeBuilder<LoyaltyTransaction> builder)
    {
        builder.Property(transaction => transaction.CustomerId).IsRequired();
        builder.Property(transaction => transaction.TransactionType).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(transaction => transaction.Amount).IsRequired().HasColumnType("numeric(18,4)");
        builder.Property(transaction => transaction.Currency).IsRequired().HasMaxLength(3);
        builder.Property(transaction => transaction.IdempotencyKey).IsRequired();
        builder.Property(transaction => transaction.Reference).HasMaxLength(200);
        builder.Property(transaction => transaction.OccurredAt).IsRequired();
        builder.Property(transaction => transaction.OrbitTransactionId).HasMaxLength(200);
        builder.Property(transaction => transaction.ResultingBalance).HasColumnType("numeric(18,4)");
        builder.Property(transaction => transaction.Status).IsRequired().HasConversion<string>().HasMaxLength(32);

        // The at-most-once key: one row per key per company, enforced by the database rather
        // than by convention. A replayed key reads this row instead of writing a second one.
        builder.HasIndex(transaction => new { transaction.TenantId, transaction.CompanyId, transaction.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_loyalty_transactions_idempotency");

        builder.HasIndex(transaction => new { transaction.TenantId, transaction.CompanyId, transaction.CustomerId })
            .HasDatabaseName("ix_loyalty_transactions_customer");

        builder.HasIndex(transaction => new { transaction.TenantId, transaction.Status, transaction.OccurredAt })
            .HasDatabaseName("ix_loyalty_transactions_retry");
    }
}

/// <summary><c>loyalty.tiers</c> — cached tier definitions (Stage 20).</summary>
internal sealed class LoyaltyTierConfiguration : EntityConfiguration<LoyaltyTier>
{
    protected override string Schema => Schemas.Loyalty;

    protected override string TableName => "tiers";

    protected override void ConfigureEntity(EntityTypeBuilder<LoyaltyTier> builder)
    {
        builder.Property(tier => tier.TierId).IsRequired().HasMaxLength(200);
        builder.Property(tier => tier.Name).IsRequired().HasMaxLength(200);
        builder.Property(tier => tier.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(tier => tier.ThresholdPoints).IsRequired().HasColumnType("numeric(18,4)");
        builder.Property(tier => tier.Multiplier).IsRequired().HasColumnType("numeric(18,4)");
        builder.Property(tier => tier.SyncedAt).IsRequired();

        builder.HasIndex(tier => new { tier.TenantId, tier.CompanyId, tier.TierId })
            .IsUnique()
            .HasDatabaseName("ux_loyalty_tiers_tier");
    }
}

/// <summary><c>loyalty.rewards</c> — cached rewards catalogue (Stage 20).</summary>
internal sealed class LoyaltyRewardConfiguration : EntityConfiguration<LoyaltyReward>
{
    protected override string Schema => Schemas.Loyalty;

    protected override string TableName => "rewards";

    protected override void ConfigureEntity(EntityTypeBuilder<LoyaltyReward> builder)
    {
        builder.Property(reward => reward.RewardId).IsRequired().HasMaxLength(200);
        builder.Property(reward => reward.Name).IsRequired().HasMaxLength(200);
        builder.Property(reward => reward.Description).HasMaxLength(2000);
        builder.Property(reward => reward.CostInPoints).IsRequired().HasColumnType("numeric(18,4)");
        builder.Property(reward => reward.TierId).HasMaxLength(200);
        builder.Property(reward => reward.ImageUrl).HasMaxLength(1000);
        builder.Property(reward => reward.IsAvailable).IsRequired();
        builder.Property(reward => reward.SyncedAt).IsRequired();

        builder.HasIndex(reward => new { reward.TenantId, reward.CompanyId, reward.RewardId })
            .IsUnique()
            .HasDatabaseName("ux_loyalty_rewards_reward");
    }
}

/// <summary><c>loyalty.settings</c> — per-company loyalty configuration (Stage 20).</summary>
internal sealed class LoyaltySettingsConfiguration : EntityConfiguration<LoyaltySettings>
{
    protected override string Schema => Schemas.Loyalty;

    protected override string TableName => "settings";

    protected override void ConfigureEntity(EntityTypeBuilder<LoyaltySettings> builder)
    {
        builder.Property(settings => settings.Currency).IsRequired().HasMaxLength(3);
        builder.Property(settings => settings.IsEnabled).IsRequired();
        builder.Property(settings => settings.EarnRate).IsRequired().HasColumnType("numeric(18,4)");
        builder.Property(settings => settings.PointExpiryDays).IsRequired();

        builder.HasIndex(settings => new { settings.TenantId, settings.CompanyId })
            .IsUnique()
            .HasDatabaseName("ux_loyalty_settings_company");
    }
}
