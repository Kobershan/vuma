using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Sales;

/// <summary>
/// The seven <c>sales</c> tables (Stage 10), grouped in one file the way <c>pos</c>'s five and
/// <c>inventory</c>'s six are — one module's schema read as a unit.
/// </summary>
/// <remarks>
/// Every item, variant, customer, sale, sale-line, user and ledger-entry reference below is a plain
/// <see cref="Guid"/> column with an index, never a foreign key into <c>catalog</c>, <c>partners</c>,
/// <c>pos</c>, <c>identity</c> or <c>inventory</c> — <c>CONVENTIONS.md</c> §2 forbids the cross-schema
/// foreign key, and the application layer validates the reference instead. Foreign keys <em>within</em>
/// this schema are legal and used: a price without its list, or a return line without its return, is
/// meaningless.
/// </remarks>
internal sealed class PriceListConfiguration : EntityConfiguration<PriceList>
{
    protected override string Schema => Schemas.Sales;

    protected override string TableName => "price_lists";

    protected override void ConfigureEntity(EntityTypeBuilder<PriceList> builder)
    {
        builder.Property(list => list.Code).IsRequired().HasMaxLength(32);
        builder.Property(list => list.Name).IsRequired().HasMaxLength(128);

        builder.Property(list => list.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        // Stored as text (docs/DATA_MODEL.md §2): an enum persisted by ordinal turns a reordered member
        // into silently relabelled history — and a price list mislabelled Staff instead of Retail is a
        // pricing incident, not a display bug.
        builder.Property(list => list.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(list => list.PricesIncludeTax).IsRequired();
        builder.Property(list => list.Priority).IsRequired();
        builder.Property(list => list.EffectiveFrom).IsRequired();
        builder.Property(list => list.EffectiveTo);
        builder.Property(list => list.IsActive).IsRequired();

        builder.HasMany(list => list.Lines)
            .WithOne()
            .HasForeignKey(line => line.PriceListId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(list => list.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(list => new { list.TenantId, list.Code })
            .IsUnique()
            .HasDatabaseName("ux_price_lists_tenant_id_code")
            .HasFilter("deleted_at IS NULL");

        // The resolver's entry point: "which lists could price something at this store today", asked
        // once per scanned line. Partial on the active set, because a retired list is never a candidate
        // and a tenant accumulates them.
        builder.HasIndex(list => new { list.StoreId, list.EffectiveFrom })
            .HasDatabaseName("ix_price_lists_store_id_effective_active")
            .HasFilter("is_active = true AND deleted_at IS NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_price_lists_effective_window",
            "effective_to IS NULL OR effective_to >= effective_from"));
    }
}

/// <summary><c>sales.price_list_lines</c> — one price, from one quantity upwards.</summary>
internal sealed class PriceListLineConfiguration : EntityConfiguration<PriceListLine>
{
    protected override string Schema => Schemas.Sales;

    protected override string TableName => "price_list_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<PriceListLine> builder)
    {
        builder.Property(line => line.PriceListId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);

        builder.HasMoney(line => line.UnitPrice, "unit_price");

        builder.Property(line => line.MinimumQuantity)
            .IsRequired()
            .HasColumnType(ValueObjectMapping.QuantityColumnType);

        // The uniqueness rule that makes Stage 11's bulk price import idempotent: re-importing the same
        // sheet updates rows rather than accumulating a second price for the same break. Two prices for
        // one break is not a price, and the resolver would have to pick one arbitrarily.
        builder.HasIndex(line => new { line.PriceListId, line.ItemId, line.ItemVariantId, line.MinimumQuantity })
            .IsUnique()
            .HasDatabaseName("ux_price_list_lines_list_id_sku_minimum_quantity")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(line => line.ItemId)
            .HasDatabaseName("ix_price_list_lines_item_id");

        builder.HasIndex(line => line.ItemVariantId)
            .HasDatabaseName("ix_price_list_lines_item_variant_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_price_list_lines_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            // A price list does not pay customers.
            table.HasCheckConstraint("ck_price_list_lines_price_not_negative", "unit_price_amount >= 0");

            table.HasCheckConstraint("ck_price_list_lines_minimum_quantity_positive", "minimum_quantity > 0");
        });
    }
}

/// <summary><c>sales.promotions</c> — a special, as configuration rather than code.</summary>
internal sealed class PromotionConfiguration : EntityConfiguration<Promotion>
{
    protected override string Schema => Schemas.Sales;

    protected override string TableName => "promotions";

    protected override void ConfigureEntity(EntityTypeBuilder<Promotion> builder)
    {
        builder.Property(promotion => promotion.Code).IsRequired().HasMaxLength(32);
        builder.Property(promotion => promotion.Name).IsRequired().HasMaxLength(128);

        builder.Property(promotion => promotion.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(promotion => promotion.DiscountPercentage).HasColumnType("numeric(9,4)");

        // ADR-067: the reward is an optional monetary amount, which EF Core 9 cannot map as an optional
        // complex property. Two plain columns behind the computed Money? accessor, exactly as
        // JournalLine and TillSession do it.
        builder.Property(promotion => promotion.RewardAmount)
            .HasColumnType(ValueObjectMapping.MoneyColumnType);

        builder.Property(promotion => promotion.RewardCurrency)
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(promotion => promotion.RequiredQuantity)
            .HasColumnType(ValueObjectMapping.QuantityColumnType);

        builder.Property(promotion => promotion.FreeQuantity)
            .HasColumnType(ValueObjectMapping.QuantityColumnType);

        builder.Property(promotion => promotion.EffectiveFrom).IsRequired();
        builder.Property(promotion => promotion.EffectiveTo);

        // The flags enum stored as its integer, not as text: it is a set rather than a member, and the
        // string form of a combination ("Monday, Wednesday, Friday") is neither queryable nor stable
        // across a member rename. The other enums in this schema are single members and stay text.
        builder.Property(promotion => promotion.Days).HasConversion<int?>();

        builder.Property(promotion => promotion.StartsAt);
        builder.Property(promotion => promotion.EndsAt);
        builder.Property(promotion => promotion.Priority).IsRequired();
        builder.Property(promotion => promotion.IsExclusive).IsRequired();
        builder.Property(promotion => promotion.IsActive).IsRequired();

        builder.HasMany(promotion => promotion.Lines)
            .WithOne()
            .HasForeignKey(line => line.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(promotion => promotion.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(promotion => new { promotion.TenantId, promotion.Code })
            .IsUnique()
            .HasDatabaseName("ux_promotions_tenant_id_code")
            .HasFilter("deleted_at IS NULL");

        // "What is live at this store today" — read on every price resolution, so it is the one index
        // in this schema that has to earn its keep on a busy Saturday.
        builder.HasIndex(promotion => new { promotion.StoreId, promotion.EffectiveFrom })
            .HasDatabaseName("ix_promotions_store_id_effective_active")
            .HasFilter("is_active = true AND deleted_at IS NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_promotions_effective_window",
                "effective_to IS NULL OR effective_to >= effective_from");

            table.HasCheckConstraint(
                "ck_promotions_percentage_range",
                "discount_percentage IS NULL OR (discount_percentage >= 0 AND discount_percentage <= 100)");

            // ADR-067's pair moves together or not at all: an amount with no currency is the exact bug
            // §7 rule 4 exists to prevent, and it is invisible until something tries to add it up.
            table.HasCheckConstraint(
                "ck_promotions_reward_currency_pairs",
                "(reward_amount IS NULL) = (reward_currency IS NULL)");

            table.HasCheckConstraint(
                "ck_promotions_reward_not_negative",
                "reward_amount IS NULL OR reward_amount >= 0");

            table.HasCheckConstraint(
                "ck_promotions_quantities_positive",
                "(required_quantity IS NULL OR required_quantity > 0) "
                + "AND (free_quantity IS NULL OR free_quantity > 0)");
        });
    }
}

/// <summary><c>sales.promotion_lines</c> — what a promotion applies to.</summary>
internal sealed class PromotionLineConfiguration : EntityConfiguration<PromotionLine>
{
    protected override string Schema => Schemas.Sales;

    protected override string TableName => "promotion_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<PromotionLine> builder)
    {
        builder.Property(line => line.PromotionId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.CategoryCode).HasMaxLength(64);

        builder.HasIndex(line => line.PromotionId)
            .HasDatabaseName("ix_promotion_lines_promotion_id");

        builder.HasIndex(line => line.ItemId)
            .HasDatabaseName("ix_promotion_lines_item_id");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_promotion_lines_exactly_one_target",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int "
            + "+ (category_code IS NOT NULL)::int) = 1"));
    }
}

/// <summary><c>sales.sales_returns</c> — the document that takes goods and money back.</summary>
internal sealed class SalesReturnConfiguration : EntityConfiguration<SalesReturn>
{
    protected override string Schema => Schemas.Sales;

