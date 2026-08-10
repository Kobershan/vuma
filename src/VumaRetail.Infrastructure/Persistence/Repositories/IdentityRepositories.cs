using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementations of the Stage 02 identity ports.
/// </summary>
/// <remarks>
/// <para>
/// None of these commit. The unit of work is the pipeline's (§7 rule 2), so a handler that changes a
/// user and writes a second row commits both or neither.
/// </para>
/// <para>
/// None of them filter by tenant or by soft delete either — both are global query filters applied to
/// every entity by <see cref="VumaRetailDbContext"/>. Adding a second predicate here would hide a
/// bug in the first one rather than defending against it.
/// </para>
/// </remarks>
/// <param name="context">The database context.</param>
public sealed class UserRepository(VumaRetailDbContext context) : IUserRepository
{
    /// <inheritdoc />
    public Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken = default)
        => context.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        string normalized = User.Normalize(userName);

        return context.Users.FirstOrDefaultAsync(
            user => user.NormalizedUserName == normalized,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListPinOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
        => await context.Users
            .Where(user => user.IsActive && user.PinHash != null)
            .Where(user => context.UserRoleAssignments
                .Any(assignment => assignment.UserId == user.Id
                    && (assignment.StoreId == null || assignment.StoreId == storeId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListPinHoldersAsync(CancellationToken cancellationToken = default)
        => await context.Users
            .Where(user => user.IsActive && user.PinHash != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(User user) => context.Users.Add(user);
}

/// <summary>Roles, their grants and their assignments.</summary>
/// <param name="context">The database context.</param>
public sealed class RoleRepository(VumaRetailDbContext context) : IRoleRepository
{
    /// <inheritdoc />
    public Task<Role?> FindAsync(Guid roleId, CancellationToken cancellationToken = default)
        => context.Roles.FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    /// <inheritdoc />
    public Task<Role?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string normalized = User.Normalize(name);

        return context.Roles.FirstOrDefaultAsync(role => role.NormalizedName == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RolePermission>> ListPermissionsAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
        => await context.RolePermissions
            .Where(grant => grant.RoleId == roleId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRoleAssignment>> ListAssignmentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await context.UserRoleAssignments
            .Where(assignment => assignment.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> ListEffectivePermissionsAsync(
        Guid userId,
        Guid? storeId,
        CancellationToken cancellationToken = default)
    {
        // One round trip. Asking for the assignments and then the grants would be two queries and a
        // join done in memory, on a path that runs on every authorised request.
        IQueryable<Guid> roleIds = context.UserRoleAssignments
            .Where(assignment => assignment.UserId == userId)
            .Where(assignment => assignment.StoreId == null || assignment.StoreId == storeId)
            .Select(assignment => assignment.RoleId);

        return await context.RolePermissions
            .Where(grant => roleIds.Contains(grant.RoleId))
            .Select(grant => grant.Permission)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(Role role) => context.Roles.Add(role);

    /// <inheritdoc />
    public void Add(RolePermission permission) => context.RolePermissions.Add(permission);

    /// <inheritdoc />
    public void Add(UserRoleAssignment assignment) => context.UserRoleAssignments.Add(assignment);
}

/// <summary>Terminals.</summary>
/// <param name="context">The database context.</param>
/// <param name="tenantContext">Opens the explicit cross-tenant scope thumbprint lookup needs.</param>
public sealed class TerminalRepository(VumaRetailDbContext context, ITenantContext tenantContext) : ITerminalRepository
{
    /// <inheritdoc />
    public Task<Terminal?> FindAsync(Guid terminalId, CancellationToken cancellationToken = default)
        => context.Terminals.FirstOrDefaultAsync(terminal => terminal.Id == terminalId, cancellationToken);

    /// <inheritdoc />
    public async Task<Terminal?> FindByThumbprintAsync(
        string thumbprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);

        // A terminal presenting a certificate is how its tenant is discovered, so there is no ambient
        // tenant to filter by yet. The scope names itself and is logged (ITenantContext), and the
        // thumbprint is unique across the installation, so exactly one row can match.
        using IDisposable scope = tenantContext.BypassTenantFilter(
            "terminal certificate authentication resolves the tenant from the thumbprint");

        return await context.Terminals
            .FirstOrDefaultAsync(terminal => terminal.CertificateThumbprint == thumbprint, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Terminal>> ListPendingAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
        => await context.Terminals
            .Where(terminal => terminal.StoreId == storeId && terminal.Status == TerminalStatus.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Terminal?> FindByCodeAsync(Guid storeId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.Terminals.FirstOrDefaultAsync(
            terminal => terminal.StoreId == storeId && terminal.Code == normalized,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Terminal terminal) => context.Terminals.Add(terminal);
}

/// <summary>Refresh tokens.</summary>
/// <param name="context">The database context.</param>
/// <param name="tenantContext">Opens the cross-tenant scope digest lookup needs.</param>
public sealed class RefreshTokenRepository(VumaRetailDbContext context, ITenantContext tenantContext)
    : IRefreshTokenRepository
{
    /// <inheritdoc />
    public async Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        // Same reasoning as the terminal thumbprint: a bearer token arriving at /auth/refresh carries
        // no tenant, and the digest is what identifies both the session and the tenant it belongs to.
        using IDisposable scope = tenantContext.BypassTenantFilter(
            "refresh token exchange resolves the tenant from the token digest");

        return await context.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RefreshToken>> ListLiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(RefreshToken token) => context.RefreshTokens.Add(token);
}
