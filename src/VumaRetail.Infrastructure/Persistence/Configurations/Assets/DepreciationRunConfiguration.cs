using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Assets;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Assets;

internal sealed class DepreciationRunConfiguration : EntityConfiguration<DepreciationRun>
{
    protected override string Schema => Schemas.Assets;
    protected override string TableName => "depreciation_runs";
    protected override void ConfigureEntity(EntityTypeBuilder<DepreciationRun> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.AssetId).IsRequired();
        builder.Property(x => x.AssetBookId).IsRequired();
        builder.Property(x => x.Period).IsRequired();
        builder.HasMoney(x => x.Amount, "amount");
        builder.HasMoney(x => x.ClosingNetBookValue, "closing_net_book_value");
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.AssetBookId, x.Period }).IsUnique()
            .HasDatabaseName("ux_depreciation_runs_tenant_company_book_period").HasFilter("deleted_at IS NULL");
    }
}