    protected override string TableName => "sales_returns";

    protected override void ConfigureEntity(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.Property(salesReturn => salesReturn.SaleId).IsRequired();
        builder.Property(salesReturn => salesReturn.ReturnNumber).IsRequired().HasMaxLength(32);
        builder.Property(salesReturn => salesReturn.LocationId).IsRequired();
        builder.Property(salesReturn => salesReturn.CustomerId);

        builder.Property(salesReturn => salesReturn.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(salesReturn => salesReturn.Reason).IsRequired().HasMaxLength(500);

        builder.Property(salesReturn => salesReturn.RefundTenderType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(salesReturn => salesReturn.AuthorisedByUserId).IsRequired();

        builder.Property(salesReturn => salesReturn.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasMoney(salesReturn => salesReturn.Net, "net");
        builder.HasMoney(salesReturn => salesReturn.Tax, "tax");
        builder.HasMoney(salesReturn => salesReturn.Gross, "gross");

        builder.Property(salesReturn => salesReturn.RaisedAt).IsRequired();
        builder.Property(salesReturn => salesReturn.CompletedAt);
        builder.Property(salesReturn => salesReturn.CancelledAt);

        builder.HasMany(salesReturn => salesReturn.Lines)
            .WithOne()
            .HasForeignKey(line => line.SalesReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(salesReturn => salesReturn.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(salesReturn => new { salesReturn.TenantId, salesReturn.ReturnNumber })
            .IsUnique()
            .HasDatabaseName("ux_sales_returns_tenant_id_return_number")
            .HasFilter("deleted_at IS NULL");

        // "Has any of this sale already come back?" — asked at the counter before goods are accepted,
        // and again by the aggregate before every line is added.
        builder.HasIndex(salesReturn => salesReturn.SaleId)
            .HasDatabaseName("ix_sales_returns_sale_id");

        builder.HasIndex(salesReturn => new { salesReturn.Status, salesReturn.CompletedAt })
            .HasDatabaseName("ix_sales_returns_status_completed_at");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_sales_returns_completed_has_timestamp",
                "status <> 'Completed' OR completed_at IS NOT NULL");

            table.HasCheckConstraint(
                "ck_sales_returns_cancelled_has_timestamp",
                "status <> 'Cancelled' OR cancelled_at IS NOT NULL");

            // A refund does not take money off the customer, and the document's parts add up. Both are
            // asserted in the aggregate; both are re-asserted here because every later report reads
            // these three columns and a wrong one would be silently wrong rather than loud.
            table.HasCheckConstraint(
                "ck_sales_returns_amounts_not_negative",
                "net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0");

            table.HasCheckConstraint(
                "ck_sales_returns_balances",
                "net_amount + tax_amount = gross_amount");
        });
    }
}

/// <summary><c>sales.sales_return_lines</c> — one line coming back, at what was actually charged.</summary>
internal sealed class SalesReturnLineConfiguration : EntityConfiguration<SalesReturnLine>
{
    protected override string Schema => Schemas.Sales;

    protected override string TableName => "sales_return_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<SalesReturnLine> builder)
    {
        builder.Property(line => line.SalesReturnId).IsRequired();
        builder.Property(line => line.SaleLineId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);

        builder.HasQuantity(line => line.Quantity, "quantity");
        builder.HasQuantity(line => line.OriginalQuantity, "original_quantity");

        builder.Property(line => line.PreviouslyReturnedQuantity)
            .IsRequired()
            .HasColumnType(ValueObjectMapping.QuantityColumnType);

        builder.HasMoney(line => line.UnitPrice, "unit_price");
        builder.HasMoney(line => line.Net, "net");
        builder.HasMoney(line => line.Tax, "tax");
        builder.HasMoney(line => line.Gross, "gross");

        builder.Property(line => line.TaxCode).IsRequired().HasMaxLength(32);
        builder.Property(line => line.OriginalStockLedgerEntryId);

        builder.Property(line => line.StockReturn)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(line => line.StockLedgerEntryId);
        builder.Property(line => line.StockReturnNote).HasMaxLength(500);

        // One row per original line per document. Two rows for one sale line is how an over-return
        // slips past a per-row check: each passes on its own and the pair does not.
        builder.HasIndex(line => new { line.SalesReturnId, line.SaleLineId })
            .IsUnique()
            .HasDatabaseName("ux_sales_return_lines_return_id_sale_line_id")
            .HasFilter("deleted_at IS NULL");

        // "How much of this sale line has already come back" — the cumulative check, which the
        // aggregate performs on every added line.
        builder.HasIndex(line => line.SaleLineId)
            .HasDatabaseName("ix_sales_return_lines_sale_line_id");

        builder.HasIndex(line => line.StockReturn)
            .HasDatabaseName("ix_sales_return_lines_stock_return_refused")
            .HasFilter("stock_return = 'Refused' AND deleted_at IS NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_sales_return_lines_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            table.HasCheckConstraint("ck_sales_return_lines_quantity_positive", "quantity_value > 0");

            // Business rule 5 as a database guarantee. The two snapshot columns are what make it
            // expressible on the row: a return line written around the aggregate — by an import, a
            // repair script, a future bug — still cannot claim more than was sold. What this cannot
            // settle is two returns raced against the same line, where each snapshot is honest at the
            // moment it is taken; that case is settled by the aggregate reading the committed sum
            // inside the command's transaction.
            table.HasCheckConstraint(
                "ck_sales_return_lines_within_quantity_sold",
                "quantity_value + previously_returned_quantity <= original_quantity_value");

            table.HasCheckConstraint(
                "ck_sales_return_lines_previously_returned_not_negative",
                "previously_returned_quantity >= 0");

            table.HasCheckConstraint(
                "ck_sales_return_lines_balances",
                "net_amount + tax_amount = gross_amount");

            table.HasCheckConstraint(
                "ck_sales_return_lines_amounts_not_negative",
                "net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0 AND unit_price_amount >= 0");
        });
    }
}

/// <summary><c>sales.price_override_logs</c> — the append-only off-price log (business rule 9).</summary>
internal sealed class PriceOverrideLogConfiguration : EntityConfiguration<PriceOverrideLog>
{
    protected override string Schema => Schemas.Sales;

