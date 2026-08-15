using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>
/// A physical count session at one location: <see cref="StocktakeLine"/>s are recorded while it is
/// open, and finalizing it posts every line's variance as an immutable <see cref="StockLedgerEntry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Not marked <see cref="IImmutableRecord"/> — unlike a ledger entry, a session genuinely has one legal
/// state transition (<see cref="Finalize"/>), so the framework-enforced "never touch again" guarantee
/// is the wrong fit. Immutability after finalizing is a domain rule instead: <see cref="EnsureOpen"/>
/// is called by every mutating method, including this session's own line-recording path, and refuses
/// once <see cref="Status"/> is <see cref="StocktakeStatus.Finalized"/>. This is the same shape
/// <c>CLAUDE.md</c> §7 rule 7 asks for — a finalized count is corrected by a new adjustment, never an
/// edit to the count itself.
/// </para>
/// <para>
/// Store-local operational data: the count happens on one store's floor, and the cloud tier observes
/// the result rather than participating in it — the same relationship <c>Terminal</c> has with the
/// store that enrolled it.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class StocktakeSession : Entity
{
    private StocktakeSession(Guid tenantId, Guid? storeId, Guid locationId)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        Status = StocktakeStatus.Open;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private StocktakeSession()
    {
    }

    /// <summary>The location being counted.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>Where this session stands.</summary>
    public StocktakeStatus Status { get; private set; }

    /// <summary>When the session was finalized, or <c>null</c> while it is still open.</summary>
    public DateTimeOffset? FinalizedAt { get; private set; }

    /// <summary>True once <see cref="Finalize"/> has been called. No further counts may be recorded.</summary>
    public bool IsFinalized => Status == StocktakeStatus.Finalized;

    /// <summary>Opens a new count session at a location.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store — the location's own.</param>
    /// <param name="locationId">The location being counted.</param>
    public static StocktakeSession Open(Guid tenantId, Guid? storeId, Guid locationId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A stocktake session must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A stocktake session must name a location.", nameof(locationId));
        }

        return new StocktakeSession(tenantId, storeId, locationId);
    }

    /// <summary>
    /// Refuses to proceed once the session is finalized. Called before every mutation, including a
    /// count being recorded against this session's lines.
    /// </summary>
    /// <exception cref="InventoryRuleException">The session is already finalized.</exception>
    public void EnsureOpen()
    {
        if (IsFinalized)
        {
            throw InventoryRuleException.StocktakeAlreadyFinalized();
        }
    }

    /// <summary>
    /// Closes the session. Called by the command handler after every line's variance has been posted
    /// to the ledger — this method only records that it happened, it does not post anything itself,
    /// because posting needs the ledger poster this aggregate has no access to.
    /// </summary>
    /// <param name="at">The instant of finalizing, UTC.</param>
    public void Finalize(DateTimeOffset at)
    {
        EnsureOpen();

        Status = StocktakeStatus.Finalized;
        FinalizedAt = at;
    }
}

/// <summary>Where a <see cref="StocktakeSession"/> stands.</summary>
public enum StocktakeStatus
{
    /// <summary>Counts may still be recorded.</summary>
    Open = 0,

    /// <summary>Every line's variance has been posted to the ledger. No further counts may be recorded.</summary>
    Finalized = 1,
}
