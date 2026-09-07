using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Quotes;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The quote's lifecycle and snapshot invariants: a promise of price with an expiry, never a
/// promise of stock. Totals are recomputed from the lines' stored amounts, never re-resolved.
/// </summary>
public sealed class QuoteLifecycleTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CustomerId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_quote_is_a_draft_with_zeroed_totals_and_the_company_stamped()
    {
        Quote quote = Draft();

        quote.Status.Should().Be(QuoteStatus.Draft);
        quote.Net.Should().Be(new Money(0m, "ZAR"));
        quote.Tax.Should().Be(new Money(0m, "ZAR"));
        quote.Gross.Should().Be(new Money(0m, "ZAR"));
        quote.CompanyId.Should().Be(CompanyId);
        quote.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Adding_lines_recomputes_totals_from_the_stored_amounts()
    {
        // Line 1: 2 x R100 less R10 discount, R28.50 stored tax. Line 2: 1 x R50, R7.50 stored tax.
        // Net R240, tax R36, gross R276 — all from the snapshots, nothing re-resolved.
        Quote quote = Draft();
        quote.AddLine(Line(unitPrice: 100m, quantity: 2m, discount: 10m, tax: 28.50m));
        quote.AddLine(Line(unitPrice: 50m, quantity: 1m, discount: 0m, tax: 7.50m));

        quote.Net.Amount.Should().Be(240m);
        quote.Tax.Amount.Should().Be(36m);
        quote.Gross.Amount.Should().Be(276m);
    }

    [Fact]
    public void A_quote_with_no_lines_cannot_be_issued()
    {
        Quote quote = Draft();

        Action issuing = () => quote.Issue(Now);

        issuing.Should().Throw<QuotesRuleException>();
        quote.Status.Should().Be(QuoteStatus.Draft);
    }

    [Fact]
    public void An_expired_quote_cannot_be_issued_or_accepted()
    {
        Quote quote = Draft(validUntil: Now.AddDays(-1));
        quote.AddLine(Line());

        Action issuing = () => quote.Issue(Now);
        issuing.Should().Throw<QuotesRuleException>().WithMessage("*validity period*");
    }

    [Fact]
    public void Issue_then_accept_then_convert_is_the_only_path_to_a_trade()
    {
        Quote quote = Draft();
        quote.AddLine(Line());

        quote.Issue(Now);
        quote.Status.Should().Be(QuoteStatus.Issued);

        quote.Accept(Now);
        quote.Status.Should().Be(QuoteStatus.Accepted);

        quote.MarkConverted();
        quote.Status.Should().Be(QuoteStatus.Converted);
    }

    [Fact]
    public void Accepting_twice_or_converting_without_acceptance_is_refused()
    {
        Quote quote = Draft();
        quote.AddLine(Line());
        quote.Issue(Now);

        Action convertingEarly = () => quote.MarkConverted();
        convertingEarly.Should().Throw<QuotesRuleException>();

        quote.Accept(Now);

        Action acceptingAgain = () => quote.Accept(Now);
        acceptingAgain.Should().Throw<QuotesRuleException>();

        quote.MarkConverted();

        Action convertingAgain = () => quote.MarkConverted();
        convertingAgain.Should().Throw<QuotesRuleException>();
    }

    [Fact]
    public void Reject_and_expire_are_terminal_and_reject_needs_an_issued_quote()
    {
        Quote draft = Draft();
        draft.AddLine(Line());

        Action rejectingDraft = () => draft.Reject();
        rejectingDraft.Should().Throw<QuotesRuleException>();

        draft.Issue(Now);
        draft.Reject();
        draft.Status.Should().Be(QuoteStatus.Rejected);

        Action expiringRejected = () => draft.Expire();
        expiringRejected.Should().Throw<QuotesRuleException>();
    }

    [Fact]
    public void Lines_cannot_be_added_once_the_quote_has_left_draft()
    {
        Quote quote = Draft();
        quote.AddLine(Line());
        quote.Issue(Now);

        Action adding = () => quote.AddLine(Line());

        adding.Should().Throw<QuotesRuleException>();
    }

    [Fact]
    public void A_line_names_exactly_one_of_an_item_or_a_variant_and_a_positive_quantity()
    {
        Action neither = () => QuoteLine.Create(
            TenantId, StoreId, UuidV7.NewGuid(), null, null,
            1m, "EA", new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null, string.Empty);
        neither.Should().Throw<QuotesRuleException>();

        Action both = () => QuoteLine.Create(
            TenantId, StoreId, UuidV7.NewGuid(), ItemId, UuidV7.NewGuid(),
            1m, "EA", new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null, string.Empty);
        both.Should().Throw<QuotesRuleException>();

        Action zero = () => QuoteLine.Create(
            TenantId, StoreId, UuidV7.NewGuid(), ItemId, null,
            0m, "EA", new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null, string.Empty);
        zero.Should().Throw<QuotesRuleException>();
    }

    [Fact]
    public void A_quote_needs_a_tenant_a_customer_and_a_number()
    {
        Action noTenant = () => Quote.Create(
            Guid.Empty, StoreId, "QTE-1", CustomerId, "ZAR", Now.AddDays(30));
        noTenant.Should().Throw<ArgumentException>();

        Action noCustomer = () => Quote.Create(
            TenantId, StoreId, "QTE-1", Guid.Empty, "ZAR", Now.AddDays(30));
        noCustomer.Should().Throw<ArgumentException>();

        Action noNumber = () => Quote.Create(
            TenantId, StoreId, "  ", CustomerId, "ZAR", Now.AddDays(30));
        noNumber.Should().Throw<ArgumentException>();
    }

    private static Quote Draft(DateTimeOffset? validUntil = null)
    {
        return Quote.Create(
            TenantId, StoreId, $"QTE-{UuidV7.NewGuid():N}", CustomerId, "ZAR",
            validUntil ?? Now.AddDays(30), null, CompanyId);
    }

    private static QuoteLine Line(
        decimal unitPrice = 100m,
        decimal quantity = 1m,
        decimal discount = 0m,
        decimal tax = 15m)
    {
        return QuoteLine.Create(
            TenantId, StoreId, UuidV7.NewGuid(), ItemId, null,
            quantity, "EA", new Money(unitPrice, "ZAR"), new Money(discount, "ZAR"),
            new Money(tax, "ZAR"), "Each", "ZAR", null, string.Empty);
    }
}
