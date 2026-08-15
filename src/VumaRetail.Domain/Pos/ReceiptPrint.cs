using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Pos;

/// <summary>
/// One printing of a receipt. Append-only, one row per print, and every print after the first is a
/// reprint carrying a reason.
/// </summary>
/// <remarks>
/// <para>
/// This table exists because reprinting a receipt is the oldest till fraud there is — a second copy of
/// a real sale is what a refund scheme is built on. R6 says who changed what, when, from which
/// terminal, and a print is something somebody did even though it changes nothing.
/// </para>
/// <para>
/// <see cref="IImmutableRecord"/>, so the persistence layer refuses it in the <c>Modified</c> or
/// <c>Deleted</c> state (§7 rule 7). A print log that can be pruned by the person being logged is not
/// a log.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ReceiptPrint : Entity, IImmutableRecord
{
    private ReceiptPrint(
        Guid tenantId,
        Guid? storeId,
        Guid saleId,
        Guid printedByUserId,
        Guid terminalId,
        bool isReprint,
        string? reason,
        DateTimeOffset printedAt)
        : base(tenantId, storeId)
    {
        SaleId = saleId;
        PrintedByUserId = printedByUserId;
        TerminalId = terminalId;
        IsReprint = isReprint;
        Reason = reason;
        PrintedAt = printedAt;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ReceiptPrint()
    {
    }

    /// <summary>The sale whose receipt was printed.</summary>
    public Guid SaleId { get; private set; }

    /// <summary>Who printed it.</summary>
    public Guid PrintedByUserId { get; private set; }

    /// <summary>Which terminal it came out of.</summary>
    public Guid TerminalId { get; private set; }

    /// <summary>True for every print after the first.</summary>
    public bool IsReprint { get; private set; }

    /// <summary>Why it was reprinted. Required on a reprint, absent on the original.</summary>
    public string? Reason { get; private set; }

    /// <summary>When, UTC.</summary>
    public DateTimeOffset PrintedAt { get; private set; }

    /// <summary>Records a print.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store.</param>
    /// <param name="saleId">The sale.</param>
    /// <param name="printedByUserId">Who printed it.</param>
    /// <param name="terminalId">Which terminal.</param>
    /// <param name="isReprint">Whether the receipt has been printed before.</param>
    /// <param name="reason">Why it was reprinted. Required when <paramref name="isReprint"/> is true.</param>
    /// <param name="printedAt">When, UTC.</param>
    /// <exception cref="PosRuleException">A reprint carried no reason.</exception>
    public static ReceiptPrint Record(
        Guid tenantId,
        Guid? storeId,
        Guid saleId,
        Guid printedByUserId,
        Guid terminalId,
        bool isReprint,
        string? reason,
        DateTimeOffset printedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A receipt print must belong to a tenant.", nameof(tenantId));
        }

        if (saleId == Guid.Empty)
        {
            throw new ArgumentException("A receipt print must name the sale it printed.", nameof(saleId));
        }

        if (isReprint && string.IsNullOrWhiteSpace(reason))
        {
            throw new ReceiptReprintRequiresReasonException();
        }

        return new ReceiptPrint(
            tenantId,
            storeId,
            saleId,
            printedByUserId,
            terminalId,
            isReprint,
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            printedAt);
    }
}

/// <summary>A receipt was reprinted without saying why.</summary>
public sealed class ReceiptReprintRequiresReasonException()
    : DomainException(
        "POS_REPRINT_REQUIRES_REASON",
        "A reprint must carry a reason. A second copy of a real receipt is what a refund scheme is "
        + "built on, so who asked for it and why is the whole point of recording the print.");
