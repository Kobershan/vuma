using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Planning;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Planning;

/// <summary><c>planning.demand_history</c> — aggregated demand history from sale issues.</summary>
internal sealed class DemandHistoryConfiguration : EntityConfiguration<DemandHistory>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "demand_history";

    protected override void ConfigureEntity(EntityTypeBuilder<DemandHistory> builder)
    {
        builder.Property(entry => entry.LocationId).IsRequired();

        builder.Property(entry => entry.ItemId);

        builder.Property(entry => entry.ItemVariantId);

        builder.Property(entry => entry.PeriodStart).IsRequired();

        builder.Property(entry => entry.PeriodEnd).IsRequired();

        builder.Property(entry => entry.TotalQuantity).IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(entry => entry.Uom).IsRequired().HasMaxLength(16);

        builder.Property(entry => entry.GeneratedAt).IsRequired();

        // Natural key: one row per SKU/location/week. Rebuilds upsert on it, never duplicate.
        builder.HasIndex(entry => new { entry.TenantId, entry.CompanyId, entry.LocationId, entry.ItemId, entry.PeriodStart })
            .IsUnique()
            .HasDatabaseName("ux_demand_history_item_period")
            .HasFilter("item_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(entry => new { entry.TenantId, entry.CompanyId, entry.LocationId, entry.ItemVariantId, entry.PeriodStart })
            .IsUnique()
            .HasDatabaseName("ux_demand_history_variant_period")
            .HasFilter("item_variant_id IS NOT NULL AND deleted_at IS NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_demand_history_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
        });
    }
}

/// <summary><c>planning.demand_forecasts</c> — versioned forecast snapshots.</summary>
internal sealed class DemandForecastConfiguration : EntityConfiguration<DemandForecast>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "demand_forecasts";

    protected override void ConfigureEntity(EntityTypeBuilder<DemandForecast> builder)
    {
        builder.Property(entry => entry.ItemId);

        builder.Property(entry => entry.ItemVariantId);

        builder.Property(entry => entry.LocationId).IsRequired();

        builder.Property(entry => entry.ForecastPeriod).IsRequired();

        builder.Property(entry => entry.ForecastMethod).IsRequired().HasConversion<string>().HasMaxLength(32);

        builder.Property(entry => entry.Quantity).IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(entry => entry.Mape).IsRequired()
            .HasColumnType("numeric(18,4)");

        builder.Property(entry => entry.Bias).HasColumnType("numeric(18,4)");

        builder.Property(entry => entry.Version).IsRequired().HasMaxLength(16);

        builder.Property(entry => entry.GeneratedAt).IsRequired();

        builder.Property(entry => entry.GeneratedBy);

        // One version stamp per SKU/location/period — re-runs append, never overwrite.
        builder.HasIndex(entry => new { entry.TenantId, entry.CompanyId, entry.LocationId, entry.ItemId, entry.ItemVariantId, entry.ForecastPeriod, entry.Version })
            .IsUnique()
            .HasDatabaseName("ux_demand_forecasts_period_version");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_demand_forecasts_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
        });
    }
}

/// <summary><c>planning.replenishment_parameters</c> — per-SKU/location replenishment settings.</summary>
internal sealed class ReplenishmentParameterConfiguration : EntityConfiguration<ReplenishmentParameter>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "replenishment_parameters";

    protected override void ConfigureEntity(EntityTypeBuilder<ReplenishmentParameter> builder)
    {
        builder.Property(parameter => parameter.LocationId).IsRequired();

        builder.Property(parameter => parameter.ItemId);

        builder.Property(parameter => parameter.ItemVariantId);

        builder.Property(parameter => parameter.ForecastMethod).IsRequired().HasConversion<string>().HasMaxLength(32);

        builder.Property(parameter => parameter.ServiceLevelPercent).IsRequired()
            .HasColumnType("numeric(5,2)");

        builder.Property(parameter => parameter.LeadTimeDays).IsRequired();

        builder.Property(parameter => parameter.ReviewPeriodDays).IsRequired();

        builder.Property(parameter => parameter.Uom).IsRequired().HasMaxLength(16);

        builder.HasIndex(parameter => new { parameter.TenantId, parameter.CompanyId, parameter.LocationId, parameter.ItemId, parameter.ItemVariantId })
            .IsUnique()
            .HasDatabaseName("ux_replenishment_parameters_sku")
            .HasFilter("deleted_at IS NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_replenishment_parameters_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
        });
    }
}

