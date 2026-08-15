using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Finance;

/// <summary><c>finance.ap_invoices</c> — the AP sub-ledger's own documents, the mirror of AR.</summary>
internal sealed class ApInvoiceConfiguration : EntityConfiguration<ApInvoice>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "ap_invoices";

    protected override void ConfigureEntity(EntityTypeBuilder<ApInvoice> builder)
    {
        builder.Property(invoice => invoice.PartnerId)
            .IsRequired()
            .HasConversion(id => id.Value, value => PartnerId.From(value));

        builder.Property(invoice => invoice.SupplierInvoiceNumber).IsRequired().HasMaxLength(64);
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

        // A supplier's own invoice numbers are not unique tenant-wide (two suppliers may both use
        // "001"); uniqueness is per supplier, not per tenant.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.PartnerId, invoice.SupplierInvoiceNumber })
            .IsUnique()
            .HasDatabaseName("ux_ap_invoices_tenant_id_partner_id_supplier_invoice_number")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(invoice => new { invoice.PartnerId, invoice.Status })
            .HasDatabaseName("ix_ap_invoices_partner_id_status")
            .HasFilter("status <> 'Settled'");

        builder.HasMany(invoice => invoice.Lines)
            .WithOne()
            .HasForeignKey(line => line.ApInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(invoice => invoice.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>finance.ap_invoice_lines</c>.</summary>
internal sealed class ApInvoiceLineConfiguration : EntityConfiguration<ApInvoiceLine>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "ap_invoice_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<ApInvoiceLine> builder)
    {
        builder.Property(line => line.ApInvoiceId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);
        builder.Property(line => line.TaxCode).IsRequired().HasMaxLength(32);

        builder.HasMoney(line => line.NetAmount, "net");
        builder.HasMoney(line => line.TaxAmount, "tax");
        builder.HasMoney(line => line.GrossAmount, "gross");

        builder.HasIndex(line => line.ApInvoiceId)
            .HasDatabaseName("ix_ap_invoice_lines_ap_invoice_id");
    }
}

/// <summary><c>finance.ap_payments</c> — immutable once recorded, the mirror of <c>ar_receipts</c>.</summary>
internal sealed class ApPaymentConfiguration : EntityConfiguration<ApPayment>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "ap_payments";

    protected override void ConfigureEntity(EntityTypeBuilder<ApPayment> builder)
    {
        builder.Property(payment => payment.PartnerId)
            .IsRequired()
            .HasConversion(id => id.Value, value => PartnerId.From(value));

        builder.Property(payment => payment.PaymentNumber).IsRequired().HasMaxLength(32);
        builder.Property(payment => payment.PaidAt).IsRequired();
        builder.Property(payment => payment.BankAccountId);
        builder.Property(payment => payment.JournalId).IsRequired();

        builder.HasMoney(payment => payment.Amount, "amount");

        builder.HasIndex(payment => new { payment.TenantId, payment.PaymentNumber })
            .IsUnique()
            .HasDatabaseName("ux_ap_payments_tenant_id_payment_number");

        builder.HasIndex(payment => payment.PartnerId)
            .HasDatabaseName("ix_ap_payments_partner_id");

        builder.HasMany(payment => payment.Allocations)
            .WithOne()
            .HasForeignKey(allocation => allocation.ApPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(payment => payment.Allocations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>finance.ap_payment_allocations</c>.</summary>
internal sealed class ApPaymentAllocationConfiguration : EntityConfiguration<ApPaymentAllocation>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "ap_payment_allocations";

    protected override void ConfigureEntity(EntityTypeBuilder<ApPaymentAllocation> builder)
    {
        builder.Property(allocation => allocation.ApPaymentId).IsRequired();
        builder.Property(allocation => allocation.ApInvoiceId).IsRequired();

        builder.HasMoney(allocation => allocation.Amount, "amount");

        builder.HasIndex(allocation => allocation.ApPaymentId)
            .HasDatabaseName("ix_ap_payment_allocations_ap_payment_id");

        builder.HasIndex(allocation => allocation.ApInvoiceId)
            .HasDatabaseName("ix_ap_payment_allocations_ap_invoice_id");
    }
}
