using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Crm;

namespace VumaRetail.Application.Crm.Commands;

/// <summary>Logs an interaction. Append-only — a logged activity is never edited.</summary>
/// <param name="CompanyId">The owning company.</param>
/// <param name="Type">What kind of interaction.</param>
/// <param name="Subject">Short subject.</param>
/// <param name="Body">Detail, if any.</param>
/// <param name="Direction">Which way it flowed.</param>
/// <param name="DurationMinutes">Duration, if known.</param>
/// <param name="LeadId">The lead it concerns, if any.</param>
/// <param name="OpportunityId">The opportunity it concerns, if any.</param>
/// <param name="CustomerId">The customer it concerns, if any.</param>
/// <param name="StoreId">The owning store, if any.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record LogActivityCommand(
    Guid CompanyId,
    ActivityType Type,
    string Subject,
    string? Body,
    ActivityDirection Direction = ActivityDirection.Outbound,
    int? DurationMinutes = null,
    Guid? LeadId = null,
    Guid? OpportunityId = null,
    Guid? CustomerId = null,
    Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed activity.</summary>
public sealed class LogActivityCommandValidator : AbstractValidator<LogActivityCommand>
{
    /// <summary>Builds the rules.</summary>
    public LogActivityCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(300);
        RuleFor(command => command.Body).MaximumLength(10000);
        RuleFor(command => command.DurationMinutes).GreaterThan(0).When(command => command.DurationMinutes.HasValue);
    }
}

/// <summary>Logs an interaction.</summary>
/// <param name="activities">Activity persistence.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class LogActivityCommandHandler(
    IActivityRepository activities,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<LogActivityCommand, Guid>
{
    /// <inheritdoc />
    public Task<Guid> HandleAsync(LogActivityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var activity = new Activity(
            tenant.TenantId,
            command.CompanyId,
            command.Type,
            command.Subject,
            command.Body,
            clock.UtcNow,
            command.Direction,
            command.DurationMinutes,
            command.LeadId,
            command.OpportunityId,
            command.CustomerId,
            command.StoreId);

        activities.Add(activity);
        return Task.FromResult(activity.Id);
    }
}
