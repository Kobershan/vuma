#pragma warning disable CS1591
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>Group credit exposure with hold tokens.</summary>
public interface IGroupCreditService
{
    /// <summary>Current exposure and available headroom.</summary>
    Task<CreditPosition> GetPositionAsync(Guid tenantId, Guid creditGroupId, CancellationToken cancellationToken = default);
    
    /// <summary>Attempts to take a hold. Returns false if the group limit would be exceeded.</summary>
    Task<HoldResult> TryHoldAsync(Guid tenantId, Guid creditGroupId, Guid companyId, decimal amount, string currency, string documentReference, TimeSpan expiry, CancellationToken cancellationToken = default);
    
    /// <summary>Converts a hold into confirmed consumption.</summary>
    Task ConfirmHoldAsync(Guid holdId, CancellationToken cancellationToken = default);
    
    /// <summary>Releases an unconfirmed hold.</summary>
    Task ReleaseHoldAsync(Guid holdId, CancellationToken cancellationToken = default);
    
    /// <summary>Expired holds are cleaned by a background job. This is the idempotent method it calls.</summary>
    Task<int> ExpireHoldsAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a hold attempt.</summary>
public sealed record CreditPosition(Guid CreditGroupId, decimal Limit, string Currency, decimal Confirmed, decimal Held, decimal Available)
{
    /// <summary>Whether the available credit is negative.</summary>
    public bool IsExceeded => Available < 0;
}

/// <summary>The result of a hold attempt.</summary>
public sealed record HoldResult(Guid HoldId, bool Success, decimal RemainingAvailable)
{
    /// <summary>Creates a failed hold result.</summary>
    public static HoldResult Failed(Guid holdId) => new(holdId, false, 0);
}

/// <summary>Summary of a credit group.</summary>
public sealed record CreditGroupSummary(Guid Id, string Name, string Direction, decimal Limit, string Currency, int MemberCount);

/// <summary>Summary of a credit group member.</summary>
public sealed record CreditGroupMemberSummary(Guid CompanyId, string CompanyCode, decimal? SubLimit);
