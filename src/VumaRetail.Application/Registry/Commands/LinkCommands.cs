#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Registry;

[CommandSideEffect(SideEffect.Write)]
public sealed record AcceptCompanyLinkCommand(Guid LinkId) : ICommand;
public sealed class AcceptCompanyLinkCommandValidator : AbstractValidator<AcceptCompanyLinkCommand>
{
    public AcceptCompanyLinkCommandValidator() => RuleFor(x => x.LinkId).NotEmpty();
}

public sealed class AcceptCompanyLinkCommandHandler(
    ICompanyLinkService companyLinkService) : ICommandHandler<AcceptCompanyLinkCommand, Unit>
{
    public async Task<Unit> HandleAsync(AcceptCompanyLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await companyLinkService.AcceptAsync(command.LinkId, Guid.Empty, cancellationToken);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record SuspendCompanyLinkCommand(Guid LinkId, string Reason) : ICommand;
public sealed class SuspendCompanyLinkCommandValidator : AbstractValidator<SuspendCompanyLinkCommand>
{
    public SuspendCompanyLinkCommandValidator()
    {
        RuleFor(x => x.LinkId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10);
    }
}

public sealed class SuspendCompanyLinkCommandHandler(
    ICompanyLinkService companyLinkService) : ICommandHandler<SuspendCompanyLinkCommand, Unit>
{
    public async Task<Unit> HandleAsync(SuspendCompanyLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await companyLinkService.SuspendAsync(command.LinkId, command.Reason, cancellationToken);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RevokeCompanyLinkCommand(Guid LinkId, string Reason) : ICommand;
public sealed class RevokeCompanyLinkCommandValidator : AbstractValidator<RevokeCompanyLinkCommand>
{
    public RevokeCompanyLinkCommandValidator()
    {
        RuleFor(x => x.LinkId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10);
    }
}

public sealed class RevokeCompanyLinkCommandHandler(
    ICompanyLinkService companyLinkService) : ICommandHandler<RevokeCompanyLinkCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeCompanyLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await companyLinkService.RevokeAsync(command.LinkId, command.Reason, cancellationToken);
        return Unit.Value;
    }
}
