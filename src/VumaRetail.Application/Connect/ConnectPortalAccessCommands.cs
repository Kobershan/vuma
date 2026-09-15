#pragma warning disable CS1591, IDE0011, CA1062
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;

namespace VumaRetail.Application.Connect;

[CommandSideEffect(SideEffect.Write)]
public sealed record GrantSupplierPortalAccessCommand(Guid ConnectionId, Guid ContactId, string AccessRole) : ICommand<Guid>;

[CommandSideEffect(SideEffect.Write)]
public sealed record RevokeSupplierPortalAccessCommand(Guid GrantId) : ICommand;

public sealed class GrantSupplierPortalAccessCommandValidator : AbstractValidator<GrantSupplierPortalAccessCommand>
{
    public GrantSupplierPortalAccessCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty(); RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.AccessRole).NotEmpty().MaximumLength(64);
    }
}

public sealed class GrantSupplierPortalAccessCommandHandler(
    ITradingConnectionRepository connections, ISupplierPortalGrantRepository grants,
    ITenantContext tenant, IClock clock) : ICommandHandler<GrantSupplierPortalAccessCommand, Guid>
{
    public async Task<Guid> HandleAsync(GrantSupplierPortalAccessCommand command, CancellationToken cancellationToken = default)
    {
        TradingConnection connection = await connections.FindForTenantAsync(command.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connection not found.");
        if (connection.SupplierTenantId != tenant.TenantId || connection.Status != TradingConnectionStatus.Active)
            throw new UnauthorizedAccessException("Only an active supplier can grant portal access.");
        SupplierPortalGrant grant = SupplierPortalGrant.Create(connection.SupplierTenantId, connection.RetailerTenantId,
            connection.Id, command.ContactId, command.AccessRole, clock.UtcNow);
        grants.Add(grant);
        return grant.Id;
    }
}

public sealed class RevokeSupplierPortalAccessCommandHandler(
    ISupplierPortalGrantRepository grants, ITenantContext tenant, IClock clock)
    : ICommandHandler<RevokeSupplierPortalAccessCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeSupplierPortalAccessCommand command, CancellationToken cancellationToken = default)
    {
        SupplierPortalGrant grant = await grants.FindForTenantAsync(command.GrantId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Portal grant not found.");
        grant.Revoke(tenant.TenantId, clock.UtcNow);
        return Unit.Value;
    }
}
