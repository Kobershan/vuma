#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Registry;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreatePremisesCommand(string Code, string Name, string Address, string GeoLocation, string TradingHours) : ICommand<Guid>;
public sealed class CreatePremisesCommandValidator : AbstractValidator<CreatePremisesCommand>
{
    public CreatePremisesCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.GeoLocation).NotEmpty();
    }
}

public sealed class CreatePremisesCommandHandler(
    IPremisesService premisesService,
    ITenantContext tenant) : ICommandHandler<CreatePremisesCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreatePremisesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Premises premises = await premisesService.CreateAsync(tenant.TenantId, command.Code, command.Name, command.Address, command.GeoLocation, command.TradingHours, cancellationToken);
        return premises.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record AddPremisesOccupancyCommand(Guid PremisesId, Guid CompanyId, Guid StoreId) : ICommand<Guid>;
public sealed class AddPremisesOccupancyCommandValidator : AbstractValidator<AddPremisesOccupancyCommand>
{
    public AddPremisesOccupancyCommandValidator()
    {
        RuleFor(x => x.PremisesId).NotEmpty();
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.StoreId).NotEmpty();
    }
}

public sealed class AddPremisesOccupancyCommandHandler(
    IPremisesService premisesService) : ICommandHandler<AddPremisesOccupancyCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddPremisesOccupancyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        PremisesOccupancy occupancy = await premisesService.AddOccupancyAsync(command.PremisesId, command.CompanyId, command.StoreId, cancellationToken);
        return occupancy.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record PublishPremisesBinLayoutCommand(Guid PremisesId) : ICommand;
public sealed class PublishPremisesBinLayoutCommandValidator : AbstractValidator<PublishPremisesBinLayoutCommand>
{
    public PublishPremisesBinLayoutCommandValidator() => RuleFor(x => x.PremisesId).NotEmpty();
}

public sealed class PublishPremisesBinLayoutCommandHandler(
    IPremisesService premisesService) : ICommandHandler<PublishPremisesBinLayoutCommand, Unit>
{
    public async Task<Unit> HandleAsync(PublishPremisesBinLayoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await premisesService.PublishBinLayoutAsync(command.PremisesId, cancellationToken);
        return Unit.Value;
    }
}