    protected override string TableName => "price_override_logs";

    protected override void ConfigureEntity(EntityTypeBuilder<PriceOverrideLog> builder)
    {
        builder.Property(entry => entry.SaleId);
        builder.Property(entry => entry.SaleLineId);
        builder.Property(entry => entry.ItemId);
        builder.Property(entry => entry.ItemVariantId);
        builder.Property(entry => entry.OperatorUserId).IsRequired();

        builder.HasQuantity(entry => entry.Quantity, "quantity");
        builder.HasMoney(entry => entry.ResolvedUnitPrice, "resolved_unit_price");
        builder.HasMoney(entry => entry.ActualUnitPrice, "actual_unit_price");

        builder.Property(entry => entry.Reason).IsRequired().HasMaxLength(500);
        builder.Property(entry => entry.OccurredAt).IsRequired();

        // Variance is derived from two columns that are already here, so there is nothing to persist and
        // nothing that can disagree with them. Left undeclared, EF discovers the Money-typed property
        // and tries to map it.
        builder.Ignore(entry => entry.Variance);

        // The shrinkage report: "who overrode what, and when". Operator first, because the question is
        // almost always asked about a person rather than about a date.
        builder.HasIndex(entry => new { entry.OperatorUserId, entry.OccurredAt })
            .HasDatabaseName("ix_price_override_logs_operator_user_id_occurred_at")
            .IsDescending(false, true);

        builder.HasIndex(entry => entry.OccurredAt)
            .HasDatabaseName("ix_price_override_logs_occurred_at")
            .IsDescending(true);

        builder.HasIndex(entry => entry.SaleId)
            .HasDatabaseName("ix_price_override_logs_sale_id")
            .HasFilter("sale_id IS NOT NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_price_override_logs_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            table.HasCheckConstraint("ck_price_override_logs_quantity_positive", "quantity_value > 0");

            table.HasCheckConstraint(
                "ck_price_override_logs_prices_not_negative",
                "resolved_unit_price_amount >= 0 AND actual_unit_price_amount >= 0");
        });
    }
}
