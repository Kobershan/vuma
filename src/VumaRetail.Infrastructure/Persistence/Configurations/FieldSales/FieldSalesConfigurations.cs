using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Infrastructure.Persistence.Configurations;

namespace VumaRetail.Infrastructure.Persistence.Configurations.FieldSales;

/// <summary>EF configuration for <see cref="Rep"/> (Stage 14b).</summary>
internal sealed class RepConfiguration : EntityConfiguration<Rep>
{
    protected override string Schema => Schemas.FieldSales;

    protected override string TableName => "reps";

    protected override void ConfigureEntity(EntityTypeBuilder<Rep> builder)
    {
        builder.Property(rep => rep.RegistryUserId).IsRequired();
        builder.Property(rep => rep.DisplayName).IsRequired().HasMaxLength(128);
        builder.Property(rep => rep.TerritoryProvince).HasMaxLength(64);
        builder.Property(rep => rep.TerritoryCity).HasMaxLength(64);
        builder.Property(rep => rep.TerritorySuburb).HasMaxLength(64);
        builder.Property(rep => rep.SeeCost).IsRequired();
        builder.Property(rep => rep.IsActive).IsRequired();

        // Plain primitive collections: Npgsql maps List<Guid> to uuid[] natively. An explicit
        // ToArray/ToList value conversion breaks change-tracker snapshots (the stored array
        // cannot cast back to the list type), so there deliberately is none here.
        builder.Property(rep => rep.CompanyIds);
        builder.Property(rep => rep.CustomerIds);

        builder.HasIndex(rep => new { rep.TenantId, rep.RegistryUserId }).IsUnique();
        builder.HasIndex(rep => new { rep.TenantId, rep.IsActive });
    }
}

/// <summary>EF configuration for <see cref="ProFormaOrder"/> (Stage 14b).</summary>
internal sealed class ProFormaOrderConfiguration : EntityConfiguration<ProFormaOrder>
{
    protected override string Schema => Schemas.FieldSales;

    protected override string TableName => "pro_forma_orders";

