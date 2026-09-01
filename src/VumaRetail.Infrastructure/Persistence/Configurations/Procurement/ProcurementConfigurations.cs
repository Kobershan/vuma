using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Procurement;

/// <summary>
/// The thirteen <c>procurement</c> tables (Stage 12), grouped in one file the way <c>sales</c>'s seven
/// and <c>pos</c>'s five are — one module's schema read as a unit.
/// </summary>
/// <remarks>
/// <para>
/// Every partner, item, variant, user, stock-location and ledger-entry reference below is a plain
/// <see cref="Guid"/> column with an index, never a foreign key into <c>partners</c>, <c>catalog</c>,
/// <c>identity</c> or <c>inventory</c> — <c>CONVENTIONS.md</c> §2 forbids the cross-schema foreign key,
/// and <c>ProcurementPartners</c> validates the reference in the application layer instead. Foreign keys
/// <em>within</em> this schema are legal and used: a receipt line without its receipt, or a quote
/// without its RFQ, is meaningless.
/// </para>
/// <para>
/// The check constraints are not decoration. Every one of them re-asserts an invariant the aggregate
/// already enforces, and each exists because the figures involved are read by later reports that would
/// be silently wrong rather than loudly broken — a match whose parts do not add up produces a payment
/// run nobody questions.
/// </para>
/// </remarks>
internal sealed class PurchaseRequisitionConfiguration : EntityConfiguration<PurchaseRequisition>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "purchase_requisitions";

    protected override void ConfigureEntity(EntityTypeBuilder<PurchaseRequisition> builder)
    {
        builder.Property(requisition => requisition.RequisitionNumber).IsRequired().HasMaxLength(32);
        builder.Property(requisition => requisition.RequestedByUserId).IsRequired();
        builder.Property(requisition => requisition.LocationId);
        builder.Property(requisition => requisition.RequiredBy).IsRequired();
        builder.Property(requisition => requisition.Justification).IsRequired().HasMaxLength(1000);

        // Stored as text (docs/DATA_MODEL.md §2): an enum persisted by ordinal turns a reordered member
        // into silently relabelled history, and a rejected requisition reading as approved is not a
        // display bug.
        builder.Property(requisition => requisition.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(requisition => requisition.RaisedAt).IsRequired();
        builder.Property(requisition => requisition.SubmittedAt);
        builder.Property(requisition => requisition.DecidedByUserId);
        builder.Property(requisition => requisition.DecidedAt);
        builder.Property(requisition => requisition.RejectionReason).HasMaxLength(500);

        builder.HasMany(requisition => requisition.Lines)
            .WithOne()
            .HasForeignKey(line => line.PurchaseRequisitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(requisition => requisition.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(requisition => new { requisition.TenantId, requisition.RequisitionNumber })
            .IsUnique()
            .HasDatabaseName("ux_purchase_requisitions_tenant_id_number")
            .HasFilter("deleted_at IS NULL");

        // The approver's queue: "what is waiting for me", asked every morning.
        builder.HasIndex(requisition => new { requisition.Status, requisition.RequiredBy })
            .HasDatabaseName("ix_purchase_requisitions_status_required_by");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_purchase_requisitions_rejected_has_reason",
            "status <> 'Rejected' OR rejection_reason IS NOT NULL"));
    }
}

