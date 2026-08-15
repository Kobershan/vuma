using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using static VumaRetail.UnitTests.Finance.FinanceTestContext;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// The AR and AP sub-ledger documents' lifecycle: draft, posted, settled (CLAUDE.md §7 rule 7).
/// </summary>
/// <remarks>
/// An invoice is not <c>IImmutableRecord</c> and deliberately so — its outstanding balance
/// legitimately falls as receipts allocate. What is frozen at posting is its <em>lines</em>, and that
/// is enforced in the entity rather than by the persistence layer's blanket guard. These tests hold
/// that distinction in place, because "the invoice is mutable" and "the invoice's lines are mutable"
/// are one careless refactor apart.
/// </remarks>
public sealed class SubLedgerDocumentTests
{
    private static readonly PartnerId Customer = PartnerId.From(UuidV7.NewGuid());
    private static readonly PartnerId Supplier = PartnerId.From(UuidV7.NewGuid());

    private static ArInvoice DraftArInvoice()
        => ArInvoice.Draft(TenantId, StoreId, Customer, "INV-000001", Today, Today.AddDays(30), "ZAR");

    private static ArInvoice PostedArInvoice(decimal net = 100m, decimal tax = 15m)
    {
        ArInvoice invoice = DraftArInvoice();
        invoice.AddLine("Goods", Rand(net), "STANDARD", Rand(tax));
        invoice.Post(UuidV7.NewGuid());
        return invoice;
    }

    [Fact]
    public void Posting_an_invoice_totals_its_lines_and_opens_the_balance_at_the_total()
    {
        ArInvoice invoice = DraftArInvoice();
        invoice.AddLine("Goods", Rand(100m), "STANDARD", Rand(15m));
        invoice.AddLine("Delivery", Rand(50m), "STANDARD", Rand(7.50m));
        Guid journalId = UuidV7.NewGuid();

        invoice.Post(journalId);

        invoice.Status.Should().Be(DocumentStatus.Posted);
        invoice.Total.Should().Be(Rand(172.50m));
        invoice.OutstandingBalance.Should().Be(Rand(172.50m));
        invoice.JournalId.Should().Be(journalId);
    }

    [Fact]
    public void A_posted_invoice_refuses_a_new_line()
    {
        ArInvoice invoice = PostedArInvoice();

        Action adding = () => invoice.AddLine("Sneaked in later", Rand(1000m), "STANDARD", Rand(150m));

        adding.Should().Throw<DocumentNotDraftException>();
        invoice.Lines.Should().ContainSingle();
        invoice.Total.Should().Be(Rand(115m));
    }

    [Fact]
    public void A_posted_invoice_cannot_be_posted_again()
    {
        ArInvoice invoice = PostedArInvoice();

        Action reposting = () => invoice.Post(UuidV7.NewGuid());

        reposting.Should().Throw<DocumentNotDraftException>();
    }

    [Fact]
    public void An_invoice_with_no_lines_cannot_be_posted()
    {
        ArInvoice invoice = DraftArInvoice();

        Action posting = () => invoice.Post(UuidV7.NewGuid());

        posting.Should().Throw<DocumentHasNoLinesException>();
    }

    [Fact]
    public void Allocating_a_receipt_reduces_the_outstanding_balance()
    {
        ArInvoice invoice = PostedArInvoice();

        invoice.Allocate(Rand(15m));

        invoice.OutstandingBalance.Should().Be(Rand(100m));
        invoice.Status.Should().Be(DocumentStatus.Posted);
        invoice.Total.Should().Be(Rand(115m), "the total is what was invoiced and never changes");
    }

    [Fact]
    public void Allocating_the_whole_balance_settles_the_invoice()
    {
        ArInvoice invoice = PostedArInvoice();

        invoice.Allocate(Rand(115m));

        invoice.OutstandingBalance.Should().Be(Rand(0m));
        invoice.Status.Should().Be(DocumentStatus.Settled);
    }

    [Fact]
    public void Allocating_more_than_is_outstanding_is_refused()
    {
        // Over-allocation is how an AR control account silently stops matching its sub-ledger.
        ArInvoice invoice = PostedArInvoice();

        Action overAllocating = () => invoice.Allocate(Rand(115.01m));

        overAllocating.Should().Throw<OverAllocationException>();
        invoice.OutstandingBalance.Should().Be(Rand(115m));
    }

    [Fact]
    public void An_invoice_references_its_partner_by_a_bare_opaque_id_and_nothing_else()
    {
        // The Stage 06 boundary: Finance carries the id and never resolves it to a name. If a name,
        // a credit limit or a term ever appears on this entity, Finance has grown a second copy of
        // master data.
        ArInvoice invoice = PostedArInvoice();

        invoice.PartnerId.Should().Be(Customer);
        typeof(ArInvoice).GetProperties().Select(property => property.Name)
            .Should().NotContain(name =>
                name.Contains("PartnerName", StringComparison.Ordinal)
                || name.Contains("Customer", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ap_invoice_behaves_as_the_mirror_of_an_ar_invoice()
    {
        ApInvoice invoice = ApInvoice.Draft(
            TenantId, StoreId, Supplier, "SUPP-INV-77", Today, Today.AddDays(30), "ZAR");
        invoice.AddLine("Stock purchase", Rand(200m), "STANDARD", Rand(30m));

        invoice.Post(UuidV7.NewGuid());

        invoice.Status.Should().Be(DocumentStatus.Posted);
        invoice.Total.Should().Be(Rand(230m));
        invoice.OutstandingBalance.Should().Be(Rand(230m));

        invoice.Allocate(Rand(230m));

        invoice.Status.Should().Be(DocumentStatus.Settled);
    }

    [Fact]
    public void A_posted_ap_invoice_refuses_a_new_line()
    {
        ApInvoice invoice = ApInvoice.Draft(
            TenantId, StoreId, Supplier, "SUPP-INV-77", Today, Today.AddDays(30), "ZAR");
        invoice.AddLine("Stock purchase", Rand(200m), "STANDARD", Rand(30m));
        invoice.Post(UuidV7.NewGuid());

        Action adding = () => invoice.AddLine("Later", Rand(1m), "STANDARD", Rand(0m));

        adding.Should().Throw<DocumentNotDraftException>();
    }
}