    protected override void ConfigureEntity(EntityTypeBuilder<ProFormaOrder> builder)
    {
        builder.Property(order => order.ProFormaNumber).IsRequired().HasMaxLength(32);
        builder.Property(order => order.RepId).IsRequired();
        builder.Property(order => order.PartnerId).IsRequired();
        builder.Property(order => order.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(order => order.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(order => order.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(order => order.ExpiresAt).IsRequired();
        builder.Property(order => order.ApprovalRequestId);
        builder.Property(order => order.ConvertedOrderId);
        builder.Property(order => order.DecisionReason).HasMaxLength(500);
        builder.Property(order => order.RepriceDeltaAmount).HasColumnType(ValueObjectMapping.MoneyColumnType);
        builder.Ignore(order => order.RepriceDelta);
        builder.HasAddress(order => order.DeliveryAddress, "delivery_address", isRequired: false);
        builder.Property(order => order.CapturedAt).IsRequired();
        builder.Property(order => order.SubmittedAt);
        builder.Property(order => order.DecidedAt);
        builder.HasMany(order => order.Lines).WithOne().HasForeignKey(line => line.ProFormaOrderId);
        builder.Ignore(order => order.Gross);

        builder.HasIndex(order => new { order.TenantId, order.ProFormaNumber }).IsUnique();
        builder.HasIndex(order => new { order.TenantId, order.IdempotencyKey }).IsUnique();
        builder.HasIndex(order => new { order.TenantId, order.RepId, order.Status });
    }
}

/// <summary>EF configuration for <see cref="ProFormaOrderLine"/> (Stage 14b).</summary>
internal sealed class ProFormaOrderLineConfiguration : EntityConfiguration<ProFormaOrderLine>
{
    protected override string Schema => Schemas.FieldSales;

    protected override string TableName => "pro_forma_order_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<ProFormaOrderLine> builder)
    {
        builder.Property(line => line.ProFormaOrderId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.QuantityValue).HasColumnType(ValueObjectMapping.QuantityColumnType).IsRequired();
        builder.Property(line => line.QuantityUom).HasMaxLength(16).IsRequired();
        builder.HasMoney(line => line.UnitPrice, "unit_price");
        builder.HasMoney(line => line.DiscountAmount, "discount");
        builder.Property(line => line.TaxCode).HasMaxLength(32).IsRequired();
        builder.HasMoney(line => line.TaxAmount, "tax");
        builder.HasMoney(line => line.Net, "net");
        builder.Ignore(line => line.Gross);
        builder.Property(line => line.PackSizeDescription).HasMaxLength(128).IsRequired();
        builder.Property(line => line.PriceListId);
        builder.Property(line => line.PromotionsSummary).HasMaxLength(500).IsRequired();
        builder.HasMoney(line => line.AvailableAtCapture, "available");
        builder.Property(line => line.AvailabilityAsAt).IsRequired();

        builder.HasIndex(line => new { line.TenantId, line.ProFormaOrderId });
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pro_forma_order_lines_quantity_positive", "quantity_value > 0"));
    }
}

/// <summary>EF configuration for <see cref="ProFormaCreditNote"/> (Stage 14b).</summary>
internal sealed class ProFormaCreditNoteConfiguration : EntityConfiguration<ProFormaCreditNote>
{
    protected override string Schema => Schemas.FieldSales;

    protected override string TableName => "pro_forma_credit_notes";

    protected override void ConfigureEntity(EntityTypeBuilder<ProFormaCreditNote> builder)
    {
        builder.Property(note => note.CreditNoteNumber).IsRequired().HasMaxLength(32);
        builder.Property(note => note.RepId).IsRequired();
        builder.Property(note => note.OriginalInvoiceId).IsRequired();
        builder.Property(note => note.OriginalInvoiceNumber).IsRequired().HasMaxLength(32);
        builder.Property(note => note.ReasonCode).IsRequired().HasMaxLength(32);
        builder.Property(note => note.Reason).IsRequired().HasMaxLength(500);
        builder.Property(note => note.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(note => note.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(note => note.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(note => note.ExpiresAt).IsRequired();
        builder.Property(note => note.ApprovalRequestId);
        builder.Property(note => note.ResultingReturnId);
        builder.Property(note => note.DecisionReason).HasMaxLength(500);
        builder.Property(note => note.CapturedAt).IsRequired();
        builder.HasMany(note => note.Lines).WithOne().HasForeignKey(line => line.ProFormaCreditNoteId);
        builder.Ignore(note => note.Gross);

        builder.HasIndex(note => new { note.TenantId, note.CreditNoteNumber }).IsUnique();
        builder.HasIndex(note => new { note.TenantId, note.IdempotencyKey }).IsUnique();
        builder.HasIndex(note => new { note.TenantId, note.RepId, note.Status });
    }
}

/// <summary>EF configuration for <see cref="ProFormaCreditNoteLine"/> (Stage 14b).</summary>
internal sealed class ProFormaCreditNoteLineConfiguration : EntityConfiguration<ProFormaCreditNoteLine>
{
    protected override string Schema => Schemas.FieldSales;

    protected override string TableName => "pro_forma_credit_note_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<ProFormaCreditNoteLine> builder)
    {
        builder.Property(line => line.ProFormaCreditNoteId).IsRequired();
        builder.Property(line => line.OriginalInvoiceLineId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.QuantityValue).HasColumnType(ValueObjectMapping.QuantityColumnType).IsRequired();
        builder.Property(line => line.QuantityUom).HasMaxLength(16).IsRequired();
        builder.HasMoney(line => line.UnitPrice, "unit_price");
        builder.HasMoney(line => line.TaxAmount, "tax");
        builder.HasMoney(line => line.Net, "net");
        builder.Ignore(line => line.Gross);

        builder.HasIndex(line => new { line.TenantId, line.ProFormaCreditNoteId });
    }
}

/// <summary>EF configuration for <see cref="RepTarget"/> (Stage 14b).</summary>
internal sealed class RepTargetConfiguration : EntityConfiguration<RepTarget>
{
    protected override string Schema => Schemas.FieldSales;

    protected override string TableName => "rep_targets";

    protected override void ConfigureEntity(EntityTypeBuilder<RepTarget> builder)
    {
        builder.Property(target => target.RepId).IsRequired();
        builder.Property(target => target.PeriodStart).IsRequired();
        builder.HasMoney(target => target.TargetNet, "target_net");
        builder.Property(target => target.Version).IsRequired();
        builder.Property(target => target.Reason).IsRequired().HasMaxLength(256);
        builder.Property(target => target.IsCurrent).IsRequired();
        builder.Property(target => target.SetAt).IsRequired();

        builder.HasIndex(target => new { target.TenantId, target.RepId, target.PeriodStart, target.Version }).IsUnique();
    }
}

/// <summary>EF configuration for <see cref="RepPerformanceSnapshot"/> (Stage 14b).</summary>
internal sealed class RepPerformanceSnapshotConfiguration : EntityConfiguration<RepPerformanceSnapshot>
{
    protected override string Schema => Schemas.FieldSales;

    protected override string TableName => "rep_performance_snapshots";

    protected override void ConfigureEntity(EntityTypeBuilder<RepPerformanceSnapshot> builder)
    {
        builder.Property(snapshot => snapshot.RepId).IsRequired();
        builder.Property(snapshot => snapshot.CompanyId);
        builder.Property(snapshot => snapshot.PeriodStart).IsRequired();
        builder.Property(snapshot => snapshot.CapturedCount).IsRequired();
        builder.HasMoney(snapshot => snapshot.CapturedValue, "captured");
        builder.HasMoney(snapshot => snapshot.ConvertedValue, "converted");
        builder.HasMoney(snapshot => snapshot.RejectedValue, "rejected");
        builder.HasMoney(snapshot => snapshot.ExpiredValue, "expired");
        builder.HasMoney(snapshot => snapshot.InvoicedValue, "invoiced");
        builder.HasMoney(snapshot => snapshot.CreditedValue, "credited");
        builder.HasMoney(snapshot => snapshot.NetValue, "net");
        builder.Property(snapshot => snapshot.MarginAmount).HasColumnType(ValueObjectMapping.MoneyColumnType);
        builder.Ignore(snapshot => snapshot.MarginValue);
        builder.Ignore(snapshot => snapshot.HasMargin);
        builder.Property(snapshot => snapshot.ActiveCustomers).IsRequired();
        builder.Property(snapshot => snapshot.Version).IsRequired();
        builder.Property(snapshot => snapshot.Reason).IsRequired().HasMaxLength(256);
        builder.Property(snapshot => snapshot.SnapshottedAt).IsRequired();

        builder.HasIndex(snapshot => new { snapshot.TenantId, snapshot.RepId, snapshot.PeriodStart, snapshot.Version }).IsUnique();
    }
}