/// <summary><c>procurement.purchase_requisition_lines</c> — one thing the shop says it needs.</summary>
internal sealed class PurchaseRequisitionLineConfiguration : EntityConfiguration<PurchaseRequisitionLine>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "purchase_requisition_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<PurchaseRequisitionLine> builder)
    {
        builder.Property(line => line.PurchaseRequisitionId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);

        builder.HasQuantity(line => line.Quantity, "quantity");

        // ADR-067: the estimate is an optional monetary amount, which EF Core 9 cannot map as an
        // optional complex property. Two plain columns behind the computed Money? accessor.
        builder.Property(line => line.EstimatedUnitCostAmount)
            .HasColumnType(ValueObjectMapping.MoneyColumnType);

        builder.Property(line => line.EstimatedUnitCostCurrency).HasMaxLength(3).IsFixedLength();

        builder.Property(line => line.SourcedToDocumentId);
        builder.Property(line => line.SourcedAt);

        builder.HasIndex(line => line.PurchaseRequisitionId)
            .HasDatabaseName("ix_purchase_requisition_lines_requisition_id");

        builder.HasIndex(line => line.ItemId)
            .HasDatabaseName("ix_purchase_requisition_lines_item_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_purchase_requisition_lines_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            table.HasCheckConstraint("ck_purchase_requisition_lines_quantity_positive", "quantity_value > 0");

            // An amount with no currency is how a multi-currency system quietly adds rands to dollars
            // (§7 rule 4). Invisible until something tries to total the estimates.
            table.HasCheckConstraint(
                "ck_purchase_requisition_lines_estimate_currency_pairs",
                "(estimated_unit_cost_amount IS NULL) = (estimated_unit_cost_currency IS NULL)");

            table.HasCheckConstraint(
                "ck_purchase_requisition_lines_estimate_not_negative",
                "estimated_unit_cost_amount IS NULL OR estimated_unit_cost_amount >= 0");
        });
    }
}

/// <summary><c>procurement.rfqs</c> — the buyer asking suppliers what they would charge.</summary>
internal sealed class RfqConfiguration : EntityConfiguration<Rfq>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "rfqs";

    protected override void ConfigureEntity(EntityTypeBuilder<Rfq> builder)
    {
        builder.Property(rfq => rfq.RfqNumber).IsRequired().HasMaxLength(32);
        builder.Property(rfq => rfq.Title).IsRequired().HasMaxLength(256);
        builder.Property(rfq => rfq.PurchaseRequisitionId);
        builder.Property(rfq => rfq.ClosesAt).IsRequired();

        builder.Property(rfq => rfq.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(rfq => rfq.RaisedAt).IsRequired();
        builder.Property(rfq => rfq.IssuedAt);
        builder.Property(rfq => rfq.AwardedResponseId);
        builder.Property(rfq => rfq.AwardedAt);

        builder.HasMany(rfq => rfq.Lines)
            .WithOne()
            .HasForeignKey(line => line.RfqId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(rfq => rfq.Responses)
            .WithOne()
            .HasForeignKey(response => response.RfqId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(rfq => rfq.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(rfq => rfq.Responses).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(rfq => new { rfq.TenantId, rfq.RfqNumber })
            .IsUnique()
            .HasDatabaseName("ux_rfqs_tenant_id_number")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(rfq => new { rfq.Status, rfq.ClosesAt })
            .HasDatabaseName("ix_rfqs_status_closes_at");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_rfqs_awarded_has_response",
            "status <> 'Awarded' OR awarded_response_id IS NOT NULL"));
    }
}

/// <summary><c>procurement.rfq_lines</c> — one thing suppliers are being asked to price.</summary>
internal sealed class RfqLineConfiguration : EntityConfiguration<RfqLine>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "rfq_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<RfqLine> builder)
    {
        builder.Property(line => line.RfqId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);
        builder.Property(line => line.Specification).HasMaxLength(1000);
        builder.Property(line => line.PurchaseRequisitionLineId);

        builder.HasQuantity(line => line.Quantity, "quantity");

        builder.HasIndex(line => line.RfqId).HasDatabaseName("ix_rfq_lines_rfq_id");
        builder.HasIndex(line => line.ItemId).HasDatabaseName("ix_rfq_lines_item_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_rfq_lines_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            table.HasCheckConstraint("ck_rfq_lines_quantity_positive", "quantity_value > 0");
        });
    }
}

