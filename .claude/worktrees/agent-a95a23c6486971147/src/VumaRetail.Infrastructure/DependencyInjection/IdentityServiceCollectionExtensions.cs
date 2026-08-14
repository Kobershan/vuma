using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Security.Identity;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 02's identity, RBAC and permission catalogue with the host's container.</summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the permission catalogue, the identity repositories, hashing, token issuance and the
    /// identity command and query handlers.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="jwt">How access and refresh tokens are signed and how long they live.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Call this <b>before</b> <c>AddVumaPersistence</c>. That method registers its system
    /// <see cref="IPrincipalAccessor"/> with try-add semantics precisely so an authenticated one
    /// registered first wins, which makes replacing it a registration rather than an edit to Stage
    /// 01's code. The web host registers the HTTP-backed accessor; this method leaves it alone.
    /// </para>
    /// <para>
    /// Stage 03 replaced this method's eight hand-written handler registrations with the Scrutor scan
    /// in <see cref="MessagingServiceCollectionExtensions.AddVumaMessaging"/> (ADR-009). What is left
    /// here is what a scan cannot infer: which adapter implements which port.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaIdentity(this IServiceCollection services, JwtOptions jwt)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jwt);

        services.AddSingleton(jwt);
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenHasher, Sha256TokenHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton(CredentialPolicy.Default);

        services.AddVumaPermissionCatalogue();
        services.AddVumaMessaging();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITerminalRepository, TerminalRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        services.AddScoped<AuthenticationService>();

        return services;
    }

    /// <summary>
    /// Registers the permission catalogue and the two modules that declare permissions so far.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Every module stage from 06 onward adds one line here, or registers its own
    /// <see cref="IModulePermissions"/> from its own composition root. The catalogue is a singleton
    /// assembled once: it never changes after startup, and a mutable shared dictionary read from every
    /// request thread is a race waiting for a busy Saturday.
    /// </remarks>
    public static IServiceCollection AddVumaPermissionCatalogue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, PlatformPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, IdentityPermissions>());

        services.TryAddSingleton<IPermissionCatalogue>(provider =>
            new PermissionCatalogue(provider.GetServices<IModulePermissions>()));

        return services;
    }
}
