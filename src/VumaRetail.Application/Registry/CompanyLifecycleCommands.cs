#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;

namespace VumaRetail.Application.Registry;

[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivateCompanyCommand(Guid CompanyId, string Reason) : ICommand;

public sealed class DeactivateCompanyCommandValidator : AbstractValidator<DeactivateCompanyCommand>
{
    public DeactivateCompanyCommandValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class DeactivateCompanyCommandHandler(
    ICompanyLifecycleService lifecycle,
    ITenantContext tenant) : ICommandHandler<DeactivateCompanyCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeactivateCompanyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await lifecycle.DeactivateAsync(tenant.TenantId, command.CompanyId, command.Reason, cancellationToken);
        return Unit.Value;
    }
}