/// <summary><c>procurement.rfq_responses</c> — one supplier's quote, frozen on submission.</summary>
internal sealed class RfqResponseConfiguration : EntityConfiguration<RfqResponse>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "rfq_responses";

    protected override void ConfigureEntity(EntityTypeBuilder<RfqResponse> builder)
    {
        builder.Property(response => response.RfqId).IsRequired();
        builder.Property(response => response.PartnerId).IsRequired();

        builder.Property(response => response.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(response => response.QuotedAt).IsRequired();
        builder.Property(response => response.ValidUntil);
        builder.Property(response => response.LeadTimeDays).IsRequired();
        builder.Property(response => response.Notes).HasMaxLength(1000);

        builder.Property(response => response.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasMoney(response => response.Total, "total");

        builder.Property(response => response.AwardedByUserId);
        builder.Property(response => response.DecidedAt);

        builder.HasMany(response => response.Lines)
            .WithOne()
            .HasForeignKey(line => line.RfqResponseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(response => response.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Business rule 2's one-quote-per-supplier rule, as a database guarantee. The aggregate checks
        // it too; this catches the row written around the aggregate — and two quotes from one supplier
        // is how an award gets made against a price the supplier later says they never gave.
        builder.HasIndex(response => new { response.RfqId, response.PartnerId })
            .IsUnique()
            .HasDatabaseName("ux_rfq_responses_rfq_id_partner_id")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(response => response.PartnerId)
            .HasDatabaseName("ix_rfq_responses_partner_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_rfq_responses_lead_time_not_negative", "lead_time_days >= 0");
            table.HasCheckConstraint("ck_rfq_responses_total_not_negative", "total_amount >= 0");

            table.HasCheckConstraint(
                "ck_rfq_responses_awarded_has_user",
                "status <> 'Awarded' OR awarded_by_user_id IS NOT NULL");
        });
    }
}

/// <summary><c>procurement.rfq_response_lines</c> — what one supplier charges for one RFQ line.</summary>
internal sealed class RfqResponseLineConfiguration : EntityConfiguration<RfqResponseLine>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "rfq_response_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<RfqResponseLine> builder)
    {
        builder.Property(line => line.RfqResponseId).IsRequired();
        builder.Property(line => line.RfqLineId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);

        builder.HasQuantity(line => line.RequestedQuantity, "requested_quantity");
        builder.HasQuantity(line => line.QuotedQuantity, "quoted_quantity");
        builder.HasMoney(line => line.UnitCost, "unit_cost");
        builder.HasMoney(line => line.ExtendedCost, "extended_cost");

        // One price per line per quote. Two rows would let a supplier's total be whichever the reader
        // happened to sum first.
        builder.HasIndex(line => new { line.RfqResponseId, line.RfqLineId })
            .IsUnique()
            .HasDatabaseName("ux_rfq_response_lines_response_id_rfq_line_id")
            .HasFilter("deleted_at IS NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_rfq_response_lines_quoted_positive", "quoted_quantity_value > 0");
            table.HasCheckConstraint("ck_rfq_response_lines_cost_not_negative", "unit_cost_amount >= 0");
        });
    }
}

