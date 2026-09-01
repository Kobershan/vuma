using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Pos;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Pos;

/// <summary>
/// The five <c>pos</c> tables (Stage 09), grouped in one file the way <c>inventory</c>'s six and
/// <c>catalog</c>'s four are — one module's schema read as a unit.
/// </summary>
/// <remarks>
/// Every item, variant, customer, terminal and user reference below is a plain <see cref="Guid"/>
/// column with an index, never a foreign key into <c>catalog</c>, <c>partners</c> or <c>identity</c> —
/// <c>CONVENTIONS.md</c> §2 forbids the cross-schema foreign key, and the application layer validates
/// the reference instead (<c>SellableItemResolver</c>). Foreign keys <em>within</em> this schema are
/// legal and used: a sale line without its sale is meaningless.
/// </remarks>
internal sealed class TillSessionConfiguration : EntityConfiguration<TillSession>
{
    protected override string Schema => Schemas.Pos;

    protected override string TableName => "till_sessions";

    protected override void ConfigureEntity(EntityTypeBuilder<TillSession> builder)
    {
        builder.Property(session => session.TerminalId).IsRequired();
        builder.Property(session => session.OperatorUserId).IsRequired();

        builder.Property(session => session.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.HasMoney(session => session.OpeningFloat, "opening_float");

        builder.Property(session => session.OpenedAt).IsRequired();

        // Stored as text (docs/DATA_MODEL.md §2): an enum persisted by ordinal turns a reordered member
        // into silently relabelled history.
        builder.Property(session => session.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        // ADR-067: the counted and expected amounts are meaningless until the session closes, so they
        // are two plain columns behind computed Money? accessors rather than optional complex
        // properties EF Core 9 cannot configure. The currency is the session's own, above.
        builder.Property<decimal>("_countedCashAmount")
            .HasColumnName("counted_cash_amount")
            .HasColumnType(ValueObjectMapping.MoneyColumnType)
            .IsRequired();

        builder.Property<decimal>("_expectedCashAmount")
            .HasColumnName("expected_cash_amount")
            .HasColumnType(ValueObjectMapping.MoneyColumnType)
            .IsRequired();

        builder.Property(session => session.ClosedAt);
        builder.Property(session => session.ClosedByUserId);
        builder.Property(session => session.Note).HasMaxLength(1000);

        // The module's hot lookup: "is this terminal trading?" — asked before every sale is opened and
        // before a second session can be created. Partial and unique together, so the
        // one-open-session-per-terminal rule is a database guarantee rather than only a check the
        // handler performs: two concurrent open requests on the same drawer collide here.
        builder.HasIndex(session => new { session.TenantId, session.TerminalId })
            .IsUnique()
            .HasDatabaseName("ux_till_sessions_tenant_id_terminal_id_open")
            .HasFilter("status = 'Open' AND deleted_at IS NULL");

        builder.HasIndex(session => new { session.TerminalId, session.OpenedAt })
            .HasDatabaseName("ix_till_sessions_terminal_id_opened_at")
            .IsDescending(false, true);
    }
}

/// <summary><c>pos.sales</c> — the aggregate root of the till.</summary>
internal sealed class SaleConfiguration : EntityConfiguration<Sale>
{
    protected override string Schema => Schemas.Pos;

    protected override string TableName => "sales";

    protected override void ConfigureEntity(EntityTypeBuilder<Sale> builder)
    {
        builder.Property(sale => sale.SaleNumber).IsRequired().HasMaxLength(32);
        builder.Property(sale => sale.TillSessionId).IsRequired();
        builder.Property(sale => sale.TerminalId).IsRequired();
        builder.Property(sale => sale.OperatorUserId).IsRequired();
        builder.Property(sale => sale.LocationId).IsRequired();
        builder.Property(sale => sale.CustomerId);

        builder.Property(sale => sale.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(sale => sale.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasMoney(sale => sale.Net, "net");
        builder.HasMoney(sale => sale.Tax, "tax");
        builder.HasMoney(sale => sale.Gross, "gross");
        builder.HasMoney(sale => sale.AmountTendered, "amount_tendered");
        builder.HasMoney(sale => sale.ChangeGiven, "change_given");

        builder.Property(sale => sale.OpenedAt).IsRequired();
        builder.Property(sale => sale.CompletedAt);
        builder.Property(sale => sale.VoidedAt);
        builder.Property(sale => sale.VoidReason).HasMaxLength(500);

        // ADR-066's shape, for the same reason: lines and tenders are full entities with their own
        // configuration and their own tables, reached through a backing-field navigation. They are not
        // owned collections — a sale line is queried directly by the reconciliation report, which an
        // owned type cannot be.
        builder.HasMany(sale => sale.Lines)
            .WithOne()
            .HasForeignKey(line => line.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(sale => sale.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(sale => sale.Tenders)
            .WithOne()
            .HasForeignKey(tender => tender.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(sale => sale.Tenders).UsePropertyAccessMode(PropertyAccessMode.Field);

        // LiveLines is a filtered view over Lines, not a second relationship — but it is a property
        // returning a collection of an entity type, which is all EF's conventions look at. Left
        // undeclared it is discovered as a second one-to-many onto sale_lines, and the failure is not
        // a modelling error at startup: the model builds, the migration is clean, and the first line
        // added to a *tracked* sale throws from inside change detection ("LiveLines is
        // ListWhereIterator<SaleLine> which does not implement ICollection<SaleLine>"). Ignoring it is
        // what makes the aggregate's convenience accessor stay a convenience accessor.
        builder.Ignore(sale => sale.LiveLines);

        builder.HasIndex(sale => new { sale.TenantId, sale.SaleNumber })
            .IsUnique()
            .HasDatabaseName("ux_sales_tenant_id_sale_number")
            .HasFilter("deleted_at IS NULL");

        // "What is still open on this session" — the check that blocks a cash-up, and the query that
        // derives the expected cash. Both start from the session.
        builder.HasIndex(sale => new { sale.TillSessionId, sale.Status })
            .HasDatabaseName("ix_sales_till_session_id_status");

        // "What is parked at this till" — the resume list, which a cashier opens on every customer who
        // comes back. Partial, because parked sales are a handful out of a day's trading.
        builder.HasIndex(sale => new { sale.TerminalId, sale.OpenedAt })
            .HasDatabaseName("ix_sales_terminal_id_parked")
            .HasFilter("status = 'Parked' AND deleted_at IS NULL");

        builder.HasIndex(sale => sale.CustomerId)
            .HasDatabaseName("ix_sales_customer_id")
            .HasFilter("customer_id IS NOT NULL");

        builder.ToTable(table =>
        {
            // A completed sale has a completion time and a voided one has a void reason. Asserted at
            // the boundary as well as in the aggregate, because these are the columns every later
            // report reads and a null in one of them would be silently wrong rather than loud.
            table.HasCheckConstraint(
                "ck_sales_completed_has_timestamp",
                "status <> 'Completed' OR completed_at IS NOT NULL");

            table.HasCheckConstraint(
                "ck_sales_voided_has_reason",
                "status <> 'Voided' OR (voided_at IS NOT NULL AND void_reason IS NOT NULL)");

            // Change comes out of the drawer, so it is never negative — an under-tendered sale does not
            // complete at all rather than completing with negative change.
            table.HasCheckConstraint("ck_sales_change_not_negative", "change_given_amount >= 0");
        });
    }
}

/// <summary><c>pos.sale_lines</c> — what was rung up.</summary>
internal sealed class SaleLineConfiguration : EntityConfiguration<SaleLine>
{
    protected override string Schema => Schemas.Pos;

    protected override string TableName => "sale_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<SaleLine> builder)
    {
        builder.Property(line => line.SaleId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);

        builder.HasQuantity(line => line.Quantity, "quantity");
        builder.HasMoney(line => line.UnitPrice, "unit_price");
        builder.HasMoney(line => line.DiscountAmount, "discount");
        builder.HasMoney(line => line.Net, "net");
        builder.HasMoney(line => line.Tax, "tax");
        builder.HasMoney(line => line.Gross, "gross");

        builder.Property(line => line.TaxCode).IsRequired().HasMaxLength(32);
        builder.Property(line => line.IsVoided).IsRequired();
        builder.Property(line => line.VoidedAt);

        builder.Property(line => line.StockIssue)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(line => line.StockLedgerEntryId);
        builder.Property(line => line.StockIssueNote).HasMaxLength(500);

        builder.HasIndex(line => new { line.SaleId, line.LineNumber })
            .IsUnique()
            .HasDatabaseName("ux_sale_lines_sale_id_line_number")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(line => line.ItemId)
            .HasDatabaseName("ix_sale_lines_item_id");

        builder.HasIndex(line => line.ItemVariantId)
            .HasDatabaseName("ix_sale_lines_item_variant_id");

        // The reconciliation queue ADR-073 creates. Partial on the refused state, because refusals are
        // rare by design — if this index ever grows large, that is itself the signal that something is
        // wrong with the stock data rather than with the index.
        builder.HasIndex(line => line.StockIssue)
            .HasDatabaseName("ix_sale_lines_stock_issue_refused")
            .HasFilter("stock_issue = 'Refused' AND deleted_at IS NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_sale_lines_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            // A return is a Stage 10 document, not a negative line here. Asserted so that a later stage
            // adding returns has to make a deliberate decision about this table rather than sliding a
            // negative quantity in.
            table.HasCheckConstraint("ck_sale_lines_quantity_positive", "quantity_value > 0");

            table.HasCheckConstraint("ck_sale_lines_discount_not_negative", "discount_amount >= 0");

            // The arithmetic the aggregate enforces, asserted at the boundary too. A line whose parts
            // do not add up would make every total downstream of it wrong.
            table.HasCheckConstraint("ck_sale_lines_balances", "net_amount + tax_amount = gross_amount");
        });
    }
}

/// <summary><c>pos.sale_tenders</c> — money that changed hands. Immutable (§7 rule 7).</summary>
internal sealed class SaleTenderConfiguration : EntityConfiguration<SaleTender>
{
    protected override string Schema => Schemas.Pos;

    protected override string TableName => "sale_tenders";

    protected override void ConfigureEntity(EntityTypeBuilder<SaleTender> builder)
    {
        builder.Property(tender => tender.SaleId).IsRequired();

        builder.Property(tender => tender.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.HasMoney(tender => tender.Amount, "amount");

        builder.Property(tender => tender.Reference).HasMaxLength(128);
        builder.Property(tender => tender.CapturedAt).IsRequired();

        builder.HasIndex(tender => tender.SaleId)
            .HasDatabaseName("ix_sale_tenders_sale_id");

        // "What cash did this shift take" starts here, from the tender rather than the sale, so a
        // future banking report does not have to load every sale to find out.
        builder.HasIndex(tender => new { tender.Type, tender.CapturedAt })
            .HasDatabaseName("ix_sale_tenders_type_captured_at");

        builder.ToTable(table => table.HasCheckConstraint("ck_sale_tenders_amount_positive", "amount_amount > 0"));
    }
}

/// <summary><c>pos.receipt_prints</c> — the append-only print and reprint log (R6).</summary>
internal sealed class ReceiptPrintConfiguration : EntityConfiguration<ReceiptPrint>
{
    protected override string Schema => Schemas.Pos;

    protected override string TableName => "receipt_prints";

    protected override void ConfigureEntity(EntityTypeBuilder<ReceiptPrint> builder)
    {
        builder.Property(print => print.SaleId).IsRequired();
        builder.Property(print => print.PrintedByUserId).IsRequired();
        builder.Property(print => print.TerminalId).IsRequired();
        builder.Property(print => print.IsReprint).IsRequired();
        builder.Property(print => print.Reason).HasMaxLength(500);
        builder.Property(print => print.PrintedAt).IsRequired();

        builder.HasIndex(print => new { print.SaleId, print.PrintedAt })
            .HasDatabaseName("ix_receipt_prints_sale_id_printed_at");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_receipt_prints_reprint_has_reason",
            "is_reprint = false OR reason IS NOT NULL"));
    }
}
