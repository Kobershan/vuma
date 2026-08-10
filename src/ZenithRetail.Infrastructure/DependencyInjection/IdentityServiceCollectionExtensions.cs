using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZenithRetail.Application.Abstractions;
using ZenithRetail.Application.Identity;
using ZenithRetail.Application.Identity.Commands;
using ZenithRetail.Application.Identity.Permissions;
using ZenithRetail.Application.Identity.Queries;
using ZenithRetail.Domain.Identity;
using ZenithRetail.Infrastructure.Persistence.Repositories;
using ZenithRetail.Infrastructure.Security.Identity;

namespace ZenithRetail.Infrastructure.DependencyInjection;

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
    /// Call this <b>before</b> <c>AddZenithPersistence</c>. That method registers its system
    /// <see cref="IPrincipalAccessor"/> with try-add semantics precisely so an authenticated one
    /// registered first wins, which makes replacing it a registration rather than an edit to Stage
    /// 01's code. The web host registers the HTTP-backed accessor; this method leaves it alone.
    /// </para>
    /// <para>
    /// Handlers are registered one by one rather than by assembly scanning. Stage 03 introduces the
    /// dispatcher and the Scrutor scan that ADR-009 describes; doing it here would be building half of
    /// that stage's deliverable in a way it then has to unpick.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddZenithIdentity(this IServiceCollection services, JwtOptions jwt)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jwt);

        services.AddSingleton(jwt);
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenHasher, Sha256TokenHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton(CredentialPolicy.Default);

        services.AddZenithPermissionCatalogue();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITerminalRepository, TerminalRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        services.AddScoped<AuthenticationService>();

        services.AddScoped<ICommandHandler<CreateUserCommand, Guid>, CreateUserCommandHandler>();
        services.AddScoped<ICommandHandler<SetUserPinCommand, Unit>, SetUserPinCommandHandler>();
        services.AddScoped<ICommandHandler<ChangePasswordCommand, Unit>, ChangePasswordCommandHandler>();
        services.AddScoped<ICommandHandler<CreateRoleCommand, Guid>, CreateRoleCommandHandler>();
        services.AddScoped<ICommandHandler<AssignRoleCommand, Guid>, AssignRoleCommandHandler>();
        services.AddScoped<ICommandHandler<EnrolTerminalCommand, TerminalEnrolment>, EnrolTerminalCommandHandler>();
        services.AddScoped<ICommandHandler<ActivateTerminalCommand, Guid>, ActivateTerminalCommandHandler>();
        services.AddScoped<ICommandHandler<RevokeTerminalCommand, Unit>, RevokeTerminalCommandHandler>();

        services
            .AddScoped<IQueryHandler<GetEffectivePermissionsQuery, IReadOnlyCollection<string>>,
                GetEffectivePermissionsQueryHandler>();

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
    public static IServiceCollection AddZenithPermissionCatalogue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, PlatformPermissions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, IdentityPermissions>());

        services.TryAddSingleton<IPermissionCatalogue>(provider =>
            new PermissionCatalogue(provider.GetServices<IModulePermissions>()));

        return services;
    }
}