/// <summary><c>procurement.purchase_orders</c> — the commitment.</summary>
internal sealed class PurchaseOrderConfiguration : EntityConfiguration<PurchaseOrder>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "purchase_orders";

    protected override void ConfigureEntity(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.Property(order => order.OrderNumber).IsRequired().HasMaxLength(32);
        builder.Property(order => order.PartnerId).IsRequired();

        builder.Property(order => order.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(order => order.LocationId).IsRequired();
        builder.Property(order => order.ExpectedAt).IsRequired();

        builder.Property(order => order.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(order => order.Version).IsRequired();
        builder.Property(order => order.AmendsPurchaseOrderId);
        builder.Property(order => order.RfqResponseId);
        builder.Property(order => order.Notes).HasMaxLength(2000);

        builder.HasMoney(order => order.Net, "net");
        builder.HasMoney(order => order.Tax, "tax");
        builder.HasMoney(order => order.Gross, "gross");

        builder.Property(order => order.RaisedAt).IsRequired();
        builder.Property(order => order.ApprovedByUserId);
        builder.Property(order => order.ApprovedAt);
        builder.Property(order => order.IssuedAt);
        builder.Property(order => order.ClosedAt);
        builder.Property(order => order.CancelledAt);
        builder.Property(order => order.CancellationReason).HasMaxLength(500);

        builder.HasMany(order => order.Lines)
            .WithOne()
            .HasForeignKey(line => line.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(order => order.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(order => new { order.TenantId, order.OrderNumber })
            .IsUnique()
            .HasDatabaseName("ux_purchase_orders_tenant_id_number")
            .HasFilter("deleted_at IS NULL");

        // "What is still to come from this supplier" — the goods-in desk's question and the scorecard
        // calculator's read. Partial on the open set: a closed order is never a candidate for either and
        // a shop accumulates them.
        builder.HasIndex(order => new { order.PartnerId, order.ExpectedAt })
            .HasDatabaseName("ix_purchase_orders_partner_id_expected_at");

        builder.HasIndex(order => new { order.Status, order.ExpectedAt })
            .HasDatabaseName("ix_purchase_orders_status_expected_at");

        builder.HasIndex(order => order.AmendsPurchaseOrderId)
            .HasDatabaseName("ix_purchase_orders_amends_purchase_order_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_purchase_orders_version_positive", "version >= 1");

            table.HasCheckConstraint(
                "ck_purchase_orders_amounts_not_negative",
                "net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0");

            // The document's parts add up. Every payable report reads these three columns.
            table.HasCheckConstraint(
                "ck_purchase_orders_balances",
                "net_amount + tax_amount = gross_amount");

            table.HasCheckConstraint(
                "ck_purchase_orders_cancelled_has_reason",
                "status <> 'Cancelled' OR cancellation_reason IS NOT NULL");

            // An issued order is a document a supplier holds. It cannot have got there without approval.
            table.HasCheckConstraint(
                "ck_purchase_orders_issued_was_approved",
                "issued_at IS NULL OR approved_at IS NOT NULL");
        });
    }
}

/// <summary><c>procurement.purchase_order_lines</c> — one thing the shop committed to buy.</summary>
internal sealed class PurchaseOrderLineConfiguration : EntityConfiguration<PurchaseOrderLine>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "purchase_order_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.Property(line => line.PurchaseOrderId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);
        builder.Property(line => line.TaxCode).IsRequired().HasMaxLength(32);
        builder.Property(line => line.PurchaseRequisitionLineId);

        builder.HasQuantity(line => line.Quantity, "quantity");
        builder.HasQuantity(line => line.ReceivedQuantity, "received_quantity");
        builder.HasQuantity(line => line.RejectedQuantity, "rejected_quantity");
        builder.HasQuantity(line => line.InvoicedQuantity, "invoiced_quantity");

        builder.HasMoney(line => line.UnitCost, "unit_cost");
        builder.HasMoney(line => line.Net, "net");
        builder.HasMoney(line => line.Tax, "tax");
        builder.HasMoney(line => line.Gross, "gross");

        builder.HasIndex(line => line.PurchaseOrderId)
            .HasDatabaseName("ix_purchase_order_lines_purchase_order_id");

        builder.HasIndex(line => line.ItemId).HasDatabaseName("ix_purchase_order_lines_item_id");

        builder.HasIndex(line => line.PurchaseRequisitionLineId)
            .HasDatabaseName("ix_purchase_order_lines_requisition_line_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_purchase_order_lines_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            table.HasCheckConstraint("ck_purchase_order_lines_quantity_positive", "quantity_value > 0");

            table.HasCheckConstraint(
                "ck_purchase_order_lines_running_totals_not_negative",
                "received_quantity_value >= 0 AND rejected_quantity_value >= 0 "
                + "AND invoiced_quantity_value >= 0");

            table.HasCheckConstraint(
                "ck_purchase_order_lines_costs_not_negative",
                "unit_cost_amount >= 0 AND net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0");

            table.HasCheckConstraint(
                "ck_purchase_order_lines_balances",
                "net_amount + tax_amount = gross_amount");
        });
    }
}

