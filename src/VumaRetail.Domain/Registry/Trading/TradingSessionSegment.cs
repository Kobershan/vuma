using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry.Trading;

/// <summary>
/// One company's slice of a trading session — the future tax invoice (ADR-125).
/// </summary>
/// <remarks>
/// Its lines, its discounts, its tax, its rounding. The basket total is the sum of rounded
/// segment totals, never the rounding of a sum: each segment rounds its own lines and its
/// own total here, and nothing above this level rounds again.
/// </remarks>
public sealed class TradingSessionSegment
{
    private readonly List<TradingSessionLine> _lines = [];

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    public TradingSessionSegment()
    {
    }

    /// <summary>The segment's identity.</summary>
    public Guid Id { get; init; }

    /// <summary>The owning tenant.</summary>
    public Guid TenantId { get; init; }

    /// <summary>The session this segment belongs to.</summary>
    public Guid SessionId { get; init; }

    /// <summary>The company whose books this segment will land in.</summary>
    public Guid CompanyId { get; init; }

    /// <summary>Lifecycle state.</summary>
    public TradingSegmentStatus Status { get; private set; }

    /// <summary>This segment's share of the captured tender, value. Null until tendered
    /// (ADR-067: stored plain, exposed through <see cref="TenderAllocation"/>).</summary>
    public decimal? TenderAllocationAmount { get; private set; }

    /// <summary>This segment's share currency, paired with <see cref="TenderAllocationAmount"/>.</summary>
    public string? TenderAllocationCurrency { get; private set; }

    /// <summary>This segment's share of the captured tender. Null until tendered.</summary>
    public Money? TenderAllocation => TenderAllocationAmount is { } amount
        ? new Money(amount, TenderAllocationCurrency!)
        : null;

    /// <summary>How the allocation was derived (proportional rule or cashier override).</summary>
    public string? AllocationBasis { get; private set; }

    /// <summary>The posted sale's id. Set on completion.</summary>
    public Guid? ResultingSaleId { get; private set; }

    /// <summary>The posted invoice's id. Set on completion.</summary>
    public Guid? ResultingInvoiceId { get; private set; }

    /// <summary>The posted invoice's number. Set on completion.</summary>
    public string? ResultingInvoiceNumber { get; private set; }

    /// <summary>True once the segment was emptied and dropped (leaves no trace).</summary>
    public bool IsRemoved { get; private set; }

    /// <summary>Every line ever added, voided ones included.</summary>
    public IReadOnlyList<TradingSessionLine> Lines => _lines;

    /// <summary>The lines that count.</summary>
    public IEnumerable<TradingSessionLine> LiveLines => _lines.Where(line => !line.IsVoided);

    /// <summary>Segment net — the sum of rounded line nets.</summary>
    public Money Net => LiveLines
        .Select(line => line.Net)
        .Aggregate(Money.Zero(Currency), (sum, net) => sum + net);

    /// <summary>Segment tax — the sum of rounded line taxes, computed on this segment alone.</summary>
    public Money Tax => LiveLines
        .Select(line => line.TaxAmount)
        .Aggregate(Money.Zero(Currency), (sum, tax) => sum + tax);

    /// <summary>Segment gross — the sum of rounded line grosses. The basket sums these, never re-rounds.</summary>
    public Money Gross => LiveLines
        .Select(line => line.Gross)
        .Aggregate(Money.Zero(Currency), (sum, gross) => sum + gross);

    private string Currency => LiveLines.FirstOrDefault()?.Net.Currency
        ?? TenderAllocation?.Currency
        ?? "ZAR";

    /// <summary>Appends a priced line. Amounts arrive finalised (ADR-138) and are stored verbatim.</summary>
    public TradingSessionLine AddLine(
        Guid lineId,
        Guid tenantId,
        Guid sessionId,
        string barcode,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        decimal quantityValue,
        string quantityUom,
        Money unitPrice,
        Money discountAmount,
        string taxCode,
        Money taxAmount,
        Money net,
        string packSizeDescription,
        Guid? priceListId,
        string currency,
        DateTimeOffset addedAt)
    {
        var line = TradingSessionLine.Create(
            lineId, tenantId, sessionId, Id, CompanyId, barcode, itemId, itemVariantId,
            description, quantityValue, quantityUom, unitPrice, discountAmount, taxCode,
            taxAmount, net, packSizeDescription, priceListId, currency, addedAt);
        _lines.Add(line);
        return line;
    }

    /// <summary>Voids one line. Returns false when the line is not on this segment.</summary>
    public bool TryVoidLine(Guid lineId, DateTimeOffset voidedAt)
    {
        TradingSessionLine? line = _lines.FirstOrDefault(candidate => candidate.Id == lineId && !candidate.IsVoided);
        if (line is null)
        {
            return false;
        }

        line.Void(voidedAt);
        return true;
    }

    /// <summary>Fixes this segment's share of the tender.</summary>
    public void SetAllocation(Money share, string basis)
    {
        TenderAllocationAmount = share.Amount;
        TenderAllocationCurrency = share.Currency;
        AllocationBasis = basis;
        if (Status is TradingSegmentStatus.Building)
        {
            Status = TradingSegmentStatus.Tendered;
        }
    }

    /// <summary>Records the posted sale and invoice.</summary>
    public void MarkPosted(Guid saleId, Guid invoiceId, string invoiceNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);

        ResultingSaleId = saleId;
        ResultingInvoiceId = invoiceId;
        ResultingInvoiceNumber = invoiceNumber.Trim();
        Status = TradingSegmentStatus.Posted;
    }

    /// <summary>Records compensation (sale voided, receipt reversed, holds released).</summary>
    public void MarkCompensated()
    {
        Status = TradingSegmentStatus.Compensated;
    }

    /// <summary>Drops an emptied segment.</summary>
    public void MarkRemoved()
    {
        IsRemoved = true;
    }
}
