using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Sales.Quotes;
#pragma warning disable CS1591

namespace VumaRetail.Application.Sales.Services;

public sealed class QuoteService
{
    private readonly IQuoteRepository _quotes;
    private readonly IClock _clock;

    public QuoteService(IQuoteRepository quotes, IClock clock)
    {
        _quotes = quotes;
        _clock = clock;
    }

    public async Task<IReadOnlyList<Quote>> GetOpenQuotesAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _quotes.ListForCustomerAsync(customerId, cancellationToken);
    }

    public async Task ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow;
        var due = await _quotes.ListAsync(QuoteStatus.Issued, now, cancellationToken);
        foreach (var quote in due)
        {
            try
            {
                // Tracked entities: the pipeline's unit of work commits the transition. No Update
                // call — the same reason ISalesReturnRepository has none.
                quote.Expire();
            }
            catch
            {
                // Already terminal: the read and the write raced a customer accepting at the
                // last minute, and the acceptance wins.
            }
        }
    }
}
