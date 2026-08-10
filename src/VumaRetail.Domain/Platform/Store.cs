using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Platform;

/// <summary>
/// A trading location — a shop, a warehouse, a dark store. The <c>store_id</c> on every row points here.
/// </summary>
/// <remarks>
/// <para>
/// R8 requires multi-store from the data model up even though v1 ships single-store, which is why
/// this exists in Stage 01 with nothing yet referencing it. Retrofitting a store dimension into a
/// year of trading data is a migration nobody wants to write.
/// </para>
/// <para>
/// Currency and timezone override the tenant's where a store needs them to — a branch across a
/// border trades in its own currency and closes its day on its own clock. Both are nullable and fall
/// back to the tenant, so the common case stays a single place to edit.
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.Bidirectional"/> with <see cref="ConflictPolicy.CloudWins"/>:
/// head office creates and configures stores, but a store manager legitimately edits their own
/// trading hours and address. When both edit at once the cloud's version stands, because head office
/// is where a multi-store estate is reconciled.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Store : Entity
{
    private Store(Guid tenantId, string code, string name)
        : base(tenantId)
    {
        Code = code;
        Name = name;
        // A store is its own store: rows written by this store carry its id, and so does the store row.
        StoreId = Id;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Store()
    {
    }

    /// <summary>The short code staff use and documents are numbered by, for example <c>JHB01</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The store's name as it appears on a receipt.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The trading address, unstructured for now — Stage 06 owns structured addresses.</summary>
    public string? Address { get; private set; }

    /// <summary>ISO 4217 code this store trades in, or <c>null</c> to use the tenant's base currency.</summary>
    public string? CurrencyOverride { get; private set; }

    /// <summary>IANA timezone for this store, or <c>null</c> to use the tenant's.</summary>
    public string? TimeZoneOverride { get; private set; }

    /// <summary>True while the store is trading. A closed store keeps its history and stops accepting sales.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a store within a tenant.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The short store code, upper-cased.</param>
    /// <param name="name">The store name as printed on receipts.</param>
    public static Store Create(Guid tenantId, string code, string name)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A store must belong to a tenant.", nameof(tenantId));
        }

        return new Store(tenantId, Require(code, nameof(code)).ToUpperInvariant(), Require(name, nameof(name)));
    }

    /// <summary>Renames the store and updates its address.</summary>
    /// <param name="name">The new store name.</param>
    /// <param name="address">The new trading address, or <c>null</c> to clear it.</param>
    public void SetDetails(string name, string? address)
    {
        Name = Require(name, nameof(name));
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
    }

    /// <summary>Overrides the tenant's currency and timezone for this store, or clears the overrides.</summary>
    /// <param name="currency">ISO 4217 code, or <c>null</c> to inherit the tenant's.</param>
    /// <param name="timeZone">IANA timezone, or <c>null</c> to inherit the tenant's.</param>
    public void SetLocalisationOverrides(string? currency, string? timeZone)
    {
        CurrencyOverride = currency is null ? null : new Money(0m, currency).Currency;
        TimeZoneOverride = string.IsNullOrWhiteSpace(timeZone) ? null : timeZone.Trim();
    }

    /// <summary>Opens the store for trading.</summary>
    public void Open() => IsActive = true;

    /// <summary>Closes the store. History is retained; nothing is deleted (§7 rule 8).</summary>
    public void Close() => IsActive = false;

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}