/// <summary><c>planning.abc_xyz_classifications</c> — append-only classification snapshots.</summary>
internal sealed class AbcXyzClassificationConfiguration : EntityConfiguration<AbcXyzClassification>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "abc_xyz_classifications";

    protected override void ConfigureEntity(EntityTypeBuilder<AbcXyzClassification> builder)
    {
        builder.Property(classification => classification.LocationId).IsRequired();

        builder.Property(classification => classification.ItemId);

        builder.Property(classification => classification.ItemVariantId);

        builder.Property(classification => classification.Abc).IsRequired().HasConversion<string>().HasMaxLength(8);

        builder.Property(classification => classification.Xyz).IsRequired().HasConversion<string>().HasMaxLength(8);

        builder.Property(classification => classification.DemandShare).IsRequired()
            .HasColumnType("numeric(9,6)");

        builder.Property(classification => classification.CoefficientOfVariation).IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(classification => classification.PeriodStart).IsRequired();

        builder.Property(classification => classification.PeriodEnd).IsRequired();

        builder.Property(classification => classification.GeneratedAt).IsRequired();

        builder.HasIndex(classification => new { classification.TenantId, classification.CompanyId, classification.LocationId, classification.ItemId, classification.ItemVariantId, classification.GeneratedAt })
            .HasDatabaseName("ix_abc_xyz_sku_generated");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_abc_xyz_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
        });
    }
}

/// <summary><c>planning.safety_stock_calculations</c> — append-only calculation audit.</summary>
internal sealed class SafetyStockCalculationConfiguration : EntityConfiguration<SafetyStockCalculation>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "safety_stock_calculations";

    protected override void ConfigureEntity(EntityTypeBuilder<SafetyStockCalculation> builder)
    {
        builder.Property(calculation => calculation.LocationId).IsRequired();

        builder.Property(calculation => calculation.ItemId);

        builder.Property(calculation => calculation.ItemVariantId);

        builder.Property(calculation => calculation.LeadTimeDemandMean).IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(calculation => calculation.DemandVariance).IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(calculation => calculation.ServiceLevelPercent).IsRequired()
            .HasColumnType("numeric(5,2)");

        builder.Property(calculation => calculation.HistoryWeeks).IsRequired();

        builder.Property(calculation => calculation.LeadTimeDays).IsRequired();

        builder.Property(calculation => calculation.SafetyStock).IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(calculation => calculation.ReorderPoint).IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(calculation => calculation.LowConfidence).IsRequired();

        builder.Property(calculation => calculation.Method).IsRequired().HasMaxLength(16);

        builder.Property(calculation => calculation.CalculatedAt).IsRequired();

        builder.HasIndex(calculation => new { calculation.TenantId, calculation.CompanyId, calculation.LocationId, calculation.ItemId, calculation.ItemVariantId, calculation.CalculatedAt })
            .HasDatabaseName("ix_safety_stock_sku_calculated");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_safety_stock_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
        });
    }
}

