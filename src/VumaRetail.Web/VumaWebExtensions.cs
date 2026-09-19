using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VumaRetail.Application.Abstractions;
using VumaRetail.Infrastructure.DependencyInjection;
using VumaRetail.Infrastructure.Security.Identity;
using VumaRetail.Web.Api;
using VumaRetail.Web.Diagnostics;
using VumaRetail.Web.Identity;

namespace VumaRetail.Web;

/// <summary>Wires Stage 02's identity into an ASP.NET Core host.</summary>
public static class VumaWebExtensions
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
    /// Call before <c>AddVumaPersistence</c>. The <see cref="IPrincipalAccessor"/> registered here
    /// wins because that method's registration is try-add — which is what turns "give the audit trail
    /// a real user" into a registration rather than an edit to Stage 01's code.
    /// </remarks>
    public static IServiceCollection AddVumaWeb(this IServiceCollection services, JwtOptions jwt, HostTenantOptions host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jwt);
        ArgumentNullException.ThrowIfNull(host);

        services.AddSingleton(host);
        services.AddHttpContextAccessor();
        services.AddDistributedMemoryCache();
        services.AddScoped<IPrincipalAccessor, HttpContextPrincipalAccessor>();

        services.AddVumaIdentity(jwt);
        services.AddCors(options =>
        {
            string[] allowedOrigins = jwt.CorsAllowedOrigins ?? [];
            options.AddDefaultPolicy(policy =>
            {
                if (allowedOrigins.Length == 0)
                {
                    policy.SetIsOriginAllowed(_ => false);
                }
                else
                {
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
                }
            });
        });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Leave the claim types alone. The default rewrites `sub` to the WS-Federation
                // ClaimTypes.NameIdentifier URI, so every `FindFirstValue("sub")` in the codebase
                // silently returns null — which reads as "this token has no subject" and answers 401
                // on a perfectly good token. Found by the Stage 03 API tests; see ADR-045.
                options.MapInboundClaims = false;

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
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { code = "RATE_LIMITED", detail = "Too many authentication attempts. Try again later." },
                    cancellationToken).ConfigureAwait(false);
            };

            options.AddPolicy("vuma-auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

            options.AddPolicy("vuma-terminal-activation", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionAuthorizationHandler>());
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        // Stage 03: the error contract and the OpenAPI document, registered once for every host
        // rather than per host, so the store server and the cloud API cannot drift apart.
        services.AddVumaProblemDetails();
        services.AddVumaOpenApi();

        return services;
    }

    /// <summary>
    /// Adds authentication, authorisation and tenant resolution to the pipeline, in that order.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Order matters and is not a style choice: tenant resolution reads the authenticated principal,
    /// so it has to run after authentication. Put it before and every request resolves no tenant and
    /// every query returns nothing — which looks like a data problem, not a wiring one.
    /// </para>
    /// <para>
    /// The exception handler and the correlation id go first, outside everything. A request that
    /// fails to authenticate is exactly the sort support gets called about, so it needs an id and a
    /// <c>ProblemDetails</c> body too — and a failure inside authentication itself must still come
    /// back as a problem document rather than as an empty 500.
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseVumaWeb(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'";
            if (context.Request.IsHttps)
            {
                context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            await next(context).ConfigureAwait(false);
        });

        // Gives 401, 403 and 404 a body. They are produced by middleware that short-circuits before
        // the exception handler, so without this they are the only errors in the system with no code.
        app.UseStatusCodePages();

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseVumaRequestLogging();
        app.UseRateLimiter();
        app.UseCors();
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method)
                && context.User.Identity?.IsAuthenticated == true
                && Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out Guid userId)
                && context.User.FindFirst(VumaClaims.SecurityStamp)?.Value is { Length: > 0 } stamp
                && await context.RequestServices.GetRequiredService<ITokenRevocationCache>()
                    .IsStampRevokedAsync(userId, stamp, context.RequestAborted).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseMiddleware<OperatorResolutionMiddleware>();
        app.UseAuthorization();

        return app;
    }

    private static string ClientKey(HttpContext context)
    {
        string? ip = context.Connection.RemoteIpAddress?.ToString();
        if (ip is null)
        {
            ip = context.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
        }

        return string.IsNullOrWhiteSpace(ip) ? "unknown-client" : ip;
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
