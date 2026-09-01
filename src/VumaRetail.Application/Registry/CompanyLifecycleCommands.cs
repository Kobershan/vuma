#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Registry;

[CommandSideEffect(SideEffect.Write)]
public sealed record ProvisionCompanyCommand(string Code, string LegalName, string TradingName, string BaseCurrency, string Locale, string DocumentPrefix) : ICommand<Guid>;
public sealed class ProvisionCompanyCommandValidator : AbstractValidator<ProvisionCompanyCommand>
{
    public ProvisionCompanyCommandValidator() { RuleFor(x => x.Code).NotEmpty().MaximumLength(32); RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200); RuleFor(x => x.TradingName).NotEmpty().MaximumLength(200); RuleFor(x => x.BaseCurrency).NotEmpty().Length(3); RuleFor(x => x.Locale).NotEmpty().MaximumLength(35); RuleFor(x => x.DocumentPrefix).NotEmpty().MaximumLength(32); }
}
public sealed class ProvisionCompanyCommandHandler(ICompanyProvisioner provisioner, ITenantContext tenant) : ICommandHandler<ProvisionCompanyCommand, Guid>
{
    public async Task<Guid> HandleAsync(ProvisionCompanyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Company company = Company.Create(tenant.TenantId, command.Code, command.LegalName, command.TradingName, command.BaseCurrency, command.Locale, command.DocumentPrefix);
        await provisioner.ProvisionAsync(company, cancellationToken).ConfigureAwait(false);
        return company.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ActivateCompanyCommand(Guid CompanyId) : ICommand;
public sealed class ActivateCompanyCommandValidator : AbstractValidator<ActivateCompanyCommand>
{
    public ActivateCompanyCommandValidator() => RuleFor(x => x.CompanyId).NotEmpty();
}
public sealed class ActivateCompanyCommandHandler(ICompanyLifecycleService lifecycle, ITenantContext tenant) : ICommandHandler<ActivateCompanyCommand, Unit>
{
    public async Task<Unit> HandleAsync(ActivateCompanyCommand command, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(command); await lifecycle.ActivateAsync(tenant.TenantId, command.CompanyId, cancellationToken).ConfigureAwait(false); return Unit.Value; }
}

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
