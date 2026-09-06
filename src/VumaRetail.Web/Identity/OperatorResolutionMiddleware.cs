using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Security.Identity;

namespace VumaRetail.Web.Identity;

/// <summary>
/// Sets <see cref="IOperatorContext"/> at the request edge from the licence-derived claim.
/// </summary>
/// <remarks>
/// Runs after authentication and tenant resolution: the operator claim is only trusted from a
/// signed token, and the operator row is looked up tenant-filtered. A request without the claim
/// — a pre-registry login, a terminal certificate session, a background job — leaves the context
/// unset, and whatever then needs an operator fails with an honest error rather than someone
/// else's identity. A claim naming an unknown operator likewise stays unset: the refusal names
/// the missing operator context, never a guess.
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="logger">Records a request whose operator claim resolves to nothing.</param>
public sealed class OperatorResolutionMiddleware(RequestDelegate next, ILogger<OperatorResolutionMiddleware> logger)
{
    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request.</param>
    /// <param name="operatorContext">The ambient operator to set.</param>
    /// <param name="tenantContext">The already-resolved tenant.</param>
    /// <param name="registry">The registry database.</param>
    public async Task InvokeAsync(
        HttpContext context,
        IOperatorContext operatorContext,
        ITenantContext tenantContext,
        VumaRegistryDbContext registry)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operatorContext);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(registry);

        if (ReadClaim(context.User, VumaClaims.OperatorId) is { } operatorId
            && tenantContext.TenantId != Guid.Empty)
        {
            Domain.Registry.Operator? row = await registry.Operators
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperatorId == operatorId && o.TenantId == tenantContext.TenantId)
                .ConfigureAwait(false);

            if (row is not null)
            {
                operatorContext.SetOperator(row.OperatorId, row.DisplayName, row.LicenceFingerprint, row.IsActive);
            }
            else
            {
                logger.LogWarning(
                    "Operator claim {OperatorId} resolved to no operator row for tenant {TenantId} on {Path}.",
                    operatorId,
                    tenantContext.TenantId,
                    context.Request.Path);
            }
        }

        await next(context).ConfigureAwait(false);
    }

    private static Guid? ReadClaim(ClaimsPrincipal? user, string claimType)
        => user?.Identity is { IsAuthenticated: true }
            && user.FindFirstValue(claimType) is { } value
            && Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid parsed)
                ? parsed
                : null;
}
