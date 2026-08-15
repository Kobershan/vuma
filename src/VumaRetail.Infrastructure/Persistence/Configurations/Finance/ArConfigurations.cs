using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Finance;

/// <summary><c>finance.ar_invoices</c> — the AR sub-ledger's own documents.</summary>
internal sealed class ArInvoiceConfiguration : EntityConfiguration<ArInvoice>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "ar_invoices";

    protected override void ConfigureEntity(EntityTypeBuilder<ArInvoice> builder)
    {
        builder.Property(invoice => invoice.PartnerId)
            .IsRequired()
            .HasConversion(id => id.Value, value => PartnerId.From(value));

        builder.Property(invoice => invoice.InvoiceNumber).IsRequired().HasMaxLength(32);
        builder.Property(invoice => invoice.InvoiceDate).IsRequired();
        builder.Property(invoice => invoice.DueDate).IsRequired();
        builder.Property(invoice => invoice.Currency).IsRequired().HasMaxLength(3).IsFixedLength();

        builder.Property(invoice => invoice.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(invoice => invoice.JournalId);

        builder.HasMoney(invoice => invoice.Total, "total");
        builder.HasMoney(invoice => invoice.OutstandingBalance, "outstanding_balance");

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.InvoiceNumber })
            .IsUnique()
            .HasDatabaseName("ux_ar_invoices_tenant_id_invoice_number");

        builder.HasIndex(invoice => new { invoice.PartnerId, invoice.Status })
            .HasDatabaseName("ix_ar_invoices_partner_id_status")
            .HasFilter("status <> 'Settled'");

        builder.HasMany(invoice => invoice.Lines)
            .WithOne()
            .HasForeignKey(line => line.ArInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(invoice => invoice.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>finance.ar_invoice_lines</c>.</summary>
internal sealed class ArInvoiceLineConfiguration : EntityConfiguration<ArInvoiceLine>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "ar_invoice_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<ArInvoiceLine> builder)
    {
        builder.Property(line => line.ArInvoiceId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);
        builder.Property(line => line.TaxCode).IsRequired().HasMaxLength(32);

        builder.HasMoney(line => line.NetAmount, "net");
        builder.HasMoney(line => line.TaxAmount, "tax");
        builder.HasMoney(line => line.GrossAmount, "gross");

        builder.HasIndex(line => line.ArInvoiceId)
            .HasDatabaseName("ix_ar_invoice_lines_ar_invoice_id");
    }
}

/// <summary><c>finance.ar_receipts</c> — immutable once recorded (CLAUDE.md §7 rule 7).</summary>
internal sealed class ArReceiptConfiguration : EntityConfiguration<ArReceipt>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "ar_receipts";

    protected override void ConfigureEntity(EntityTypeBuilder<ArReceipt> builder)
    {
        builder.Property(receipt => receipt.PartnerId)
            .IsRequired()
            .HasConversion(id => id.Value, value => PartnerId.From(value));

        builder.Property(receipt => receipt.ReceiptNumber).IsRequired().HasMaxLength(32);
        builder.Property(receipt => receipt.ReceivedAt).IsRequired();
        builder.Property(receipt => receipt.BankAccountId);
        builder.Property(receipt => receipt.JournalId).IsRequired();

        builder.HasMoney(receipt => receipt.Amount, "amount");

        builder.HasIndex(receipt => new { receipt.TenantId, receipt.ReceiptNumber })
            .IsUnique()
            .HasDatabaseName("ux_ar_receipts_tenant_id_receipt_number");

        builder.HasIndex(receipt => receipt.PartnerId)
            .HasDatabaseName("ix_ar_receipts_partner_id");

        builder.HasMany(receipt => receipt.Allocations)
            .WithOne()
            .HasForeignKey(allocation => allocation.ArReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(receipt => receipt.Allocations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>finance.ar_receipt_allocations</c>.</summary>
internal sealed class ArReceiptAllocationConfiguration : EntityConfiguration<ArReceiptAllocation>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "ar_receipt_allocations";

    protected override void ConfigureEntity(EntityTypeBuilder<ArReceiptAllocation> builder)
    {
        builder.Property(allocation => allocation.ArReceiptId).IsRequired();
        builder.Property(allocation => allocation.ArInvoiceId).IsRequired();

        builder.HasMoney(allocation => allocation.Amount, "amount");

        builder.HasIndex(allocation => allocation.ArReceiptId)
            .HasDatabaseName("ix_ar_receipt_allocations_ar_receipt_id");

        builder.HasIndex(allocation => allocation.ArInvoiceId)
            .HasDatabaseName("ix_ar_receipt_allocations_ar_invoice_id");
    }
}
