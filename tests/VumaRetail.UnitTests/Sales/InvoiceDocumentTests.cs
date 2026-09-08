using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Invoices;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The invoice's immutability contract (ADR-012): a draft builds, a posted invoice is frozen, and
/// corrections go through credit notes — never through a mutation here.
/// </summary>
public sealed class InvoiceDocumentTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid CustomerId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_invoice_is_a_draft_with_zeroed_totals()
    {
        Invoice invoice = DraftInvoice();

        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.Net.Amount.Should().Be(0m);
        invoice.Tax.Amount.Should().Be(0m);
        invoice.Gross.Amount.Should().Be(0m);
        invoice.PostedAt.Should().BeNull();
        invoice.Lines.Should().BeEmpty();
    }

    [Fact]
    public void A_draft_builds_totals_from_its_stored_lines_and_posts_exactly_once()
    {
        Invoice invoice = DraftInvoice();

        invoice.AddLine(Line(invoice, quantity: 6m, unitPrice: 100m, discount: 60m, tax: 81m, pack: "6 x Case of 10"));
        invoice.AddLine(Line(invoice, quantity: 40m, unitPrice: 10m, discount: 0m, tax: 60m, pack: "Each"));

        // (6 × 100 − 60) + (40 × 10) = 940 net; 81 + 60 = 141 tax; 1081 gross.
        invoice.Net.Amount.Should().Be(940m);
        invoice.Tax.Amount.Should().Be(141m);
        invoice.Gross.Amount.Should().Be(1081m);

        invoice.Post(Now);
        invoice.Status.Should().Be(InvoiceStatus.Posted);
        invoice.PostedAt.Should().Be(Now);

        Action repost = () => invoice.Post(Now);
        repost.Should().Throw<InvoicesRuleException>().WithMessage("*draft*");
    }

    [Fact]
    public void Posting_an_empty_invoice_is_refused()
    {
        Invoice invoice = DraftInvoice();

        Action post = () => invoice.Post(Now);

        post.Should().Throw<InvoicesRuleException>().WithMessage("*at least one line*");
    }

    [Fact]
    public void A_posted_invoice_rejects_every_mutation()
    {
        Invoice invoice = DraftInvoice();
        invoice.AddLine(Line(invoice, 1m, 100m, 0m, 15m, "Each"));
        invoice.Post(Now);

        Action add = () => invoice.AddLine(Line(invoice, 1m, 10m, 0m, 1.50m, "Each"));
        add.Should().Throw<InvoicesRuleException>().WithMessage("*draft*");

        Action cancel = () => invoice.Cancel();
        cancel.Should().Throw<InvoicesRuleException>().WithMessage("*draft*");
    }

    [Fact]
    public void Cancelling_a_draft_works_once()
    {
        Invoice invoice = DraftInvoice();

        invoice.Cancel();
        invoice.Status.Should().Be(InvoiceStatus.Cancelled);

        Action again = () => invoice.Cancel();
        again.Should().Throw<InvoicesRuleException>();
    }

    [Fact]
    public void An_invoice_belongs_to_exactly_one_company()
    {
        Invoice invoice = DraftInvoice();

        invoice.CompanyId.Should().Be(CompanyId);

        Action noCompany = () => Invoice.Create(
            TenantId, StoreId, "INV-1", Guid.Empty, "SO-1",
            InvoiceSourceType.Order, CustomerId, "ZAR");
        noCompany.Should().Throw<ArgumentException>();

        Action noNumber = () => Invoice.Create(
            TenantId, StoreId, "  ", CompanyId, "SO-1",
            InvoiceSourceType.Order, CustomerId, "ZAR");
        noNumber.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_split_segment_carries_the_shared_group_reference()
    {
        Invoice invoice = DraftInvoice(groupRef: null);
        invoice.AddLine(Line(invoice, 60m, 100m, 0m, 900m, "6 x Case of 10"));

        invoice.SetGroupDocument("SO-2026-000412");

        invoice.GroupDocumentRef.Should().Be("SO-2026-000412");
    }

    [Fact]
    public void An_invoice_line_without_its_pack_size_is_not_a_legal_document()
    {
        Invoice invoice = DraftInvoice();

        foreach (string bad in new[] { "", "   " })
        {
            Action blank = () => Line(invoice, 1m, 100m, 0m, 15m, bad);
            blank.Should().Throw<InvoicesRuleException>().WithMessage("*pack size*");
        }
    }

    [Fact]
    public void An_invoice_line_needs_exactly_one_sku_and_a_positive_quantity()
    {
        Invoice invoice = DraftInvoice();

        Action neither = () => InvoiceLine.Create(
            TenantId, StoreId, invoice.Id, null, null, 1m, "EA",
            new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null);
        neither.Should().Throw<InvoicesRuleException>().WithMessage("*exactly one*");

        Action both = () => InvoiceLine.Create(
            TenantId, StoreId, invoice.Id, ItemId, Guid.NewGuid(), 1m, "EA",
            new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null);
        both.Should().Throw<InvoicesRuleException>();

        Action zero = () => InvoiceLine.Create(
            TenantId, StoreId, invoice.Id, ItemId, null, 0m, "EA",
            new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(0m, "ZAR"),
            "Each", "ZAR", null);
        zero.Should().Throw<InvoicesRuleException>().WithMessage("*greater than zero*");
    }

    private static Invoice DraftInvoice(string? groupRef = "SO-2026-000412")
        => Invoice.Create(
            TenantId, StoreId, $"INV-{UuidV7.NewGuid():N}", CompanyId, "order-1",
            InvoiceSourceType.Order, CustomerId, "ZAR", groupRef);

    private static InvoiceLine Line(
        Invoice invoice, decimal quantity, decimal unitPrice, decimal discount, decimal tax, string pack)
        => InvoiceLine.Create(
            TenantId, StoreId, invoice.Id, ItemId, null, quantity, "EA",
            new Money(unitPrice, "ZAR"), new Money(discount, "ZAR"), new Money(tax, "ZAR"),
            pack, "ZAR", null);
}
