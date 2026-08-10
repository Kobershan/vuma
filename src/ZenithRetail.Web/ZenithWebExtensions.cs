using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ZenithRetail.Application.Abstractions;
using ZenithRetail.Infrastructure.DependencyInjection;
using ZenithRetail.Infrastructure.Security.Identity;
using ZenithRetail.Web.Identity;

namespace ZenithRetail.Web;

/// <summary>Wires Stage 02's identity into an ASP.NET Core host.</summary>
public static class ZenithWebExtensions
{
    /// <summary>
    /// Registers authentication, authorisation, the authenticated principal accessor and tenant
    /// resolution.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="jwt">Signing key, issuer, audience and lifetimes.</param>
    /// <param name="host">The tenant this host serves when a request carries none.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Call before <c>AddZenithPersistence</c>. The <see cref="IPrincipalAccessor"/> registered here
    /// wins because that method's registration is try-add — which is what turns "give the audit trail
    /// a real user" into a registration rather than an edit to Stage 01's code.
    /// </remarks>
    public static IServiceCollection AddZenithWeb(this IServiceCollection services, JwtOptions jwt, HostTenantOptions host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jwt);
        ArgumentNullException.ThrowIfNull(host);

        services.AddSingleton(host);
        services.AddHttpContextAccessor();
        services.AddScoped<IPrincipalAccessor, HttpContextPrincipalAccessor>();

        services.AddZenithIdentity(jwt);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    // Zero, not the five-minute default. A 15-minute token silently living for 20
                    // undoes most of the reason for choosing 15 (docs/SECURITY.md §1).
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Name,
                };
            })
            .AddScheme<TerminalCertificateOptions, TerminalCertificateAuthenticationHandler>(
                TerminalCertificateOptions.Scheme,
                _ => { });

        services.AddAuthorization();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionAuthorizationHandler>());
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        return services;
    }

    /// <summary>
    /// Adds authentication, authorisation and tenant resolution to the pipeline, in that order.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Order matters and is not a style choice: tenant resolution reads the authenticated principal,
    /// so it has to run after authentication. Put it before and every request resolves no tenant and
    /// every query returns nothing — which looks like a data problem, not a wiring one.
    /// </remarks>
    public static IApplicationBuilder UseZenithWeb(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        return app;
    }

    /// <summary>Requires the caller to hold a permission (ADR-013).</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint.</param>
    /// <param name="permission">The <c>module.entity.action</c> permission, from a module's declaration.</param>
    /// <returns>The endpoint, for chaining.</returns>
    /// <remarks>
    /// Always this, never a role name. Roles are customer-editable data; an endpoint that names one
    /// breaks the moment a tenant renames it, and there is an architecture test that says so.
    /// </remarks>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireAuthorization(PermissionPolicyProvider.PolicyNameFor(permission));

        return builder;
    }
}
