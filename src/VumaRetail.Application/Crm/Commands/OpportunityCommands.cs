using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Crm.Commands;

/// <summary>Opens an opportunity.</summary>
/// <param name="CompanyId">The owning company.</param>
/// <param name="Title">Deal title.</param>
/// <param name="ExpectedAmount">Expected value amount.</param>
/// <param name="Currency">ISO 4217 code.</param>
/// <param name="Probability">Win probability, 0–100.</param>
/// <param name="LeadId">The lead it came from, if any.</param>
/// <param name="CustomerId">An existing customer, if created directly against one.</param>
/// <param name="Description">Deal description.</param>
/// <param name="CloseDate">Expected close date, if known.</param>
/// <param name="StoreId">The owning store, if any.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateOpportunityCommand(
    Guid CompanyId,
    string Title,
    decimal ExpectedAmount,
    string Currency,
    byte Probability,
    Guid? LeadId = null,
    Guid? CustomerId = null,
    string? Description = null,
    DateOnly? CloseDate = null,
    Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed opportunity.</summary>
public sealed class CreateOpportunityCommandValidator : AbstractValidator<CreateOpportunityCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateOpportunityCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.Title).NotEmpty().MaximumLength(200);
        RuleFor(command => command.ExpectedAmount).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Description).MaximumLength(2000);
    }
}

/// <summary>Opens an opportunity.</summary>
/// <param name="opportunities">Opportunity persistence.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class CreateOpportunityCommandHandler(IOpportunityRepository opportunities, ITenantContext tenant)
    : ICommandHandler<CreateOpportunityCommand, Guid>
{
    /// <inheritdoc />
    public Task<Guid> HandleAsync(CreateOpportunityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var opportunity = new Opportunity(
            tenant.TenantId,
            command.CompanyId,
            command.Title,
            new Money(command.ExpectedAmount, command.Currency),
            command.Probability,
            command.LeadId,
            command.CustomerId,
            command.Description,
            command.CloseDate,
            command.StoreId);

        opportunities.Add(opportunity);
        return Task.FromResult(opportunity.Id);
    }
}

/// <summary>Moves a deal to a new open stage.</summary>
/// <param name="OpportunityId">The opportunity.</param>
/// <param name="Stage">The new stage (never Won/Lost — win or lose explicitly).</param>
/// <param name="Probability">The revised probability, 0–100.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record MoveOpportunityStageCommand(Guid OpportunityId, OpportunityStage Stage, byte Probability)
    : ICommand;

/// <summary>Rejects a malformed stage move.</summary>
public sealed class MoveOpportunityStageCommandValidator : AbstractValidator<MoveOpportunityStageCommand>
{
    /// <summary>Builds the rules.</summary>
    public MoveOpportunityStageCommandValidator() => RuleFor(command => command.OpportunityId).NotEmpty();
}

/// <summary>Applies a stage move.</summary>
/// <param name="opportunities">Opportunity persistence.</param>
public sealed class MoveOpportunityStageCommandHandler(IOpportunityRepository opportunities)
    : ICommandHandler<MoveOpportunityStageCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(MoveOpportunityStageCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Opportunity opportunity = await opportunities
            .FindAsync(command.OpportunityId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OpportunityNotFoundException();

        opportunity.MoveTo(command.Stage, command.Probability);
        return Unit.Value;
    }
}

/// <summary>Wins a deal against a customer.</summary>
/// <param name="OpportunityId">The opportunity.</param>
/// <param name="CustomerId">The customer it converted into.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record WinOpportunityCommand(Guid OpportunityId, Guid CustomerId) : ICommand;

/// <summary>Rejects a malformed win.</summary>
public sealed class WinOpportunityCommandValidator : AbstractValidator<WinOpportunityCommand>
{
    /// <summary>Builds the rules.</summary>
    public WinOpportunityCommandValidator()
    {
        RuleFor(command => command.OpportunityId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
    }
}

/// <summary>Wins a deal.</summary>
/// <param name="opportunities">Opportunity persistence.</param>
public sealed class WinOpportunityCommandHandler(IOpportunityRepository opportunities)
    : ICommandHandler<WinOpportunityCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(WinOpportunityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Opportunity opportunity = await opportunities
            .FindAsync(command.OpportunityId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OpportunityNotFoundException();

        opportunity.Win(command.CustomerId);
        return Unit.Value;
    }
}

/// <summary>Loses a deal with a recorded reason.</summary>
/// <param name="OpportunityId">The opportunity.</param>
/// <param name="LossReason">Why it was lost.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record LoseOpportunityCommand(Guid OpportunityId, string LossReason) : ICommand;

/// <summary>Rejects a malformed loss.</summary>
public sealed class LoseOpportunityCommandValidator : AbstractValidator<LoseOpportunityCommand>
{
    /// <summary>Builds the rules.</summary>
    public LoseOpportunityCommandValidator()
    {
        RuleFor(command => command.OpportunityId).NotEmpty();
        RuleFor(command => command.LossReason).NotEmpty().MaximumLength(1000);
    }
}

/// <summary>Loses a deal.</summary>
/// <param name="opportunities">Opportunity persistence.</param>
public sealed class LoseOpportunityCommandHandler(IOpportunityRepository opportunities)
    : ICommandHandler<LoseOpportunityCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(LoseOpportunityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Opportunity opportunity = await opportunities
            .FindAsync(command.OpportunityId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OpportunityNotFoundException();

        opportunity.Lose(command.LossReason);
        return Unit.Value;
    }
}
