using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using static VumaRetail.UnitTests.Finance.FinanceTestContext;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// Stage 07c finance-domain support: invoices re-open when a receipt reverses, and receipts
/// carry on-account (unapplied) slices for money received against no invoice yet.
/// </summary>
public sealed class GroupReceiptFinanceTests
{
    private static ArInvoice PostedInvoice(decimal total)
    {
        ArInvoice invoice = ArInvoice.Draft(
            TenantId, StoreId, PartnerId.From(UuidV7.NewGuid()), "INV-000001",
            Today, Today.AddDays(30), "ZAR");
        invoice.AddLine("Goods", Rand(total), "STANDARD", Rand(0m));
        invoice.Post(UuidV7.NewGuid());
        return invoice;
    }

    [Fact]
    public void Reinstate_restores_the_outstanding_balance()
    {
        ArInvoice invoice = PostedInvoice(1000m);
        invoice.Allocate(new Money(1000m, "ZAR"));

        invoice.Reinstate(new Money(1000m, "ZAR"));

        invoice.OutstandingBalance.Should().Be(new Money(1000m, "ZAR"));
        invoice.Status.Should().Be(DocumentStatus.Posted);
    }

    [Fact]
    public void Reinstate_is_capped_at_the_invoice_total()
    {
        ArInvoice invoice = PostedInvoice(1000m);
        invoice.Allocate(new Money(400m, "ZAR"));

        Action reinstate = () => invoice.Reinstate(new Money(700m, "ZAR"));

        reinstate.Should().Throw<ArgumentException>();
        invoice.OutstandingBalance.Should().Be(new Money(600m, "ZAR"));
    }

    [Fact]
    public void Reinstate_rejects_non_positive_amounts()
    {
        ArInvoice invoice = PostedInvoice(1000m);

        Action zero = () => invoice.Reinstate(Money.Zero("ZAR"));

        zero.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_accepts_an_on_account_slice_with_a_null_invoice()
    {
        Money amount = new(500m, "ZAR");

        ArReceipt receipt = ArReceipt.Record(
            TenantId, StoreId, PartnerId.From(UuidV7.NewGuid()), "ARREC-000001",
            Now, amount, UuidV7.NewGuid(), [(null, amount)]);

        receipt.Allocations.Should().ContainSingle();
        receipt.Allocations.Single().ArInvoiceId.Should().BeNull();
        receipt.Allocations.Single().Amount.Should().Be(amount);
    }

    [Fact]
    public void Record_mixes_targeted_and_on_account_slices_that_sum_to_the_amount()
    {
        Guid invoiceId = UuidV7.NewGuid();
        Money targeted = new(300m, "ZAR");
        Money onAccount = new(200m, "ZAR");

        ArReceipt receipt = ArReceipt.Record(
            TenantId, StoreId, PartnerId.From(UuidV7.NewGuid()), "ARREC-000002",
            Now, new Money(500m, "ZAR"), UuidV7.NewGuid(),
            [(invoiceId, targeted), (null, onAccount)]);

        receipt.Allocations.Should().HaveCount(2);
    }

    [Fact]
    public void Record_still_refuses_slices_that_do_not_sum_to_the_amount()
    {
        Action record = () => ArReceipt.Record(
            TenantId, StoreId, PartnerId.From(UuidV7.NewGuid()), "ARREC-000003",
            Now, new Money(500m, "ZAR"), UuidV7.NewGuid(), [(null, new Money(400m, "ZAR"))]);

        record.Should().Throw<ArgumentException>();
    }
}
