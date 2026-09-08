using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// A stokvel group: a named saving circle with a cycle, a constitution and a status. The group's
/// balance is always projected from its members' rows — there is no "set balance" operation
/// anywhere in this module (ADR-055).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class StokvelGroup : Entity
{
    private StokvelGroup(
        Guid tenantId,
        Guid? storeId,
        string groupNumber,
        string name,
        StokvelType type,
        string constitution,
        DateOnly cycleStart,
        DateOnly cycleEnd,
        Guid storeScopeId,
        StokvelStatus status)
        : base(tenantId, storeId)
    {
        GroupNumber = groupNumber;
        Name = name;
        Type = type;
        Constitution = constitution;
        CycleStart = cycleStart;
        CycleEnd = cycleEnd;
        StoreScopeId = storeScopeId;
        Status = status;
    }

    private StokvelGroup()
    {
    }

    /// <summary>The human-readable number, series <c>STK</c>.</summary>
    public string GroupNumber { get; private set; } = string.Empty;

    /// <summary>What the members call it.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What it saves toward.</summary>
    public StokvelType Type { get; private set; }

    /// <summary>Payout rules and the visibility rule. Member-sees-own-only unless this says otherwise.</summary>
    public string Constitution { get; private set; } = string.Empty;

    /// <summary>The first day of the cycle.</summary>
    public DateOnly CycleStart { get; private set; }

    /// <summary>The last day of the cycle.</summary>
    public DateOnly CycleEnd { get; private set; }

    /// <summary>The store the group belongs to.</summary>
    public Guid StoreScopeId { get; private set; }

    /// <summary>Where the group stands.</summary>
    public StokvelStatus Status { get; private set; }

    /// <summary>Whether the constitution lets a member read another member's rows.</summary>
    public bool MembersSeeAll =>
        Constitution.Contains("members-see-all", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a forming group.</summary>
    public static StokvelGroup Create(
        Guid tenantId,
        Guid? storeId,
        string groupNumber,
        string name,
        StokvelType type,
        string constitution,
        DateOnly cycleStart,
        DateOnly cycleEnd,
        Guid storeScopeId,
        Guid? companyId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A stokvel group must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(groupNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(constitution);

        if (cycleEnd < cycleStart)
        {
            throw new StokvelExceptions("STOKVEL_CYCLE_INVALID", "The cycle end must not be before the cycle start.");
        }

        if (storeScopeId == Guid.Empty)
        {
            throw new ArgumentException("A stokvel group must belong to a store.", nameof(storeScopeId));
        }

        var group = new StokvelGroup(
            tenantId, storeId, groupNumber.Trim(), name.Trim(), type, constitution.Trim(),
            cycleStart, cycleEnd, storeScopeId, StokvelStatus.Forming);
        if (companyId.HasValue && companyId.Value != Guid.Empty)
        {
            group.AssignCompany(companyId.Value);
        }

        return group;
    }

    /// <summary>Opens the group for contributions.</summary>
    public void Activate()
    {
        if (Status != StokvelStatus.Forming)
        {
            throw StokvelExceptions.UnexpectedStatus(Status);
        }

        Status = StokvelStatus.Active;
    }

    /// <summary>Stops new members joining; payouts settle.</summary>
    public void BeginPayout()
    {
        if (Status != StokvelStatus.Active)
        {
            throw StokvelExceptions.UnexpectedStatus(Status);
        }

        Status = StokvelStatus.PayingOut;
    }

    /// <summary>Closes the cycle. Rows stay queryable forever.</summary>
    public void Close()
    {
        if (Status is StokvelStatus.Closed)
        {
            throw StokvelExceptions.UnexpectedStatus(Status);
        }

        Status = StokvelStatus.Closed;
    }

    /// <summary>
    /// Projects the group's balance: contributions less settled payouts plus vested benefits.
    /// Computed, never stored.
    /// </summary>
    public Money Balance(
        IEnumerable<StokvelContribution> contributions,
        IEnumerable<StokvelPayout> payouts,
        IEnumerable<StokvelBenefitAllocation> benefits,
        string currency)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(payouts);
        ArgumentNullException.ThrowIfNull(benefits);

        Money total = Money.Zero(currency);
        foreach (var c in contributions)
        {
            total += c.Amount;
        }

        foreach (var b in benefits)
        {
            total += b.Amount;
        }

        foreach (var p in payouts.Where(p => p.Status == StokvelPayoutStatus.Settled))
        {
            total -= p.Amount;
        }

        return total;
    }
}
