using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Orders;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Orders;

/// <summary>The four <c>orders</c> tables (Stage 14).</summary>
internal sealed class SalesOrderConfiguration : EntityConfiguration<SalesOrder>
{
    protected override string Schema => Schemas.Orders;

    protected override string TableName => "sales_orders";

    protected override void ConfigureEntity(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.Property(order => order.OrderNumber).IsRequired().HasMaxLength(32);
        builder.Property(order => order.PartnerId);

        builder.Property(order => order.Channel)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(order => order.FulfilmentType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(order => order.FulfillingLocationId).IsRequired();

        // Optional — present only for Delivery (business rule: DeliveryAddress is null for click & collect).
        builder.HasAddress(order => order.DeliveryAddress, "delivery_address", isRequired: false);

        builder.Property(order => order.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(order => order.PaymentStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(order => order.SettlingSaleId);
        builder.Property(order => order.SettlingCustomerAccountId);
        builder.Property(order => order.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(order => order.OrderDate).IsRequired();
        builder.Property(order => order.RequestedFulfilmentDate);
        builder.Property(order => order.IsRevenueRecognised).IsRequired();
        builder.Property(order => order.CancelledAt);
        builder.Property(order => order.CancelledBy).HasMaxLength(256);
        builder.Property(order => order.CancelReason).HasMaxLength(500);

        builder.HasMoney(order => order.Net, "net");
        builder.HasMoney(order => order.Tax, "tax");
        builder.HasMoney(order => order.Gross, "gross");

        builder.HasIndex(order => new { order.TenantId, order.OrderNumber })
            .IsUnique()
            .HasDatabaseName("ux_sales_orders_tenant_id_order_number")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(order => order.PartnerId).HasDatabaseName("ix_sales_orders_partner_id");
        builder.HasIndex(order => order.Status).HasDatabaseName("ix_sales_orders_status");
        builder.HasIndex(order => order.FulfillingLocationId).HasDatabaseName("ix_sales_orders_fulfilling_location_id");
    }
}

/// <summary><c>orders.sales_order_lines</c>.</summary>
internal sealed class SalesOrderLineConfiguration : EntityConfiguration<SalesOrderLine>
{
    protected override string Schema => Schemas.Orders;

    protected override string TableName => "sales_order_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.Property(line => line.SalesOrderId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);

        builder.HasQuantity(line => line.RequestedQuantity, "requested_quantity");
        builder.HasMoney(line => line.UnitPrice, "unit_price");
        builder.HasMoney(line => line.DiscountAmount, "discount_amount");
        builder.HasMoney(line => line.TaxAmount, "tax_amount");
        builder.Property(line => line.PriceListId);
        builder.Property(line => line.PromotionsSummary).IsRequired().HasMaxLength(1000);
        builder.HasQuantity(line => line.BackorderedQuantity, "backordered_quantity");

        builder.Property(line => line.LineStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.HasIndex(line => line.SalesOrderId).HasDatabaseName("ix_sales_order_lines_sales_order_id");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_sales_order_lines_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}

/// <summary><c>orders.sales_order_returns</c>.</summary>
internal sealed class SalesOrderReturnConfiguration : EntityConfiguration<SalesOrderReturn>
{
    protected override string Schema => Schemas.Orders;

    protected override string TableName => "sales_order_returns";

    protected override void ConfigureEntity(EntityTypeBuilder<SalesOrderReturn> builder)
    {
        builder.Property(orderReturn => orderReturn.SalesOrderId).IsRequired();
        builder.Property(orderReturn => orderReturn.ReturnNumber).IsRequired().HasMaxLength(32);
        builder.Property(orderReturn => orderReturn.Reason).IsRequired().HasMaxLength(500);
        builder.Property(orderReturn => orderReturn.AuthorisedByUserId).IsRequired();

        builder.Property(orderReturn => orderReturn.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(orderReturn => orderReturn.RefundStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasMoney(orderReturn => orderReturn.Net, "net");
        builder.HasMoney(orderReturn => orderReturn.Tax, "tax");
        builder.HasMoney(orderReturn => orderReturn.Gross, "gross");

        builder.Property(orderReturn => orderReturn.RaisedAt).IsRequired();
        builder.Property(orderReturn => orderReturn.CompletedAt);

        builder.HasIndex(orderReturn => new { orderReturn.TenantId, orderReturn.ReturnNumber })
            .IsUnique()
            .HasDatabaseName("ux_sales_order_returns_tenant_id_return_number")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(orderReturn => orderReturn.SalesOrderId).HasDatabaseName("ix_sales_order_returns_sales_order_id");
    }
}

/// <summary><c>orders.sales_order_return_lines</c>.</summary>
internal sealed class SalesOrderReturnLineConfiguration : EntityConfiguration<SalesOrderReturnLine>
{
    protected override string Schema => Schemas.Orders;

    protected override string TableName => "sales_order_return_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<SalesOrderReturnLine> builder)
    {
        builder.Property(line => line.SalesOrderReturnId).IsRequired();
        builder.Property(line => line.SalesOrderLineId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);

        builder.HasQuantity(line => line.Quantity, "quantity");
        builder.HasQuantity(line => line.FulfilledQuantity, "fulfilled_quantity");
        builder.Property(line => line.PreviouslyReturnedQuantity).HasColumnType(ValueObjectMapping.QuantityColumnType).IsRequired();

        builder.HasMoney(line => line.UnitPrice, "unit_price");
        builder.HasMoney(line => line.Net, "net");
        builder.HasMoney(line => line.Tax, "tax");
        builder.HasMoney(line => line.Gross, "gross");

        builder.Property(line => line.StockReturn)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(line => line.StockLedgerEntryId);
        builder.Property(line => line.StockReturnNote).HasMaxLength(500);

        builder.HasIndex(line => line.SalesOrderReturnId).HasDatabaseName("ix_sales_order_return_lines_sales_order_return_id");
        builder.HasIndex(line => line.SalesOrderLineId).HasDatabaseName("ix_sales_order_return_lines_sales_order_line_id");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_sales_order_return_lines_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}