/// <summary><c>procurement.goods_receipts</c> — the claim that the goods physically arrived.</summary>
internal sealed class GoodsReceiptConfiguration : EntityConfiguration<GoodsReceipt>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "goods_receipts";

    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.Property(receipt => receipt.PurchaseOrderId).IsRequired();
        builder.Property(receipt => receipt.PartnerId).IsRequired();
        builder.Property(receipt => receipt.ReceiptNumber).IsRequired().HasMaxLength(32);
        builder.Property(receipt => receipt.LocationId).IsRequired();

        builder.Property(receipt => receipt.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(receipt => receipt.DeliveryNoteNumber).HasMaxLength(64);
        builder.Property(receipt => receipt.ReceivedByUserId).IsRequired();
        builder.Property(receipt => receipt.ReceivedAt).IsRequired();

        builder.Property(receipt => receipt.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasMoney(receipt => receipt.ReceivedValue, "received_value");

        builder.Property(receipt => receipt.CompletedAt);
        builder.Property(receipt => receipt.CancelledAt);

        builder.HasMany(receipt => receipt.Lines)
            .WithOne()
            .HasForeignKey(line => line.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(receipt => receipt.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(receipt => new { receipt.TenantId, receipt.ReceiptNumber })
            .IsUnique()
            .HasDatabaseName("ux_goods_receipts_tenant_id_number")
            .HasFilter("deleted_at IS NULL");

        // The three-way match's middle leg: "what has actually arrived against this order".
        builder.HasIndex(receipt => receipt.PurchaseOrderId)
            .HasDatabaseName("ix_goods_receipts_purchase_order_id");

        builder.HasIndex(receipt => new { receipt.Status, receipt.ReceivedAt })
            .HasDatabaseName("ix_goods_receipts_status_received_at");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_goods_receipts_completed_has_timestamp",
                "status <> 'Completed' OR completed_at IS NOT NULL");

            table.HasCheckConstraint(
                "ck_goods_receipts_cancelled_has_timestamp",
                "status <> 'Cancelled' OR cancelled_at IS NOT NULL");

            table.HasCheckConstraint("ck_goods_receipts_value_not_negative", "received_value_amount >= 0");
        });
    }
}

/// <summary><c>procurement.goods_receipt_lines</c> — what arrived, and what was sent back.</summary>
internal sealed class GoodsReceiptLineConfiguration : EntityConfiguration<GoodsReceiptLine>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "goods_receipt_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        builder.Property(line => line.GoodsReceiptId).IsRequired();
        builder.Property(line => line.PurchaseOrderLineId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);

        builder.HasQuantity(line => line.OrderedQuantity, "ordered_quantity");
        builder.HasQuantity(line => line.AcceptedQuantity, "accepted_quantity");
        builder.HasQuantity(line => line.RejectedQuantity, "rejected_quantity");

        builder.Property(line => line.RejectionReason)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.HasMoney(line => line.UnitCost, "unit_cost");
        builder.HasMoney(line => line.AcceptedValue, "accepted_value");

        builder.Property(line => line.Note).HasMaxLength(500);

        builder.Property(line => line.StockPosting)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(line => line.StockLedgerEntryId);
        builder.Property(line => line.StockPostingNote).HasMaxLength(500);

        // One row per order line per receipt. Two rows for one order line is exactly how an over-receipt
        // slips past the per-line tolerance check: each passes on its own and the pair does not.
        builder.HasIndex(line => new { line.GoodsReceiptId, line.PurchaseOrderLineId })
            .IsUnique()
            .HasDatabaseName("ux_goods_receipt_lines_receipt_id_order_line_id")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(line => line.PurchaseOrderLineId)
            .HasDatabaseName("ix_goods_receipt_lines_purchase_order_line_id");

        // ADR-073's reconciliation queue, and it should stay empty — partial so it costs nothing when
        // it is.
        builder.HasIndex(line => line.StockPosting)
            .HasDatabaseName("ix_goods_receipt_lines_stock_posting_refused")
            .HasFilter("stock_posting = 'Refused' AND deleted_at IS NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_goods_receipt_lines_accepted_positive", "accepted_quantity_value > 0");

            table.HasCheckConstraint(
                "ck_goods_receipt_lines_rejected_not_negative", "rejected_quantity_value >= 0");

            // Both halves of the pairing (business rule 7). A rejected quantity with no reason cannot be
            // raised with the supplier, and a reason with nothing behind it is a scorecard entry for
            // something that did not happen.
            table.HasCheckConstraint(
                "ck_goods_receipt_lines_rejection_pairs",
                "(rejected_quantity_value = 0) = (rejection_reason = 'None')");

            table.HasCheckConstraint(
                "ck_goods_receipt_lines_cost_not_negative",
                "unit_cost_amount >= 0 AND accepted_value_amount >= 0");

            // A posted line names its ledger entry and a refused one explains itself. Either without the
            // other is a row nobody can reconcile.
            table.HasCheckConstraint(
                "ck_goods_receipt_lines_posting_evidence",
                "(stock_posting <> 'Posted' OR stock_ledger_entry_id IS NOT NULL) "
                + "AND (stock_posting <> 'Refused' OR stock_posting_note IS NOT NULL)");
        });
    }
}

