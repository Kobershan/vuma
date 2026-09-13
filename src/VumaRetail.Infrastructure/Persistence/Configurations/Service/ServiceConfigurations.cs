using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Service;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Service;

internal sealed class ServiceTicketConfiguration : EntityConfiguration<ServiceTicket>
{
    protected override string Schema => Schemas.Service;
    protected override string TableName => "service_tickets";
    protected override void ConfigureEntity(EntityTypeBuilder<ServiceTicket> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.OperationId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.CustomerId, x.Status })
            .HasDatabaseName("ix_service_tickets_tenant_company_customer_status").HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.OperationId }).IsUnique()
            .HasDatabaseName("ux_service_tickets_tenant_operation").HasFilter("deleted_at IS NULL");
    }
}

internal sealed class WarrantyClaimConfiguration : EntityConfiguration<WarrantyClaim>
{
    protected override string Schema => Schemas.Service;
    protected override string TableName => "warranty_claims";
    protected override void ConfigureEntity(EntityTypeBuilder<WarrantyClaim> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.TicketId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.SaleReference).IsRequired().HasMaxLength(128);
        builder.Property(x => x.SerialNumber).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.DecisionReason).HasMaxLength(512);
        builder.HasIndex(x => new { x.TenantId, x.TicketId }).IsUnique()
            .HasDatabaseName("ux_warranty_claims_tenant_ticket").HasFilter("deleted_at IS NULL");
    }
}

internal sealed class RepairJobConfiguration : EntityConfiguration<RepairJob>
{
    protected override string Schema => Schemas.Service;
    protected override string TableName => "repair_jobs";
    protected override void ConfigureEntity(EntityTypeBuilder<RepairJob> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.TicketId).IsRequired();
        builder.Property(x => x.ItemReference).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => new { x.TenantId, x.TicketId }).HasDatabaseName("ix_repair_jobs_tenant_ticket")
            .HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ServicePartUsageConfiguration : EntityConfiguration<ServicePartUsage>
{
    protected override string Schema => Schemas.Service;
    protected override string TableName => "service_part_usages";
    protected override void ConfigureEntity(EntityTypeBuilder<ServicePartUsage> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.RepairJobId).IsRequired();
        builder.Property(x => x.OperationId).IsRequired();
        builder.Property(x => x.Quantity).IsRequired().HasPrecision(18, 6);
        builder.Property(x => x.UnitCost).IsRequired().HasPrecision(18, 2);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.HasIndex(x => new { x.TenantId, x.OperationId }).IsUnique()
            .HasDatabaseName("ux_service_part_usages_tenant_operation").HasFilter("deleted_at IS NULL");
    }
}

internal sealed class ServiceSlaConfiguration : EntityConfiguration<ServiceSla>
{
    protected override string Schema => Schemas.Service;
    protected override string TableName => "service_slas";
    protected override void ConfigureEntity(EntityTypeBuilder<ServiceSla> builder)
    {
        builder.Property(x => x.CompanyId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ResponseHours).IsRequired().HasPrecision(10, 2);
        builder.Property(x => x.ResolutionHours).IsRequired().HasPrecision(10, 2);
        builder.HasIndex(x => new { x.TenantId, x.CompanyId, x.Name }).IsUnique()
            .HasDatabaseName("ux_service_slas_tenant_company_name").HasFilter("deleted_at IS NULL");
    }
}
