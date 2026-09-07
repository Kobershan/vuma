namespace VumaRetail.Application.Inventory;

/// <summary>How the group availability projection decides a contributor has gone quiet (ADR-119).</summary>
public sealed class GroupAvailabilityOptions
{
    /// <summary>Section name for configuration binding.</summary>
    public const string SectionName = "Vuma:Availability";

    /// <summary>After this long without a publish, a contributor is shown as stale. Default 15 minutes.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How many outbox rows the relay applies per company per pass. Default 200.</summary>
    public int RelayBatchSize { get; set; } = 200;
}

/// <summary>How the reservation expiry job runs.</summary>
public sealed class ReservationExpiryOptions
{
    /// <summary>Section name for configuration binding.</summary>
    public const string SectionName = "Vuma:Reservations";

    /// <summary>How often the job sweeps for due holds. Default 5 minutes.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>The tenant an inventory background pass runs as.</summary>
/// <remarks>
/// Same shape as Stage 04b's <c>LicensingHostTenant</c> and Stage 07's <c>FinanceHostTenant</c>: a
/// hosted service has no request and therefore no ambient tenant, so the host says which tenant
/// its background work belongs to.
/// </remarks>
/// <param name="TenantId">The tenant this host serves.</param>
/// <param name="StoreId">The store this host runs, where it runs one.</param>
public sealed record InventoryHostTenant(Guid TenantId, Guid? StoreId);