/// <summary><c>procurement.supplier_invoice_matches</c> — the three-way match document.</summary>
internal sealed class SupplierInvoiceMatchConfiguration : EntityConfiguration<SupplierInvoiceMatch>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "supplier_invoice_matches";

    protected override void ConfigureEntity(EntityTypeBuilder<SupplierInvoiceMatch> builder)
    {
        builder.Property(match => match.PurchaseOrderId).IsRequired();
        builder.Property(match => match.PartnerId).IsRequired();
        builder.Property(match => match.SupplierInvoiceNumber).IsRequired().HasMaxLength(64);
        builder.Property(match => match.InvoiceDate).IsRequired();

        builder.Property(match => match.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.HasMoney(match => match.ClaimedNet, "claimed_net");
        builder.HasMoney(match => match.ClaimedTax, "claimed_tax");
        builder.HasMoney(match => match.ClaimedGross, "claimed_gross");
        builder.HasMoney(match => match.MatchedNet, "matched_net");
        builder.HasMoney(match => match.PriceVariance, "price_variance");
        builder.HasMoney(match => match.PriceToleranceFloor, "price_tolerance_floor");

        builder.Property(match => match.PriceTolerancePercentage)
            .IsRequired()
            .HasColumnType("numeric(9,4)");

        builder.Property(match => match.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // The flags enum stored as text too, for the reason every other enum here is: an ordinal
        // reordering would silently relabel every historical blocked match's cause.
        builder.Property(match => match.Variances)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.Property(match => match.MatchedAt).IsRequired();
        builder.Property(match => match.ReleasedByUserId);
        builder.Property(match => match.ReleasedAt);
        builder.Property(match => match.JournalId);

        builder.HasMany(match => match.Lines)
            .WithOne()
            .HasForeignKey(line => line.SupplierInvoiceMatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(match => match.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // One delivery paid for once. The application checks it too; this is what catches the row
        // written around the handler, and paying a supplier twice is not a bug anybody notices quickly.
        builder.HasIndex(match => new { match.PurchaseOrderId, match.SupplierInvoiceNumber })
            .IsUnique()
            .HasDatabaseName("ux_supplier_invoice_matches_order_id_invoice_number")
            .HasFilter("deleted_at IS NULL");

        // The AP work queue: "what is blocked", asked before every payment run.
        builder.HasIndex(match => new { match.Status, match.MatchedAt })
            .HasDatabaseName("ix_supplier_invoice_matches_status_matched_at");

        builder.HasIndex(match => match.PartnerId)
            .HasDatabaseName("ix_supplier_invoice_matches_partner_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_supplier_invoice_matches_claim_not_negative",
                "claimed_net_amount >= 0 AND claimed_tax_amount >= 0 AND claimed_gross_amount >= 0");

            table.HasCheckConstraint(
                "ck_supplier_invoice_matches_balances",
                "claimed_net_amount + claimed_tax_amount = claimed_gross_amount");

            table.HasCheckConstraint(
                "ck_supplier_invoice_matches_tolerance_range",
                "price_tolerance_percentage >= 0 AND price_tolerance_percentage <= 100 "
                + "AND price_tolerance_floor_amount >= 0");

            // Business rule 13, as a database guarantee. This is the constraint worth having above all
            // the others in this schema: a blocked match that reaches a payment run is money out the
            // door against a delivery nobody could evidence.
            table.HasCheckConstraint(
                "ck_supplier_invoice_matches_blocked_not_released",
                "status <> 'Blocked' OR released_at IS NULL");

            table.HasCheckConstraint(
                "ck_supplier_invoice_matches_released_has_user",
                "released_at IS NULL OR released_by_user_id IS NOT NULL");
        });
    }
}

/// <summary><c>procurement.supplier_invoice_match_lines</c> — ordered against received against invoiced.</summary>
internal sealed class SupplierInvoiceMatchLineConfiguration : EntityConfiguration<SupplierInvoiceMatchLine>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "supplier_invoice_match_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<SupplierInvoiceMatchLine> builder)
    {
        builder.Property(line => line.SupplierInvoiceMatchId).IsRequired();

        // Nullable: a line the supplier invoiced that the order does not have has nothing to point at,
        // and that case is the whole reason the column is not required.
        builder.Property(line => line.PurchaseOrderLineId);

        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);

        builder.HasQuantity(line => line.OrderedQuantity, "ordered_quantity");
        builder.HasQuantity(line => line.ReceivedQuantity, "received_quantity");
        builder.HasQuantity(line => line.InvoicedQuantity, "invoiced_quantity");
        builder.HasQuantity(line => line.PreviouslyInvoicedQuantity, "previously_invoiced_quantity");
        builder.HasQuantity(line => line.QuantityVariance, "quantity_variance");

        builder.HasMoney(line => line.OrderedUnitCost, "ordered_unit_cost");
        builder.HasMoney(line => line.InvoicedUnitCost, "invoiced_unit_cost");
        builder.HasMoney(line => line.OrderedValue, "ordered_value");
        builder.HasMoney(line => line.InvoicedValue, "invoiced_value");
        builder.HasMoney(line => line.PriceVariance, "price_variance");

        builder.Property(line => line.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(line => line.Variances)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.HasIndex(line => line.SupplierInvoiceMatchId)
            .HasDatabaseName("ix_supplier_invoice_match_lines_match_id");

        // The cumulative-invoiced read (business rule 11), which walks every released match's lines for
        // one order line.
        builder.HasIndex(line => line.PurchaseOrderLineId)
            .HasDatabaseName("ix_supplier_invoice_match_lines_purchase_order_line_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_supplier_invoice_match_lines_invoiced_positive", "invoiced_quantity_value > 0");

            table.HasCheckConstraint(
                "ck_supplier_invoice_match_lines_costs_not_negative",
                "ordered_unit_cost_amount >= 0 AND invoiced_unit_cost_amount >= 0");

            // The variance is the difference, not a third independently stored number. A row where it is
            // not would show a clean match against figures that disagree.
            table.HasCheckConstraint(
                "ck_supplier_invoice_match_lines_price_variance",
                "price_variance_amount = invoiced_value_amount - ordered_value_amount");

            // An unknown line has no order line and always blocks; an ordinary one has an order line.
            table.HasCheckConstraint(
                "ck_supplier_invoice_match_lines_unknown_blocks",
                "purchase_order_line_id IS NOT NULL OR status = 'Blocked'");
        });
    }
}

