using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Finance.Commands;

/// <summary>Defines a new tax rule (CLAUDE.md §9 — tax is a rules engine, never a constant).</summary>
/// <param name="Code">The code documents reference, for example <c>STANDARD</c>.</param>
/// <param name="Name">A description for the person configuring it.</param>
/// <param name="Rate">The rate, as a fraction — <c>0.15</c> for 15%.</param>
/// <param name="Treatment">Whether stated amounts under this code are inclusive or exclusive.</param>
/// <param name="EffectiveFrom">The first date this rule applies from.</param>
/// <param name="EffectiveTo">The last date this rule applies to, or <c>null</c> if open-ended.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateTaxRuleCommand(
    string Code,
    string Name,
    decimal Rate,
    TaxTreatment Treatment,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null) : ICommand<Guid>;

/// <summary>Validates <see cref="CreateTaxRuleCommand"/>.</summary>
public sealed class CreateTaxRuleCommandValidator : AbstractValidator<CreateTaxRuleCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateTaxRuleCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Rate).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(1m);
        RuleFor(command => command.Treatment).IsInEnum();
        RuleFor(command => command.EffectiveTo)
            .GreaterThanOrEqualTo(command => command.EffectiveFrom)
            .When(command => command.EffectiveTo is not null);
    }
}

/// <summary>Handles <see cref="CreateTaxRuleCommand"/>.</summary>
/// <param name="taxRules">Where tax rules are stored.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class CreateTaxRuleCommandHandler(ITaxRuleRepository taxRules, ITenantContext tenant)
    : ICommandHandler<CreateTaxRuleCommand, Guid>
{
    /// <inheritdoc />
    public Task<Guid> HandleAsync(CreateTaxRuleCommand command, CancellationToken cancellationToken = default)
    {
        TaxRule rule = TaxRule.Define(
            tenant.TenantId,
            command.Code,
            command.Name,
            command.Rate,
            command.Treatment,
            command.EffectiveFrom,
            command.EffectiveTo);

        taxRules.Add(rule);

        return Task.FromResult(rule.Id);
    }
}
