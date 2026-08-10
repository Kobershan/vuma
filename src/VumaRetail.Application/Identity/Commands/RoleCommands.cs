using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Identity.Commands;

/// <summary>Creates a role and grants it a set of permissions.</summary>
/// <param name="Name">The role name, unique within the tenant.</param>
/// <param name="Permissions">The <c>module.entity.action</c> strings it grants.</param>
/// <param name="Description">What the role is for.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateRoleCommand(string Name, IReadOnlyCollection<string> Permissions, string? Description = null)
    : ICommand<Guid>;

/// <summary>Rejects a create-role command before it reaches the handler.</summary>
public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateRoleCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Description).MaximumLength(512);
        RuleForEach(command => command.Permissions).NotEmpty().MaximumLength(PermissionKey.MaxLength);
    }
}

/// <summary>Creates a role, refusing any permission no module declares.</summary>
/// <param name="roles">Role lookup and insertion.</param>
/// <param name="catalogue">The closed set of permissions this installation understands.</param>
/// <param name="tenant">The tenant the role belongs to.</param>
/// <param name="unitOfWork">Commits the write.</param>
public sealed class CreateRoleCommandHandler(
    IRoleRepository roles,
    IPermissionCatalogue catalogue,
    ITenantContext tenant,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateRoleCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await roles.FindByNameAsync(command.Name, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw IdentityConflictException.RoleName(command.Name);
        }

        // Validated before anything is written, so a role is never created half-granted. ADR-013: a
        // permission no module declares grants nothing, and failing loudly here is the difference
        // between a typo and a manager who cannot approve refunds for three weeks.
        List<PermissionKey> keys = [.. command.Permissions.Select(catalogue.Require).Distinct()];

        Role role = Role.Create(tenant.TenantId, command.Name, command.Description);
        roles.Add(role);

        foreach (PermissionKey key in keys)
        {
            roles.Add(RolePermission.Grant(tenant.TenantId, role.Id, key));
        }

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return role.Id;
    }
}

/// <summary>Gives a user a role, tenant-wide or in one store.</summary>
/// <param name="UserId">The user.</param>
/// <param name="RoleId">The role.</param>
/// <param name="StoreId">The store the assignment is scoped to, or <c>null</c> for tenant-wide.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AssignRoleCommand(Guid UserId, Guid RoleId, Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects an assign-role command before it reaches the handler.</summary>
public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    /// <summary>Builds the rules.</summary>
    public AssignRoleCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.RoleId).NotEmpty();
    }
}

/// <summary>Assigns a role, refusing a duplicate at the same scope.</summary>
/// <param name="users">User lookup.</param>
/// <param name="roles">Role lookup and insertion.</param>
/// <param name="tenant">The tenant the assignment belongs to.</param>
/// <param name="unitOfWork">Commits the write.</param>
public sealed class AssignRoleCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ITenantContext tenant,
    IUnitOfWork unitOfWork) : ICommandHandler<AssignRoleCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AssignRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await users.FindAsync(command.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException("user", command.UserId);

        _ = await roles.FindAsync(command.RoleId, cancellationToken).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException("role", command.RoleId);

        IReadOnlyList<UserRoleAssignment> existing = await roles
            .ListAssignmentsAsync(command.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing.Any(assignment => assignment.RoleId == command.RoleId && assignment.StoreId == command.StoreId))
        {
            throw IdentityConflictException.RoleAlreadyAssigned();
        }

        UserRoleAssignment created = UserRoleAssignment.Assign(
            tenant.TenantId,
            command.UserId,
            command.RoleId,
            command.StoreId);

        roles.Add(created);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return created.Id;
    }
}
