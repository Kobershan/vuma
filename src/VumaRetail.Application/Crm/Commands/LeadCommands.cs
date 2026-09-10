using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Crm;

namespace VumaRetail.Application.Crm.Commands;

/// <summary>Captures a lead.</summary>
/// <param name="CompanyId">The company capturing the lead.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Phone number, if known.</param>
/// <param name="Company">Prospect's company, if known.</param>
/// <param name="Source">Where the lead came from.</param>
/// <param name="StoreId">The capturing store, if any.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateLeadCommand(
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Company,
    LeadSource Source,
    Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed capture.</summary>
public sealed class CreateLeadCommandValidator : AbstractValidator<CreateLeadCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateLeadCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(command => command.LastName).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(command => command.Phone).MaximumLength(32);
        RuleFor(command => command.Company).MaximumLength(200);
    }
}

/// <summary>Captures a lead, refusing a duplicate email in the same store.</summary>
/// <param name="leads">Lead persistence.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class CreateLeadCommandHandler(ILeadRepository leads, ITenantContext tenant)
    : ICommandHandler<CreateLeadCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateLeadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string email = command.Email.Trim().ToLowerInvariant();
        Lead? duplicate = await leads
            .FindByEmailAsync(email, command.StoreId, cancellationToken)
            .ConfigureAwait(false);

        if (duplicate is not null)
        {
            throw new DuplicateLeadEmailException(command.Email.Trim());
        }

        var lead = new Lead(
            tenant.TenantId,
            command.CompanyId,
            command.FirstName,
            command.LastName,
            email,
            command.Phone,
            command.Company,
            command.Source,
            command.StoreId);

        leads.Add(lead);
        return lead.Id;
    }
}

/// <summary>Updates a lead's captured details.</summary>
/// <param name="LeadId">The lead.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="Phone">Phone number.</param>
/// <param name="Company">Prospect's company.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record UpdateLeadCommand(
    Guid LeadId,
    string FirstName,
    string LastName,
    string? Phone,
    string? Company) : ICommand;

/// <summary>Rejects a malformed update.</summary>
public sealed class UpdateLeadCommandValidator : AbstractValidator<UpdateLeadCommand>
{
    /// <summary>Builds the rules.</summary>
    public UpdateLeadCommandValidator()
    {
        RuleFor(command => command.LeadId).NotEmpty();
        RuleFor(command => command.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(command => command.LastName).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Phone).MaximumLength(32);
        RuleFor(command => command.Company).MaximumLength(200);
    }
}

/// <summary>Applies a lead update.</summary>
/// <param name="leads">Lead persistence.</param>
public sealed class UpdateLeadCommandHandler(ILeadRepository leads)
    : ICommandHandler<UpdateLeadCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(UpdateLeadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Lead lead = await leads
            .FindAsync(command.LeadId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LeadNotFoundException();

        lead.UpdateDetails(command.FirstName, command.LastName, command.Phone, command.Company);
        return Unit.Value;
    }
}

/// <summary>Assigns a lead to a user.</summary>
/// <param name="LeadId">The lead.</param>
/// <param name="UserId">The user taking it.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AssignLeadCommand(Guid LeadId, Guid UserId) : ICommand;

/// <summary>Rejects a malformed assignment.</summary>
public sealed class AssignLeadCommandValidator : AbstractValidator<AssignLeadCommand>
{
    /// <summary>Builds the rules.</summary>
    public AssignLeadCommandValidator()
    {
        RuleFor(command => command.LeadId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
    }
}

/// <summary>Applies a lead assignment.</summary>
/// <param name="leads">Lead persistence.</param>
public sealed class AssignLeadCommandHandler(ILeadRepository leads)
    : ICommandHandler<AssignLeadCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(AssignLeadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Lead lead = await leads
            .FindAsync(command.LeadId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LeadNotFoundException();

        lead.AssignTo(command.UserId);
        return Unit.Value;
    }
}

/// <summary>Qualifies a lead out. Terminal.</summary>
/// <param name="LeadId">The lead.</param>
/// <param name="Dead">True for gone-cold, false for qualified-out.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DisqualifyLeadCommand(Guid LeadId, bool Dead = false) : ICommand;

/// <summary>Rejects a malformed disqualification.</summary>
public sealed class DisqualifyLeadCommandValidator : AbstractValidator<DisqualifyLeadCommand>
{
    /// <summary>Builds the rules.</summary>
    public DisqualifyLeadCommandValidator() => RuleFor(command => command.LeadId).NotEmpty();
}

/// <summary>Applies a disqualification.</summary>
/// <param name="leads">Lead persistence.</param>
public sealed class DisqualifyLeadCommandHandler(ILeadRepository leads)
    : ICommandHandler<DisqualifyLeadCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(DisqualifyLeadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Lead lead = await leads
            .FindAsync(command.LeadId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LeadNotFoundException();

        lead.Disqualify(command.Dead);
        return Unit.Value;
    }
}

/// <summary>What converting a lead produced.</summary>
/// <param name="LeadId">The converted lead.</param>
/// <param name="CustomerId">The partner it became.</param>
/// <param name="ConvertedAt">When, UTC.</param>
public sealed record LeadConversionOutcome(Guid LeadId, Guid CustomerId, DateTimeOffset ConvertedAt);

/// <summary>
/// Converts a lead into an existing partner (Stage 06 identity). Atomic and one-way: links the
/// lead, stamps conversion and logs a system activity in one transaction.
/// </summary>
/// <param name="LeadId">The lead.</param>
/// <param name="CustomerId">The partner it became.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ConvertLeadCommand(Guid LeadId, Guid CustomerId) : ICommand<LeadConversionOutcome>;

/// <summary>Rejects a malformed conversion.</summary>
public sealed class ConvertLeadCommandValidator : AbstractValidator<ConvertLeadCommand>
{
    /// <summary>Builds the rules.</summary>
    public ConvertLeadCommandValidator()
    {
        RuleFor(command => command.LeadId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
    }
}

/// <summary>Converts a lead against a verified partner.</summary>
/// <param name="leads">Lead persistence.</param>
/// <param name="activities">Activity persistence, for the conversion record.</param>
/// <param name="partners">Partner lookup, for the link check.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ConvertLeadCommandHandler(
    ILeadRepository leads,
    IActivityRepository activities,
    IPartnerRepository partners,
    IClock clock) : ICommandHandler<ConvertLeadCommand, LeadConversionOutcome>
{
    /// <inheritdoc />
    public async Task<LeadConversionOutcome> HandleAsync(ConvertLeadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Lead lead = await leads
            .FindAsync(command.LeadId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LeadNotFoundException();

        // Link-only in v1: the partner must already exist in this company's database. The lead
        // is untouched when it does not — a conversion that half-happened would orphan the
        // pipeline state from the identity it points at.
        _ = await partners
            .FindAsync(command.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LeadCustomerNotFoundException();

        DateTimeOffset now = clock.UtcNow;
        ConvertLeadResult result = lead.Convert(command.CustomerId, now);

        activities.Add(new Activity(
            lead.TenantId,
            lead.CompanyId!.Value,
            ActivityType.System,
            "Lead converted",
            $"Lead converted to customer {command.CustomerId}.",
            now,
            ActivityDirection.Outbound,
            null,
            lead.Id,
            null,
            command.CustomerId,
            lead.StoreId));

        return new LeadConversionOutcome(
            lead.Id, result.CustomerId, result.ConvertedAt ?? now);
    }
}
