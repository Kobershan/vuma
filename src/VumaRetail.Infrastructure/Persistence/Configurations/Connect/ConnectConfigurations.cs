#pragma warning disable CS1591
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Connect;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Connect;

internal sealed class TradingConnectionConfiguration : EntityConfiguration<TradingConnection>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "trading_connections";
    protected override void ConfigureEntity(EntityTypeBuilder<TradingConnection> builder)
    {
        builder.Property(x => x.SupplierTenantId).IsRequired();
        builder.Property(x => x.RetailerTenantId).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.SupplierAccountReference).HasMaxLength(64);
        builder.Property(x => x.RetailerAccountReference).HasMaxLength(64);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreditLimit).HasColumnType("numeric(19,4)");
        builder.Property(x => x.MinimumOrderValue).HasColumnType("numeric(19,4)");
        builder.HasIndex(x => new { x.TenantId, x.SupplierTenantId, x.RetailerTenantId }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ConnectionCodeConfiguration : EntityConfiguration<ConnectionCode>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "connection_codes";
    protected override void ConfigureEntity(EntityTypeBuilder<ConnectionCode> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PriceTier).HasMaxLength(64);
        builder.Property(x => x.Territory).HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class CataloguePublicationConfiguration : EntityConfiguration<CataloguePublication>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "catalogue_publications";
    protected override void ConfigureEntity(EntityTypeBuilder<CataloguePublication> builder)
    {
        builder.Property(x => x.VersionNote).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.ConnectionId, x.Version }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PublicationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CataloguePublicationLineConfiguration : EntityConfiguration<CataloguePublicationLine>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "catalogue_publication_lines";
    protected override void ConfigureEntity(EntityTypeBuilder<CataloguePublicationLine> builder)
    {
        builder.Property(x => x.SupplierSku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Barcode).HasMaxLength(64);
        builder.Property(x => x.MinimumOrderQuantity).HasColumnType("numeric(19,4)");
        builder.HasIndex(x => new { x.TenantId, x.PublicationId, x.SupplierSku }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class PriceProposalConfiguration : EntityConfiguration<PriceProposal>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "price_proposals";
    protected override void ConfigureEntity(EntityTypeBuilder<PriceProposal> builder)
    {
        builder.Property(x => x.VersionNote).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ConnectionId, x.EffectiveFrom }).HasFilter("deleted_at IS NULL");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PriceProposalLineConfiguration : EntityConfiguration<PriceProposalLine>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "price_proposal_lines";
    protected override void ConfigureEntity(EntityTypeBuilder<PriceProposalLine> builder)
    {
        builder.Property(x => x.SupplierSku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnType("numeric(19,4)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.MinimumOrderQuantity).HasColumnType("numeric(19,4)");
        builder.HasIndex(x => new { x.TenantId, x.ProposalId, x.SupplierSku }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ConnectOrderConfiguration : EntityConfiguration<ConnectOrder>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "orders";
    protected override void ConfigureEntity(EntityTypeBuilder<ConnectOrder> builder)
    {
        builder.Property(x => x.OrderNumber).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.DispatchNoteNumber).HasMaxLength(64);
        builder.Property(x => x.RejectionReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.OrderNumber }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.ConnectionId, x.Status }).HasFilter("deleted_at IS NULL");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConnectOrderLineConfiguration : EntityConfiguration<ConnectOrderLine>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "order_lines";
    protected override void ConfigureEntity(EntityTypeBuilder<ConnectOrderLine> builder)
    {
        builder.Property(x => x.SupplierSku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(256).IsRequired();
        builder.HasQuantity(x => x.RequestedQuantity, "requested_quantity");
        builder.HasQuantity(x => x.ConfirmedQuantity, "confirmed_quantity");
        builder.HasQuantity(x => x.DispatchedQuantity, "dispatched_quantity");
        builder.HasMoney(x => x.UnitPrice, "unit_price");
        builder.HasIndex(x => new { x.TenantId, x.OrderId, x.SupplierSku }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ConnectRemittanceConfiguration : EntityConfiguration<ConnectRemittanceAdvice>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "remittance_advices";
    protected override void ConfigureEntity(EntityTypeBuilder<ConnectRemittanceAdvice> builder)
    {
        builder.Property(x => x.InvoiceReference).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RemittanceReference).HasMaxLength(128).IsRequired();
        builder.HasMoney(x => x.Amount, "amount");
        builder.HasIndex(x => new { x.TenantId, x.PaymentId }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ConnectClaimConfiguration : EntityConfiguration<ConnectDeliveryClaim>
{
    protected override string Schema => Schemas.Connect;
    protected override string TableName => "delivery_claims";
    protected override void ConfigureEntity(EntityTypeBuilder<ConnectDeliveryClaim> builder)
    {
        builder.Property(x => x.ClaimNumber).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreditNoteReference).HasMaxLength(128);
        builder.HasQuantity(x => x.Quantity, "quantity");
        builder.HasMoney(x => x.Amount, "amount");
        builder.HasIndex(x => new { x.TenantId, x.ClaimNumber }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
