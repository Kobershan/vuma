#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Registry;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateRegistryUserCommand(string Login, string DisplayName, string ContactDetails, Guid OperatorId) : ICommand<Guid>;
public sealed class CreateRegistryUserCommandValidator : AbstractValidator<CreateRegistryUserCommand>
{
    public CreateRegistryUserCommandValidator()
    {
        RuleFor(x => x.Login).NotEmpty().MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.OperatorId).NotEmpty();
    }
}

public sealed class CreateRegistryUserCommandHandler(
    IRegistryUserService registryUserService,
    ITenantContext tenant) : ICommandHandler<CreateRegistryUserCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateRegistryUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegistryUser user = await registryUserService.CreateAsync(tenant.TenantId, command.Login, command.DisplayName, command.OperatorId, command.ContactDetails, cancellationToken);
        return user.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record GrantCompanyAccessCommand(Guid RegistryUserId, Guid CompanyId, string Roles) : ICommand<Guid>;
public sealed class GrantCompanyAccessCommandValidator : AbstractValidator<GrantCompanyAccessCommand>
{
    public GrantCompanyAccessCommandValidator()
    {
        RuleFor(x => x.RegistryUserId).NotEmpty();
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Roles).NotEmpty();
    }
}

public sealed class GrantCompanyAccessCommandHandler(
    IRegistryUserService registryUserService,
    IOperatorContext operatorContext) : ICommandHandler<GrantCompanyAccessCommand, Guid>
{
    public async Task<Guid> HandleAsync(GrantCompanyAccessCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        operatorContext.RequireOperatorId();
        RegistryUserCompanyAccess access = await registryUserService.GrantAccessAsync(command.RegistryUserId, command.CompanyId, command.Roles, operatorContext.Principal, cancellationToken);
        return access.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RevokeCompanyAccessCommand(Guid RegistryUserId, Guid CompanyId) : ICommand;
public sealed class RevokeCompanyAccessCommandValidator : AbstractValidator<RevokeCompanyAccessCommand>
{
    public RevokeCompanyAccessCommandValidator()
    {
        RuleFor(x => x.RegistryUserId).NotEmpty();
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}

public sealed class RevokeCompanyAccessCommandHandler(
    IRegistryUserService registryUserService) : ICommandHandler<RevokeCompanyAccessCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeCompanyAccessCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await registryUserService.RevokeAccessAsync(command.RegistryUserId, command.CompanyId, cancellationToken);
        return Unit.Value;
    }
}
