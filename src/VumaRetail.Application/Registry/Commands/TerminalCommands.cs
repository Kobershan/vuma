#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Registry;

[CommandSideEffect(SideEffect.Write)]
public sealed record RegisterTerminalCommand(Guid PremisesId, string TerminalId, string DeviceCertThumbprint) : ICommand<Guid>;
public sealed class RegisterTerminalCommandValidator : AbstractValidator<RegisterTerminalCommand>
{
    public RegisterTerminalCommandValidator()
    {
        RuleFor(x => x.PremisesId).NotEmpty();
        RuleFor(x => x.TerminalId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.DeviceCertThumbprint).NotEmpty().Length(64);
    }
}

public sealed class RegisterTerminalCommandHandler(
    ITerminalService terminalService,
    ITenantContext tenant) : ICommandHandler<RegisterTerminalCommand, Guid>
{
    public async Task<Guid> HandleAsync(RegisterTerminalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegistryTerminal terminal = await terminalService.RegisterAsync(tenant.TenantId, command.PremisesId, command.TerminalId, command.DeviceCertThumbprint, cancellationToken);
        return terminal.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record SetTerminalCompaniesCommand(Guid TerminalId, List<Guid> CompanyIds) : ICommand;
public sealed class SetTerminalCompaniesCommandValidator : AbstractValidator<SetTerminalCompaniesCommand>
{
    public SetTerminalCompaniesCommandValidator()
    {
        RuleFor(x => x.TerminalId).NotEmpty();
        RuleFor(x => x.CompanyIds).NotEmpty();
    }
}

public sealed class SetTerminalCompaniesCommandHandler(
    ITerminalService terminalService) : ICommandHandler<SetTerminalCompaniesCommand, Unit>
{
    public async Task<Unit> HandleAsync(SetTerminalCompaniesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await terminalService.SetCompaniesAsync(command.TerminalId, command.CompanyIds, cancellationToken);
        return Unit.Value;
    }
}