/// <summary><c>planning.open_to_buy_budgets</c> — monthly budgets, one row per company/month/category.</summary>
internal sealed class OpenToBuyBudgetConfiguration : EntityConfiguration<OpenToBuyBudget>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "open_to_buy_budgets";

    protected override void ConfigureEntity(EntityTypeBuilder<OpenToBuyBudget> builder)
    {
        builder.Property(budget => budget.Year).IsRequired();

        builder.Property(budget => budget.Month).IsRequired();

        builder.Property(budget => budget.CategoryCode).HasMaxLength(32);

        builder.Property(budget => budget.PlannedAmount).IsRequired()
            .HasColumnType("numeric(18,4)");

        builder.Property(budget => budget.Currency).IsRequired().HasMaxLength(3);

        builder.HasIndex(budget => new { budget.TenantId, budget.CompanyId, budget.Year, budget.Month, budget.CategoryCode })
            .IsUnique()
            .HasDatabaseName("ux_otb_budgets_month")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>planning.replenishment_suggestions</c> — proposals that decide nothing until accepted.</summary>
internal sealed class ReplenishmentSuggestionConfiguration : EntityConfiguration<ReplenishmentSuggestion>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "replenishment_suggestions";

    protected override void ConfigureEntity(EntityTypeBuilder<ReplenishmentSuggestion> builder)
    {
        builder.Property(suggestion => suggestion.LocationId).IsRequired();

        builder.Property(suggestion => suggestion.ItemId);

        builder.Property(suggestion => suggestion.ItemVariantId);

        builder.Property(suggestion => suggestion.SuggestedQuantity).IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(suggestion => suggestion.AcceptedQuantity)
            .HasColumnType("numeric(18,6)");

        builder.Property(suggestion => suggestion.Uom).IsRequired().HasMaxLength(16);

        builder.Property(suggestion => suggestion.Reason).IsRequired().HasConversion<string>().HasMaxLength(32);

        builder.Property(suggestion => suggestion.Source).IsRequired().HasConversion<string>().HasMaxLength(16);

        builder.Property(suggestion => suggestion.SourceCompanyId);

        builder.Property(suggestion => suggestion.SourceLocationId);

        builder.Property(suggestion => suggestion.Status).IsRequired().HasConversion<string>().HasMaxLength(16);

        builder.Property(suggestion => suggestion.DownstreamDocumentId);

        builder.Property(suggestion => suggestion.IdempotencyKey).IsRequired().HasMaxLength(128);

        builder.Property(suggestion => suggestion.OverOpenToBuy).IsRequired();

        builder.Property(suggestion => suggestion.RaisedAt).IsRequired();

        builder.Property(suggestion => suggestion.ExpiresAt).IsRequired();

        // Exactly-once acceptance and idempotent re-runs both key off this.
        builder.HasIndex(suggestion => suggestion.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_replenishment_suggestions_run_key")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(suggestion => new { suggestion.TenantId, suggestion.CompanyId, suggestion.Status })
            .HasDatabaseName("ix_replenishment_suggestions_status");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_replenishment_suggestions_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
        });
    }
}

/// <summary><c>planning.markdown_plans</c> and <c>planning.markdown_plan_lines</c>.</summary>
internal sealed class MarkdownPlanConfiguration : EntityConfiguration<MarkdownPlan>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "markdown_plans";

    protected override void ConfigureEntity(EntityTypeBuilder<MarkdownPlan> builder)
    {
        builder.Property(plan => plan.Code).IsRequired().HasMaxLength(32);

        builder.Property(plan => plan.Reason).IsRequired().HasMaxLength(1000);

        builder.Property(plan => plan.EffectiveFrom).IsRequired();

        builder.Property(plan => plan.EffectiveTo);

        builder.Property(plan => plan.Version).IsRequired();

        builder.Property(plan => plan.Status).IsRequired().HasConversion<string>().HasMaxLength(16);

        builder.Property(plan => plan.ApprovalRequestId);

        builder.Property(plan => plan.PromotionId);

        builder.Property(plan => plan.SupersedesPlanId);

        builder.HasIndex(plan => new { plan.TenantId, plan.Code })
            .IsUnique()
            .HasDatabaseName("ux_markdown_plans_code")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(plan => new { plan.TenantId, plan.CompanyId, plan.Status })
            .HasDatabaseName("ix_markdown_plans_status");

        builder.HasMany(plan => plan.Lines)
            .WithOne()
            .HasForeignKey(line => line.MarkdownPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary><c>planning.markdown_plan_lines</c> — decision inputs snapshotted at authoring time.</summary>
internal sealed class MarkdownPlanLineConfiguration : EntityConfiguration<MarkdownPlanLine>
{
    protected override string Schema => Schemas.Planning;

    protected override string TableName => "markdown_plan_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<MarkdownPlanLine> builder)
    {
        builder.Property(line => line.MarkdownPlanId).IsRequired();

        builder.Property(line => line.ItemId);

        builder.Property(line => line.ItemVariantId);

        builder.Property(line => line.CurrentPrice).IsRequired()
            .HasColumnType("numeric(18,4)");

        builder.Property(line => line.ProposedDiscountPercent).IsRequired()
            .HasColumnType("numeric(5,2)");

        builder.Property(line => line.Currency).IsRequired().HasMaxLength(3);

        builder.Property(line => line.AbcXyz).HasMaxLength(8);

        builder.Property(line => line.SellThroughPercent).IsRequired()
            .HasColumnType("numeric(7,3)");

        builder.Property(line => line.DaysOfSupply).IsRequired()
            .HasColumnType("numeric(18,2)");

        builder.Property(line => line.PromotionId);

        builder.HasIndex(line => line.MarkdownPlanId)
            .HasDatabaseName("ix_markdown_plan_lines_plan");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_markdown_plan_lines_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
        });
    }
}
