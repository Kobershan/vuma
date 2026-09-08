using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>
/// A predefined basket at a group price: what a hamper payout turns into. The group price is
/// frozen on the basket — substitution at settle time is priced at it, never re-resolved
/// (snapshot, not re-derivation — ADR-112's shape for baskets).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class HamperBasket : Entity
{
    private readonly List<HamperBasketLine> _lines = [];

    private HamperBasket(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        string name,
        Money groupPrice,
        DateOnly validFrom,
        DateOnly validTo,
        Guid locationId)
        : base(tenantId, storeId)
    {
        GroupId = groupId;
        Name = name;
        GroupPrice = groupPrice;
        ValidFrom = validFrom;
        ValidTo = validTo;
        LocationId = locationId;
    }

    private HamperBasket()
    {
    }

    /// <summary>The group offering it.</summary>
    public Guid GroupId { get; private set; }

    /// <summary>What the members call it, e.g. <c>December family hamper</c>.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The frozen group price members pay.</summary>
    public Money GroupPrice { get; private set; }

    /// <summary>The first day it may be taken.</summary>
    public DateOnly ValidFrom { get; private set; }

    /// <summary>The last day it may be taken.</summary>
    public DateOnly ValidTo { get; private set; }

    /// <summary>The stock location its reservations hold at. One company, one location.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The frozen lines.</summary>
    public IReadOnlyList<HamperBasketLine> Lines => _lines;

    /// <summary>Creates a basket.</summary>
    public static HamperBasket Create(
        Guid tenantId,
        Guid? storeId,
        Guid groupId,
        string name,
        Money groupPrice,
        DateOnly validFrom,
        DateOnly validTo,
        Guid locationId,
        Guid? companyId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A hamper basket must belong to a tenant.", nameof(tenantId));
        }

        if (groupId == Guid.Empty)
        {
            throw new ArgumentException("A hamper basket must belong to a group.", nameof(groupId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (groupPrice.Amount <= 0m)
        {
            throw new StokvelExceptions("STOKVEL_HAMPER_PRICE_MUST_BE_POSITIVE", "The group price must be positive.");
        }

        if (validTo < validFrom)
        {
            throw new StokvelExceptions("STOKVEL_HAMPER_SEASON_INVALID", "The season end must not be before its start.");
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A hamper basket must name the location its stock holds at.", nameof(locationId));
        }

        var basket = new HamperBasket(
            tenantId, storeId, groupId, name.Trim(), groupPrice, validFrom, validTo, locationId);
        if (companyId.HasValue && companyId.Value != Guid.Empty)
        {
            basket.AssignCompany(companyId.Value);
        }

        return basket;
    }

    /// <summary>Adds a frozen line.</summary>
    public void AddLine(HamperBasketLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
    }

    /// <summary>True when the basket may be taken on a date.</summary>
    public bool IsInSeason(DateOnly date) => date >= ValidFrom && date <= ValidTo;
}
