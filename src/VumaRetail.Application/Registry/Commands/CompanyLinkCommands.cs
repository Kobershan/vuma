#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Registry;

[CommandSideEffect(SideEffect.Write)]
public sealed record ProposeCompanyLinkCommand(Guid CompanyAId, Guid CompanyBId, CompanyLinkScope Scopes) : ICommand<Guid>;

public sealed class ProposeCompanyLinkCommandValidator : AbstractValidator<ProposeCompanyLinkCommand>
{
    public ProposeCompanyLinkCommandValidator()
    {
        RuleFor(x => x.CompanyAId).NotEmpty();
        RuleFor(x => x.CompanyBId).NotEmpty();
        RuleFor(x => x.Scopes).NotEqual(CompanyLinkScope.None);
        RuleFor(x => x).Must(c => c.CompanyAId != c.CompanyBId).WithMessage("A company cannot link to itself.");
    }
}

public sealed class ProposeCompanyLinkCommandHandler(
    ICompanyLinkService companyLinkService,
    IOperatorContext operatorContext) : ICommandHandler<ProposeCompanyLinkCommand, Guid>
{
    public async Task<Guid> HandleAsync(ProposeCompanyLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        operatorContext.RequireOperatorId();
        CompanyLink link = await companyLinkService.ProposeAsync(command.CompanyAId, command.CompanyBId, command.Scopes, cancellationToken);
        return link.Id;
    }
}
