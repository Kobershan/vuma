using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Catalog;

namespace VumaRetail.Application.Catalog.Commands;

/// <summary>
/// Defines a unit of measure — a base unit, or one that converts to an existing base unit.
/// </summary>
/// <remarks>
/// One command for both shapes (the stage brief's own wording): setting
/// <see cref="BaseUnitOfMeasureId"/> asks for a derived unit and <see cref="Type"/> is then ignored,
/// because <see cref="UnitOfMeasure.CreateDerived"/> takes its kind from the base unit it converts to.
/// </remarks>
/// <param name="Code">The short code, upper-cased at creation.</param>
/// <param name="Name">The unit's name.</param>
/// <param name="Type">What kind of measure this is. Ignored when <see cref="BaseUnitOfMeasureId"/> is set.</param>
/// <param name="BaseUnitOfMeasureId">The base unit this converts to, or <c>null</c> to define a base unit.</param>
/// <param name="ConversionFactorToBase">How many base units one of this is worth. Required when converting.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateUnitOfMeasureCommand(
    string Code,
    string Name,
    UnitOfMeasureType Type,
    Guid? BaseUnitOfMeasureId = null,
    decimal? ConversionFactorToBase = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create-unit-of-measure command before it reaches the handler.</summary>
public sealed class CreateUnitOfMeasureCommandValidator : AbstractValidator<CreateUnitOfMeasureCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateUnitOfMeasureCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(16);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);

        RuleFor(command => command.ConversionFactorToBase)
            .NotNull()
            .GreaterThan(0m)
            .When(command => command.BaseUnitOfMeasureId is not null);
    }
}

/// <summary>Creates a unit of measure, refusing a code already used in this tenant.</summary>
/// <param name="units">Unit-of-measure lookup and insertion.</param>
/// <param name="tenant">The tenant the unit belongs to.</param>
public sealed class CreateUnitOfMeasureCommandHandler(IUnitOfMeasureRepository units, ITenantContext tenant)
    : ICommandHandler<CreateUnitOfMeasureCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateUnitOfMeasureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await units.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw CatalogConflictException.UnitOfMeasureCode(command.Code);
        }

        UnitOfMeasure unit;

        if (command.BaseUnitOfMeasureId is { } baseUnitId)
        {
            UnitOfMeasure baseUnit = await units.FindAsync(baseUnitId, cancellationToken).ConfigureAwait(false)
                ?? throw new CatalogNotFoundException("unit of measure", baseUnitId);

            unit = UnitOfMeasure.CreateDerived(
                tenant.TenantId,
                command.Code,
                command.Name,
                baseUnit,
                command.ConversionFactorToBase ?? 0m);
        }
        else
        {
            unit = UnitOfMeasure.CreateBase(tenant.TenantId, command.Code, command.Name, command.Type);
        }

        units.Add(unit);

        return unit.Id;
    }
}
