using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Logistics;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Logistics;

internal sealed class CarrierConfiguration : EntityConfiguration<Carrier>
{
    protected override string Schema => Schemas.Logistics; protected override string TableName => "carriers";
    protected override void ConfigureEntity(EntityTypeBuilder<Carrier> b) { b.Property(x => x.Code).IsRequired().HasMaxLength(32); b.Property(x => x.Name).IsRequired().HasMaxLength(160); b.Property(x => x.Phone).HasMaxLength(32); b.Property(x => x.IsActive).IsRequired(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_carriers_tenant_id_code").HasFilter("deleted_at IS NULL"); }
}
internal sealed class ShipmentConfiguration : EntityConfiguration<Shipment>
{
    protected override string Schema => Schemas.Logistics; protected override string TableName => "shipments";
    protected override void ConfigureEntity(EntityTypeBuilder<Shipment> b) { b.Property(x => x.Number).IsRequired().HasMaxLength(64); b.Property(x => x.TrackingNumber).HasMaxLength(128); b.Property(x => x.AddressLine1).IsRequired().HasMaxLength(256); b.Property(x => x.AddressLine2).HasMaxLength(256); b.Property(x => x.City).IsRequired().HasMaxLength(128); b.Property(x => x.PostalCode).HasMaxLength(32); b.Property(x => x.Country).IsRequired().HasMaxLength(2); b.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(24); b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasDatabaseName("ux_shipments_tenant_id_number").HasFilter("deleted_at IS NULL"); b.HasIndex(x => new { x.TenantId, x.TrackingNumber }).HasDatabaseName("ix_shipments_tenant_id_tracking_number").HasFilter("tracking_number IS NOT NULL AND deleted_at IS NULL"); }
}
internal sealed class DeliveryRunConfiguration : EntityConfiguration<DeliveryRun>
{
    protected override string Schema => Schemas.Logistics; protected override string TableName => "delivery_runs";
    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryRun> b) { b.Property(x => x.RunNumber).IsRequired().HasMaxLength(64); b.Property(x => x.PlannedDate).IsRequired(); b.Property(x => x.DriverName).HasMaxLength(160); b.Property(x => x.VehicleRegistration).HasMaxLength(32); b.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(24); b.HasIndex(x => new { x.TenantId, x.RunNumber }).IsUnique().HasDatabaseName("ux_delivery_runs_tenant_id_run_number").HasFilter("deleted_at IS NULL"); }
}
internal sealed class DeliveryStopConfiguration : EntityConfiguration<DeliveryStop>
{
    protected override string Schema => Schemas.Logistics; protected override string TableName => "delivery_stops";
    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryStop> b) { b.Property(x => x.RunId).IsRequired(); b.Property(x => x.ShipmentId).IsRequired(); b.Property(x => x.Sequence).IsRequired(); b.Property(x => x.AddressLine1).IsRequired().HasMaxLength(256); b.Property(x => x.City).IsRequired().HasMaxLength(128); b.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(24); b.HasIndex(x => new { x.RunId, x.Sequence }).IsUnique().HasDatabaseName("ux_delivery_stops_run_id_sequence").HasFilter("deleted_at IS NULL"); b.HasIndex(x => x.ShipmentId).HasDatabaseName("ix_delivery_stops_shipment_id"); }
}
internal sealed class ProofOfDeliveryConfiguration : EntityConfiguration<ProofOfDelivery>
{
    protected override string Schema => Schemas.Logistics; protected override string TableName => "proofs_of_delivery";
    protected override void ConfigureEntity(EntityTypeBuilder<ProofOfDelivery> b) { b.Property(x => x.ShipmentId).IsRequired(); b.Property(x => x.Outcome).IsRequired().HasConversion<string>().HasMaxLength(16); b.Property(x => x.RecipientName).IsRequired().HasMaxLength(160); b.Property(x => x.SignatureHash).HasMaxLength(128); b.Property(x => x.PhotoBlobKey).HasMaxLength(512); b.Property(x => x.Notes).HasMaxLength(1000); b.HasIndex(x => x.ShipmentId).IsUnique().HasDatabaseName("ux_proofs_of_delivery_shipment_id").HasFilter("deleted_at IS NULL"); }
}
