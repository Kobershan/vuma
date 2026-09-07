using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Sales.Services;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Quotes;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The quote housekeeping service: open quotes are listed, and only lapsed issued quotes expire.
/// </summary>
public sealed class QuoteServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExpireDueAsync_expires_only_lapsed_issued_quotes()
    {
        // Issued while fresh, lapsed since — the exact quote the expiry job exists for.
        Quote lapsed = IssuedQuote(validUntil: Now.AddDays(-1), issuedAt: Now.AddDays(-2));
        Quote fresh = IssuedQuote(validUntil: Now.AddDays(5), issuedAt: Now);

        var quotes = Substitute.For<IQuoteRepository>();
        quotes.ListAsync(QuoteStatus.Issued, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                DateTimeOffset validUntil = call.ArgAt<DateTimeOffset>(1);
                return new List<Quote> { lapsed, fresh }
                    .Where(quote => quote.ValidUntil <= validUntil)
                    .ToList();
            });

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        await new QuoteService(quotes, clock).ExpireDueAsync();

        lapsed.Status.Should().Be(QuoteStatus.Expired);
        fresh.Status.Should().Be(QuoteStatus.Issued);
    }

    [Fact]
    public async Task GetOpenQuotesAsync_returns_the_customers_quotes()
    {
        Guid customerId = UuidV7.NewGuid();
        var quotes = Substitute.For<IQuoteRepository>();
        quotes.ListForCustomerAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new List<Quote> { IssuedQuote(Now.AddDays(3)) });

        var clock = Substitute.For<IClock>();

        IReadOnlyList<Quote> open = await new QuoteService(quotes, clock).GetOpenQuotesAsync(customerId);

        open.Should().HaveCount(1);
    }

    private static Quote IssuedQuote(DateTimeOffset validUntil, DateTimeOffset? issuedAt = null)
    {
        Quote quote = Quote.Create(
            UuidV7.NewGuid(), UuidV7.NewGuid(), $"QTE-{UuidV7.NewGuid():N}",
            UuidV7.NewGuid(), "ZAR", validUntil);
        quote.AddLine(QuoteLine.Create(
            quote.TenantId, quote.StoreId, quote.Id, UuidV7.NewGuid(), null, 1m, "EA",
            new Money(100m, "ZAR"), new Money(0m, "ZAR"), new Money(15m, "ZAR"),
            "Each", "ZAR", null, string.Empty));
        quote.Issue(issuedAt ?? Now);
        return quote;
    }
}
