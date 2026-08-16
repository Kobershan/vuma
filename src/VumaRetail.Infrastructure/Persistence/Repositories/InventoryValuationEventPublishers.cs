using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// Turns the valuation event a stock movement raises into an <see cref="IFinancialEvent"/> and posts it
/// through Stage 07's posting rules engine.
/// </summary>
/// <remarks>
/// <para>
/// This is the real implementation of the integration point Stage 08 wrote against while Stage 07 was
/// still on a branch — the interface's own remarks describe the shape, and this is it, unchanged.
/// Nothing in Stage 08's command handlers moved to make it work.
/// </para>
/// <para>
/// <c>CLAUDE.md</c> §7 rule 12 is satisfied by construction: this class names an <em>event type</em>
/// and a single named amount, and has nowhere to put a GL account even if it wanted one
/// (<see cref="IFinancialEvent"/> has no such field). Which accounts a receipt or an issue lands on is
/// entirely a <see cref="PostingRule"/>'s decision, configurable per tenant.
/// </para>
/// <para>
/// <b>Signed values become distinct event types.</b> A posting rule fixes the debit/credit side of each
/// of its lines, so it cannot express "the opposite way round when the amount is negative". An
/// adjustment that writes stock off and one that writes it on are genuinely different postings — a loss
/// against an expense account, a gain against a different one — so they are raised as different event
/// types carrying a positive amount, rather than one type whose sign the rule would have to interpret.
/// </para>
/// </remarks>
/// <param name="poster">Stage 07's posting rules engine — the single door onto the GL.</param>
/// <param name="logger">Where a missing posting rule is reported.</param>
public sealed class FinancialInventoryValuationEventPublisher(
    IFinancialEventPoster poster,
    ILogger<FinancialInventoryValuationEventPublisher> logger) : IInventoryValuationEventPublisher
{
    /// <summary>The key the single amount is carried under. A posting rule's lines draw from this.</summary>
    public const string ValueAmountKey = "Value";

    /// <inheritdoc />
    public async Task PublishAsync(
        InventoryValuationEvent valuationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valuationEvent);

        // A transfer's two sides move value between locations without changing what the tenant owns.
        // With one inventory account they would net to zero, and with per-location accounts the tenant
        // configures a rule and these become real postings — see the ADR. Raised either way; whether
        // it posts is the rule's decision, not this class's.
        string eventType = EventTypeFor(valuationEvent.MovementType, valuationEvent.Value);

        // The rule fixes each line's side, so the amount it draws on is always positive. The direction
        // is already carried by the event type.
        Money value = valuationEvent.Value.IsNegative ? -valuationEvent.Value : valuationEvent.Value;

        FinancialEvent financialEvent = new(
            eventType,
            valuationEvent.TenantId,
            valuationEvent.StoreId,
            valuationEvent.OccurredAt,
            valuationEvent.LedgerEntryId.ToString(),
            new Dictionary<string, Money>(StringComparer.Ordinal) { [ValueAmountKey] = value });

        try
        {
            await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (PostingRuleNotFoundException)
        {
            // Deliberately not fatal (ADR-070). The ledger entry records that physical stock moved,
            // which is true whether or not an accountant has configured how it posts. Failing the
            // movement would make a store unable to receive a delivery because its chart of accounts
            // is incomplete — refusing to write down what is demonstrably on the shelf.
            //
            // Logged at warning because the other reading — a rule that should exist and does not —
            // is a real misconfiguration somebody needs to see.
            logger.LogWarning(
                "No posting rule for inventory event {EventType}; stock ledger entry {LedgerEntryId} " +
                "was recorded but raised no journal. Configure a posting rule for this event type if " +
                "these movements should reach the general ledger.",
                eventType,
                valuationEvent.LedgerEntryId);
        }
    }

    /// <summary>
    /// Maps a movement to the event type a <see cref="PostingRule"/> is matched on, splitting the two
    /// movement kinds that can go either way by the sign of their value.
    /// </summary>
    /// <param name="movementType">The movement.</param>
    /// <param name="value">The signed value the movement carried.</param>
    internal static string EventTypeFor(StockMovementType movementType, Money value) => movementType switch
    {
        StockMovementType.Receipt => "inventory.receipt.posted",
        StockMovementType.SaleIssue => "inventory.sale.issued",

        // Stage 10. Its own event type rather than sharing `inventory.receipt.posted`, because a
        // return credits cost of sales while a supplier receipt credits goods-received-not-invoiced —
        // the same movement of stock against two entirely different accounts.
        StockMovementType.SalesReturn => "inventory.sale.returned",
        StockMovementType.TransferOut => "inventory.transfer.out",
        StockMovementType.TransferIn => "inventory.transfer.in",
        StockMovementType.Adjustment => value.IsNegative
            ? "inventory.adjustment.decrease"
            : "inventory.adjustment.increase",
        StockMovementType.StocktakeVariance => value.IsNegative
            ? "inventory.stocktake.shortage"
            : "inventory.stocktake.surplus",
        _ => throw new ArgumentOutOfRangeException(nameof(movementType), movementType, "Unknown stock movement type."),
    };
}

/// <summary>
/// Records that a valuation event was raised and does nothing else.
/// </summary>
/// <remarks>
/// The fallback the <see cref="IInventoryValuationEventPublisher"/> contract describes, kept for hosts
/// that wire inventory without finance — the desktop app's offline cache and the mapping-probe test
/// context both build a container with no <see cref="IFinancialEventPoster"/> in it. A host that has
/// finance registered gets <see cref="FinancialInventoryValuationEventPublisher"/> instead; see
/// <c>AddVumaInventory</c>.
/// </remarks>
/// <param name="logger">Where the event is recorded.</param>
public sealed class LoggingInventoryValuationEventPublisher(
    ILogger<LoggingInventoryValuationEventPublisher> logger) : IInventoryValuationEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(InventoryValuationEvent valuationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valuationEvent);

        logger.LogDebug(
            "Inventory valuation event raised: {MovementType} of {Quantity} worth {Value} at location " +
            "{LocationId}, ledger entry {LedgerEntryId}. No financial poster is registered, so this " +
            "raised no journal.",
            valuationEvent.MovementType,
            valuationEvent.Quantity,
            valuationEvent.Value,
            valuationEvent.LocationId,
            valuationEvent.LedgerEntryId);

        return Task.CompletedTask;
    }
}
