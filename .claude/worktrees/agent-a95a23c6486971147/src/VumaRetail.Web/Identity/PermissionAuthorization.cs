using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Identity.Queries;
using VumaRetail.Domain.Identity;
using VumaRetail.Infrastructure.Security.Identity;

namespace VumaRetail.Web.Identity;

/// <summary>Requires the caller to hold a permission in the store they are acting in.</summary>
/// <param name="permission">The <c>module.entity.action</c> permission.</param>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    /// <summary>The permission required.</summary>
    public string Permission { get; } = permission;
}

/// <summary>
/// Checks a permission against what the user actually holds, resolved per request.
/// </summary>
/// <remarks>
/// <para>
/// Resolved per request rather than read from the token. A permission list baked into a 15-minute
/// access token is a 15-minute window in which a permission somebody just revoked still works
/// (<c>docs/SECURITY.md</c> §1). The query is one round trip against two indexed tables.
/// </para>
/// <para>
/// Scoped to the store on the token. A supervisor who may approve refunds at the airport branch does
/// not thereby approve them at head office (ADR-013).
/// </para>
/// </remarks>
/// <param name="permissions">Resolves the caller's effective permissions.</param>
/// <param name="catalogue">The closed set of permissions this installation understands.</param>
public sealed class PermissionAuthorizationHandler(
    IQueryHandler<GetEffectivePermissionsQuery, IReadOnlyCollection<string>> permissions,
    IPermissionCatalogue catalogue) : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        // An endpoint asking for a permission no module declares is a bug in the endpoint, and it
        // must fail closed. Silently allowing it would be a permission check that authorises anyone.
        if (!PermissionKey.TryParse(requirement.Permission, out PermissionKey key) || !catalogue.Contains(key))
        {
            return;
        }

        if (ReadGuid(context.User, "sub") is not { } userId)
        {
            return;
        }

        IReadOnlyCollection<string> held = await permissions
            .HandleAsync(new GetEffectivePermissionsQuery(userId, ReadGuid(context.User, VumaClaims.StoreId)))
            .ConfigureAwait(false);

        if (held.Contains(key.Value, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }
    }

    private static Guid? ReadGuid(ClaimsPrincipal user, string claimType)
        => user.FindFirstValue(claimType) is { } value
            && Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid parsed)
                ? parsed
                : null;
}

/// <summary>
/// Turns <c>RequirePermission("identity.user.create")</c> into a policy without anybody registering
/// one policy per permission.
/// </summary>
/// <remarks>
/// There will be several hundred permissions by Stage 31. Registering a named policy for each is a
/// startup list nobody maintains and a place for a module's new permission to be forgotten — so
/// policies for permission names are built on demand and everything else falls through to the
/// default provider.
/// </remarks>
/// <param name="options">The authorization options the default provider reads.</param>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    /// <summary>The prefix that marks a policy name as a permission requirement.</summary>
    public const string PolicyPrefix = "vuma:perm:";

    /// <summary>Builds the policy name an endpoint uses to require a permission.</summary>
    /// <param name="permission">The <c>module.entity.action</c> permission.</param>
    /// <returns>The policy name.</returns>
    public static string PolicyNameFor(string permission) => PolicyPrefix + permission;

    /// <inheritdoc />
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return base.GetPolicyAsync(policyName);
        }

        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[PolicyPrefix.Length..]))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
