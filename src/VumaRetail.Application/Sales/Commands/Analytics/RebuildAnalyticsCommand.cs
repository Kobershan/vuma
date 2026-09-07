using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;

namespace VumaRetail.Application.Sales.Commands.Analytics;

/// <summary>
/// Rebuilds the company-scoped sales read models for a period from posted invoices. Planning
/// figures only — a rebuild never blocks trade and never feeds a commit (ADR-119).
/// </summary>
/// <param name="CompanyId">The company to rebuild, or <c>null</c> for every company in scope.</param>
/// <param name="From">The first instant, inclusive, UTC.</param>
/// <param name="To">The last instant, exclusive, UTC.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RebuildAnalyticsCommand(
    Guid? CompanyId,
    DateTimeOffset From,
    DateTimeOffset To) : ICommand;

/// <summary>Rejects a malformed rebuild command before it reaches the handler.</summary>
public sealed class RebuildAnalyticsCommandValidator : AbstractValidator<RebuildAnalyticsCommand>
{
    /// <summary>Builds the rules.</summary>
    public RebuildAnalyticsCommandValidator()
    {
        RuleFor(command => command.To).GreaterThan(command => command.From);
    }
}

/// <summary>Rebuilds through the analytics repository.</summary>
/// <param name="analytics">The read-model store.</param>
public sealed class RebuildAnalyticsCommandHandler(
    ISalesAnalyticsRepository analytics) : ICommandHandler<RebuildAnalyticsCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(RebuildAnalyticsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await analytics.RebuildAsync(command.CompanyId, command.From, command.To, cancellationToken)
            .ConfigureAwait(false);

        return Unit.Value;
    }
}
