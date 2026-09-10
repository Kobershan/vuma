using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Loyalty.Permissions;

/// <summary>What the loyalty module lets somebody do (Stage 20).</summary>
/// <remarks>
/// Reads serve members and tills from cache; earn/redeem move value through Orbit. Admin-only
/// grants (configuration, cache sync, reconciliation) stay split from the till's earn/redeem so
/// a cashier can never reconfigure the programme.
/// </remarks>
public sealed class LoyaltyPermissions : IModulePermissions
{
    /// <summary>Read member profiles and tiers.</summary>
    public const string MemberView = "loyalty.member.view";

    /// <summary>Enroll members.</summary>
    public const string MemberEnroll = "loyalty.member.enroll";

    /// <summary>Earn points through Orbit.</summary>
    public const string Earn = "loyalty.earn.issue";

    /// <summary>Redeem points through Orbit. Moves value.</summary>
    public const string Redeem = "loyalty.redeem.issue";

    /// <summary>Read balances and transaction history.</summary>
    public const string BalanceView = "loyalty.balance.view";

    /// <summary>Read tiers and rewards.</summary>
    public const string CatalogueView = "loyalty.catalogue.view";

    /// <summary>Configure the programme and sync caches. Admin only.</summary>
    public const string Admin = "loyalty.admin.manage";

    /// <inheritdoc />
    public string Module => "loyalty";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(MemberView), "View loyalty members and tiers."),
        new(PermissionKey.Parse(MemberEnroll), "Enroll loyalty members."),
        new(PermissionKey.Parse(Earn), "Earn loyalty points."),
        new(PermissionKey.Parse(Redeem), "Redeem loyalty points.", IsHighRisk: true),
        new(PermissionKey.Parse(BalanceView), "View balances and transaction history."),
        new(PermissionKey.Parse(CatalogueView), "View tiers and rewards."),
        new(PermissionKey.Parse(Admin), "Configure loyalty and sync caches.", IsHighRisk: true),
    ];
}