/// <summary><c>procurement.supplier_scorecards</c> — one supplier over one closed period, frozen.</summary>
internal sealed class SupplierScorecardConfiguration : EntityConfiguration<SupplierScorecard>
{
    protected override string Schema => Schemas.Procurement;

    protected override string TableName => "supplier_scorecards";

    protected override void ConfigureEntity(EntityTypeBuilder<SupplierScorecard> builder)
    {
        builder.Property(scorecard => scorecard.PartnerId).IsRequired();
        builder.Property(scorecard => scorecard.PeriodStart).IsRequired();
        builder.Property(scorecard => scorecard.PeriodEnd).IsRequired();

        builder.Property(scorecard => scorecard.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(scorecard => scorecard.OrdersPlaced).IsRequired();
        builder.Property(scorecard => scorecard.LinesOrdered).IsRequired();
        builder.Property(scorecard => scorecard.LinesDelivered).IsRequired();
        builder.Property(scorecard => scorecard.LinesDeliveredOnTime).IsRequired();
        builder.Property(scorecard => scorecard.LinesWithRejections).IsRequired();

        // Bare decimals rather than Quantity — see the entity's own remarks on why a scorecard's
        // absolute totals deliberately cross units of measure and are only ever read as a ratio.
        builder.Property(scorecard => scorecard.QuantityOrdered)
            .IsRequired()
            .HasColumnType(ValueObjectMapping.QuantityColumnType);

        builder.Property(scorecard => scorecard.QuantityReceived)
            .IsRequired()
            .HasColumnType(ValueObjectMapping.QuantityColumnType);

        builder.Property(scorecard => scorecard.QuantityRejected)
            .IsRequired()
            .HasColumnType(ValueObjectMapping.QuantityColumnType);

        builder.HasMoney(scorecard => scorecard.PurchaseValue, "purchase_value");
        builder.HasMoney(scorecard => scorecard.PriceVariance, "price_variance");

        builder.Property(scorecard => scorecard.OnTimeDeliveryRate)
            .IsRequired()
            .HasColumnType("numeric(9,4)");

        builder.Property(scorecard => scorecard.FillRate).IsRequired().HasColumnType("numeric(9,4)");
        builder.Property(scorecard => scorecard.QualityRate).IsRequired().HasColumnType("numeric(9,4)");
        builder.Property(scorecard => scorecard.OverallRating).IsRequired().HasColumnType("numeric(9,4)");

        builder.Property(scorecard => scorecard.SnapshottedAt).IsRequired();

        // ADR-084: one snapshot per supplier per period. A second row for the same window would mean two
        // different answers to the same question, and nothing to choose between them.
        builder.HasIndex(scorecard => new
        {
            scorecard.PartnerId,
            scorecard.PeriodStart,
            scorecard.PeriodEnd,
        })
            .IsUnique()
            .HasDatabaseName("ux_supplier_scorecards_partner_period")
            .HasFilter("deleted_at IS NULL");

        // The league table: every supplier for one period, best first.
        builder.HasIndex(scorecard => new { scorecard.PeriodStart, scorecard.PeriodEnd })
            .HasDatabaseName("ix_supplier_scorecards_period");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_supplier_scorecards_period_ordered", "period_end >= period_start");

            table.HasCheckConstraint(
                "ck_supplier_scorecards_counts_not_negative",
                "orders_placed >= 0 AND lines_ordered >= 0 AND lines_delivered >= 0 "
                + "AND lines_delivered_on_time >= 0 AND lines_with_rejections >= 0");

            // A rate outside 0–100 is not a rate. It is also the shape of every arithmetic slip a
            // percentage calculation makes.
            table.HasCheckConstraint(
                "ck_supplier_scorecards_rates_in_range",
                "on_time_delivery_rate BETWEEN 0 AND 100 AND fill_rate BETWEEN 0 AND 100 "
                + "AND quality_rate BETWEEN 0 AND 100 AND overall_rating BETWEEN 0 AND 100");

            table.HasCheckConstraint(
                "ck_supplier_scorecards_on_time_within_delivered",
                "lines_delivered_on_time <= lines_delivered");
        });
    }
}
