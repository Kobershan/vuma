using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Quotes;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The quote's promises: a price frozen at creation, a lifecycle with no shortcuts, and a
/// conversion that can happen exactly once.
/// </summary>
public sealed class QuoteLifecycleTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid CustomerId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_quote_is_a_draft_with_zeroed_totals()
    {
        Quote quote = DraftQuote();

        quote.Status.Should().Be(QuoteStatus.Draft);
        quote.Net.Amount.Should().Be(0m);
        quote.Tax.Amount.Should().Be(0m);
        quote.Gross.Amount.Should().Be(0m);
        quote.Lines.Should().BeEmpty();
    }

    [Fact]
    public void A_quote_needs_a_tenant_a_customer_and_a_number()
    {
        Action noTenant = () => Quote.Create(
            Guid.Empty, StoreId, "QTE-1", CustomerId, "ZAR", Now.AddDays(7));
        noTenant.Should().Throw<ArgumentException>();

        Action noCustomer = () => Quote.Create(
            TenantId, StoreId, "QTE-1", Guid.Empty, "ZAR", Now.AddDays(7));
        noCustomer.Should().Throw<ArgumentException>();

        Action noNumber = () => Quote.Create(
            TenantId, StoreId, "  ", CustomerId, "ZAR", Now.AddDays(7));
        noNumber.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_quote_walks_draft_issued_accepted_converted_with_totals_from_its_stored_lines()
    {
        Quote quote = DraftQuote();

        quote.AddLine(Line(quote, quantity: 2m, unitPrice: 100m, discount: 10m, tax: 28.50m));
        quote.AddLine(Line(quote, quantity: 1m, unitPrice: 50m, discount: 0m, tax: 7.50m));

        // (2 × 100 − 10) + (1 × 50) = 240 net; 28.50 + 7.50 = 36 tax; 276 gross.
        quote.Net.Amount.Should().Be(240m);
        quote.Tax.Amount.Should().Be(36m);
        quote.Gross.Amount.Should().Be(276m);

        quote.Issue(Now);
        quote.Status.Should().Be(QuoteStatus.Issued);

        quote.Accept(Now);
        quote.Status.Should().Be(QuoteStatus.Accepted);

        quote.MarkConverted();
        quote.Status.Should().Be(QuoteStatus.Converted);
    }

    [Fact]
    public void Issuing_an_empty_quote_is_refused()
    {
        Quote quote = DraftQuote();

        Action issue = () => quote.Issue(Now);

        issue.Should().Throw<QuotesRuleException>().WithMessage("*at least one line*");
    }

    [Fact]
    public void Issuing_after_the_validity_window_is_refused()
    {
        Quote quote = DraftQuote(validUntil: Now.AddDays(-1));
        quote.AddLine(Line(quote, 1m, 100m, 0m, 15m));

        Action issue = () => quote.Issue(Now);

        issue.Should().Throw<QuotesRuleException>().WithMessage("*validity*");
    }

    [Fact]
    public void Accepting_a_draft_is_refused()
    {
        Quote quote = DraftQuote();
        quote.AddLine(Line(quote, 1m, 100m, 0m, 15m));

        Action accept = () => quote.Accept(Now);

        accept.Should().Throw<QuotesRuleException>().WithMessage("*Draft to Accepted*");
    }

    [Fact]
    public void Accepting_after_expiry_is_refused()
    {
        Quote quote = DraftQuote();
        quote.AddLine(Line(quote, 1m, 100m, 0m, 15m));
        quote.Issue(Now);

        Action accept = () => quote.Accept(quote.ValidUntil.AddSeconds(1));

        accept.Should().Throw<QuotesRuleException>().WithMessage("*validity*");
    }

    [Fact]
    public void A_second_acceptance_is_refused()
    {
        Quote quote = IssuedQuote();

        quote.Accept(Now);

        Action again = () => quote.Accept(Now);

        again.Should().Throw<QuotesRuleException>();
    }

    [Fact]
    public void Rejecting_a_draft_is_refused_but_rejecting_an_issued_quote_works()
    {
        Quote draft = DraftQuote();
        draft.AddLine(Line(draft, 1m, 100m, 0m, 15m));

        Action rejectDraft = () => draft.Reject();
        rejectDraft.Should().Throw<QuotesRuleException>();

        Quote issued = IssuedQuote();
        issued.Reject();
        issued.Status.Should().Be(QuoteStatus.Rejected);
    }

    [Fact]
    public void Expiring_a_terminal_quote_is_refused()
    {
        Quote quote = IssuedQuote();
        quote.Accept(Now);

        Action expire = () => quote.Expire();

        expire.Should().Throw<QuotesRuleException>();
        quote.Status.Should().Be(QuoteStatus.Accepted);
    }

    [Fact]
    public void Only_an_accepted_quote_converts_so_one_acceptance_can_never_become_two_orders()
    {
        Quote issued = IssuedQuote();

        Action convertEarly = () => issued.MarkConverted();
        convertEarly.Should().Throw<QuotesRuleException>();

        issued.Accept(Now);
        issued.MarkConverted();

        Action convertAgain = () => issued.MarkConverted();
        convertAgain.Should().Throw<QuotesRuleException>();
    }

    [Fact]
    public void Adding_a_line_after_issue_is_refused()
    {
        Quote quote = IssuedQuote();

        Action add = () => quote.AddLine(Line(quote, 1m, 10m, 0m, 1.50m));

        add.Should().Throw<QuotesRuleException>().WithMessage("*draft*");
    }

    [Fact]
    public void A_quote_line_needs_exactly_one_sku_and_a_positive_quantity()
    {
        Quote quote = DraftQuote();

        Action neither = () => QuoteLine.Create(
            TenantId, StoreId, quote.Id, null, null, 1m, "EA",
            new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null, string.Empty);
        neither.Should().Throw<QuotesRuleException>().WithMessage("*exactly one*");

        Action both = () => QuoteLine.Create(
            TenantId, StoreId, quote.Id, ItemId, Guid.NewGuid(), 1m, "EA",
            new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(1.50m, "ZAR"),
            "Each", "ZAR", null, string.Empty);
        both.Should().Throw<QuotesRuleException>();

        Action zero = () => QuoteLine.Create(
            TenantId, StoreId, quote.Id, ItemId, null, 0m, "EA",
            new Money(10m, "ZAR"), new Money(0m, "ZAR"), new Money(0m, "ZAR"),
            "Each", "ZAR", null, string.Empty);
        zero.Should().Throw<QuotesRuleException>().WithMessage("*greater than zero*");
    }

    private static Quote DraftQuote(DateTimeOffset? validUntil = null)
    {
        Quote quote = Quote.Create(
            TenantId, StoreId, $"QTE-{UuidV7.NewGuid():N}", CustomerId, "ZAR",
            validUntil ?? Now.AddDays(7), groupId: null, CompanyId);
        quote.CompanyId.Should().Be(CompanyId);
        return quote;
    }

    private static Quote IssuedQuote()
    {
        Quote quote = DraftQuote();
        quote.AddLine(Line(quote, 1m, 100m, 0m, 15m));
        quote.Issue(Now);
        return quote;
    }

    private static QuoteLine Line(Quote quote, decimal quantity, decimal unitPrice, decimal discount, decimal tax)
        => QuoteLine.Create(
            TenantId, StoreId, quote.Id, ItemId, null, quantity, "EA",
            new Money(unitPrice, "ZAR"), new Money(discount, "ZAR"), new Money(tax, "ZAR"),
            "Each", "ZAR", null, string.Empty);
}
