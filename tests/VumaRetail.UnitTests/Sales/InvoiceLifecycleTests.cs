using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Invoices;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The invoice's immutability contract: a draft that freezes on posting, with totals telescoped
/// from the lines' stored amounts (ADR-075, ADR-102). Posted is terminal — amend by credit note.
/// </summary>
public sealed class InvoiceLifecycleTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid CustomerId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_invoice_is_a_draft_in_the_company_with_zeroed_totals()
    {
        Invoice invoice = Draft();

        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.CompanyId.Should().Be(CompanyId);
        invoice.Net.Should().Be(new Money(0m, "ZAR"));
        invoice.PostedAt.Should().BeNull();
    }

    [Fact]
    public void Posting_sums_the_stored_line_amounts_and_stamps_the_instant()
    {
        Invoice invoice = Draft();
        invoice.AddLine(Line(unitPrice: 100m, quantity: 2m, discount: 10m, tax: 28.50m));
        invoice.AddLine(Line(unitPrice: 50m, quantity: 1m, discount: 0m, tax: 7.50m));

        invoice.Post(Now);

        invoice.Status.Should().Be(InvoiceStatus.Posted);
        invoice.PostedAt.Should().Be(Now);
        invoice.Net.Amount.Should().Be(240m);
        invoice.Tax.Amount.Should().Be(36m);
        invoice.Gross.Amount.Should().Be(276m);
    }

    [Fact]
    public void An_invoice_with_no_lines_cannot_post()
    {
        Invoice invoice = Draft();

        Action posting = () => invoice.Post(Now);

        posting.Should().Throw<InvoicesRuleException>();
        invoice.Status.Should().Be(InvoiceStatus.Draft);
    }

    [Fact]
    public void Posted_is_terminal_lines_freeze_and_posting_twice_is_refused()
    {
        Invoice invoice = Draft();
        invoice.AddLine(Line());
        invoice.Post(Now);

        Action postingAgain = () => invoice.Post(Now.AddMinutes(1));
        postingAgain.Should().Throw<InvoicesRuleException>();

        Action adding = () => invoice.AddLine(Line());
        adding.Should().Throw<InvoicesRuleException>();

        Action cancelling = () => invoice.Cancel();
        cancelling.Should().Throw<InvoicesRuleException>();

        // The first instants win: the freeze is the freeze.
        invoice.PostedAt.Should().Be(Now);
    }

    [Fact]
    public void Cancelling_abandons_a_draft_and_a_cancelled_invoice_stays_put()
    {
        Invoice invoice = Draft();
        invoice.AddLine(Line());

        invoice.Cancel();
        invoice.Status.Should().Be(InvoiceStatus.Cancelled);

        Action posting = () => invoice.Post(Now);
        posting.Should().Throw<InvoicesRuleException>();
    }

    [Fact]
    public void The_group_reference_travels_with_a_split_segment()
    {
        const string groupRef = "ORD-2026-0042";

        Invoice invoice = Invoice.Create(
            TenantId, StoreId, $"INV-{UuidV7.NewGuid():N}", CompanyId,
            UuidV7.NewGuid().ToString(), InvoiceSourceType.Order, CustomerId, "ZAR", groupRef);

        invoice.GroupDocumentRef.Should().Be(groupRef);
    }

    [Fact]
    public void A_line_names_exactly_one_sku_a_positive_quantity_and_a_pack_snapshot()
    {
        Guid invoiceId = UuidV7.NewGuid();

        Action neither = () => InvoiceLine.Create(
            TenantId, StoreId, invoiceId, null, null,
            1m, "EA", new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null);
        neither.Should().Throw<InvoicesRuleException>();

        Action zero = () => InvoiceLine.Create(
            TenantId, StoreId, invoiceId, ItemId, null,
            0m, "EA", new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null);
        zero.Should().Throw<InvoicesRuleException>();

        Action noPack = () => InvoiceLine.Create(
            TenantId, StoreId, invoiceId, ItemId, null,
            1m, "EA", new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "  ", "ZAR", null);
        noPack.Should().Throw<InvoicesRuleException>();
    }

    [Fact]
    public void An_invoice_needs_a_tenant_a_company_and_a_number()
    {
        Action noTenant = () => Invoice.Create(
            Guid.Empty, StoreId, "INV-1", CompanyId,
            "SRC-1", InvoiceSourceType.Sale, CustomerId, "ZAR");
        noTenant.Should().Throw<ArgumentException>();

        Action noCompany = () => Invoice.Create(
            TenantId, StoreId, "INV-1", Guid.Empty,
            "SRC-1", InvoiceSourceType.Sale, CustomerId, "ZAR");
        noCompany.Should().Throw<ArgumentException>();

        Action noNumber = () => Invoice.Create(
            TenantId, StoreId, "  ", CompanyId,
            "SRC-1", InvoiceSourceType.Sale, CustomerId, "ZAR");
        noNumber.Should().Throw<ArgumentException>();
    }

    private static Invoice Draft()
    {
        return Invoice.Create(
            TenantId, StoreId, $"INV-{UuidV7.NewGuid():N}", CompanyId,
            UuidV7.NewGuid().ToString(), InvoiceSourceType.Sale, CustomerId, "ZAR");
    }

    private static InvoiceLine Line(
        decimal unitPrice = 100m,
        decimal quantity = 1m,
        decimal discount = 0m,
        decimal tax = 15m)
    {
        return InvoiceLine.Create(
            TenantId, StoreId, UuidV7.NewGuid(), ItemId, null,
            quantity, "EA", new Money(unitPrice, "ZAR"), new Money(discount, "ZAR"),
            new Money(tax, "ZAR"), "Each", "ZAR", null);
    }
}
