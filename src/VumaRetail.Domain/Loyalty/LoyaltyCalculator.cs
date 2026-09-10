using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Loyalty;

/// <summary>
/// Pure points mathematics (Stage 20). Vuma computes only the earn <em>request</em> amount and
/// expiry evaluation for its own bookkeeping — the ledger, accrual and tier evaluation belong to
/// Orbit. No clock, no database: every input is a parameter, so every case is unit-testable.
/// </summary>
public static class LoyaltyCalculator
{
    /// <summary>Scale points are stored at — <c>decimal(18,4)</c>, like money.</summary>
    public const int PointsScale = 4;

    /// <summary>Scale points are displayed at.</summary>
    public const int DisplayScale = 2;

    /// <summary>
    /// Computes the earn request for a purchase: amount × rate × tier multiplier, stored at
    /// scale 4.
    /// </summary>
    /// <param name="purchaseAmount">The purchase amount in the settings currency.</param>
    /// <param name="earnRate">Base points per currency unit.</param>
    /// <param name="tierMultiplier">The member tier's multiplier (1 when none).</param>
    /// <returns>Points to request, scale 4.</returns>
    /// <exception cref="InvalidPointsException">Non-positive purchase amount.</exception>
    public static decimal ComputeEarn(decimal purchaseAmount, decimal earnRate, decimal tierMultiplier = 1m)
    {
        if (purchaseAmount <= 0m)
        {
            throw new InvalidPointsException("Earn requires a positive purchase amount.");
        }

        if (earnRate <= 0m)
        {
            throw new InvalidPointsException("The earn rate must be positive.");
        }

        decimal multiplier = tierMultiplier <= 0m ? 1m : tierMultiplier;
        return decimal.Round(purchaseAmount * earnRate * multiplier, PointsScale, Money.Rounding);
    }

    /// <summary>Renders stored points for display at 2dp, midpoints away from zero.</summary>
    /// <param name="stored">Points at scale 4.</param>
    /// <returns>Display value, e.g. 49.9950 → 50.00.</returns>
    public static decimal ToDisplay(decimal stored)
        => decimal.Round(stored, DisplayScale, Money.Rounding);

    /// <summary>Whether points earned at the given instant have expired.</summary>
    /// <param name="earnedAt">When the points were earned, UTC.</param>
    /// <param name="pointExpiryDays">Days before expiry.</param>
    /// <param name="now">The instant to evaluate at, UTC.</param>
    public static bool IsExpired(DateTimeOffset earnedAt, int pointExpiryDays, DateTimeOffset now)
        => earnedAt.AddDays(pointExpiryDays) <= now;

    /// <summary>
    /// Applies a burn against a known balance. The provisional local check — Orbit's live debit
    /// is authoritative and may still refuse.
    /// </summary>
    /// <param name="balance">The cached balance.</param>
    /// <param name="requested">Points requested. Must be positive.</param>
    /// <returns>The remaining balance.</returns>
    /// <exception cref="InvalidPointsException">Non-positive request.</exception>
    /// <exception cref="InsufficientPointsException">Request exceeds balance.</exception>
    public static decimal ApplyBurn(decimal balance, decimal requested)
    {
        if (requested <= 0m)
        {
            throw new InvalidPointsException("A redemption must request a positive number of points.");
        }

        if (requested > balance)
        {
            throw new InsufficientPointsException();
        }

        return balance - requested;
    }
}
