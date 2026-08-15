using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Pos;

/// <summary>
/// One payment taken against a sale. Money that changed hands.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IImmutableRecord"/>, so the persistence layer refuses this entity in the
/// <c>Modified</c> or <c>Deleted</c> state (§7 rule 7). A tender captured in error is corrected by
/// another tender, never by an edit — the same relationship a journal has with its reversal. This is
/// the row a dispute is settled from, and a row that can be edited settles nothing.
/// </para>
/// <para>
/// <see cref="TenderType.CustomerAccount"/> is declared here and settled nowhere. ADR-055 gave money
/// held on behalf of a customer its own module at Stage 10b; this stage records that a sale was put on
/// account and stops there, rather than inventing a credit control it would have to unbuild.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class SaleTender : Entity, IImmutableRecord
{
    private SaleTender(
        Guid tenantId,
        Guid? storeId,
        Guid saleId,
        TenderType type,
        Money amount,
        string? reference,
        DateTimeOffset capturedAt)
        : base(tenantId, storeId)
    {
        SaleId = saleId;
        Type = type;
        Amount = amount;
        Reference = reference;
        CapturedAt = capturedAt;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SaleTender()
    {
    }

    /// <summary>The sale this paid for.</summary>
    public Guid SaleId { get; private set; }

    /// <summary>How it was paid.</summary>
    public TenderType Type { get; private set; }

    /// <summary>How much was taken. Always positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>
    /// The reference the tender left behind — a card authorisation code, a voucher serial, a mobile
    /// money transaction id. <c>null</c> for cash, which leaves none.
    /// </summary>
    public string? Reference { get; private set; }

    /// <summary>When the money changed hands, UTC.</summary>
    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>True when this tender is cash, and therefore part of what the drawer should hold.</summary>
    public bool IsCash => Type is TenderType.Cash;

    /// <summary>Captures a payment.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the sale happened at.</param>
    /// <param name="saleId">The sale being paid for.</param>
    /// <param name="type">How it was paid.</param>
    /// <param name="amount">How much. Must be positive.</param>
    /// <param name="reference">The reference the tender left behind, if any.</param>
    /// <param name="capturedAt">When, UTC.</param>
    /// <exception cref="PosRuleException">The amount is zero or negative.</exception>
    public static SaleTender Capture(
        Guid tenantId,
        Guid? storeId,
        Guid saleId,
        TenderType type,
        Money amount,
        string? reference,
        DateTimeOffset capturedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tender must belong to a tenant.", nameof(tenantId));
        }

        if (saleId == Guid.Empty)
        {
            throw new ArgumentException("A tender must belong to a sale.", nameof(saleId));
        }

        if (amount.Amount <= 0m)
        {
            throw PosRuleException.MustBePositive("tender amount");
        }

        return new SaleTender(
            tenantId,
            storeId,
            saleId,
            type,
            amount,
            string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            capturedAt);
    }
}

/// <summary>How a sale was paid for.</summary>
public enum TenderType
{
    /// <summary>Notes and coins. The only tender that produces change, and the only one the cash-up counts.</summary>
    Cash = 0,

    /// <summary>A card, taken through the payment terminal. <see cref="SaleTender.Reference"/> is the authorisation code.</summary>
    Card = 1,

    /// <summary>A gift card, voucher or coupon redeemed at face value.</summary>
    Voucher = 2,

    /// <summary>A mobile money or instant-EFT payment.</summary>
    MobileMoney = 3,

    /// <summary>
    /// Put on the customer's account. Declared so a sale can record it; the credit control, limit check
    /// and AR settlement behind it are Stage 10b's (ADR-055).
    /// </summary>
    CustomerAccount = 4,
}
