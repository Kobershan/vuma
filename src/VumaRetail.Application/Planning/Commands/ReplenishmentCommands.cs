using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Planning;
using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Domain.Planning;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Planning.Commands;

/// <summary>Runs replenishment: expires the lapsed, proposes the needed, reattempts backorders.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record RunReplenishmentCommand : ICommand<RunReplenishmentOutcome>;

/// <summary>What a replenishment run did.</summary>
/// <param name="SuggestionsRaised">New suggestions raised.</param>
/// <param name="Expired">Open suggestions lapsed.</param>
/// <param name="Skipped">SKUs skipped (no safety calculation yet).</param>
/// <param name="BackordersReallocated">Backordered order lines the reattempt freed.</param>
public sealed record RunReplenishmentOutcome(
    int SuggestionsRaised, int Expired, int Skipped, int BackordersReallocated);

/// <summary>
/// The scheduled replenishment pass. Per parameterised SKU it reads the latest safety
/// calculation, the latest forecast, authoritative local availability and the group projection,
/// then records idempotent suggestions (one run key per day — re-runs upsert, never duplicate).
/// Transfer candidates survive only with an active, correctly scoped link checked at generation.
/// Suggestions never commit anything; only an explicit accept creates a downstream document.
/// </summary>
public sealed class RunReplenishmentCommandHandler(
    IReplenishmentParameterRepository parameters,
    ISafetyStockCalculationRepository safety,
    IDemandForecastRepository forecasts,
    IReplenishmentSuggestionRepository suggestions,
    IOpenToBuyBudgetRepository budgets,
    IAvailabilityService availability,
    ICompanyLinkService links,
    IOtbCommitmentReader commitments,
    IReplenishmentEngine engine,
    IPurchaseOrderRepository orders,
    ITenantContext tenant,
    IClock clock,
    ICommandHandler<ReattemptBackorderedAllocationsCommand, ReattemptBackorderedAllocationsResult> backorders)
    : ICommandHandler<RunReplenishmentCommand, RunReplenishmentOutcome>
{
    /// <summary>Suggestions lapse unanswered after this long.</summary>
    public static readonly TimeSpan SuggestionLifetime = TimeSpan.FromDays(14);

    /// <inheritdoc />
    public async Task<RunReplenishmentOutcome> HandleAsync(
        RunReplenishmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = clock.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        int expired = await ExpireOverdueAsync(now, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<(Guid Company, Guid? Item, Guid? Variant), decimal> inbound =
            await ReadInboundAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ReplenishmentParameter> all = await parameters
            .ListAllAsync(cancellationToken)
            .ConfigureAwait(false);

        int raised = 0;
        int skipped = 0;

        foreach (ReplenishmentParameter parameter in all)
        {
            if (parameter.CompanyId is not { } companyId)
            {
                skipped++;
                continue;
            }

            SafetyStockCalculation? calc = await safety
                .LatestAsync(companyId, parameter.LocationId, parameter.ItemId, parameter.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            if (calc is null)
            {
                skipped++;
                continue;
            }

            DemandForecast? forecast = await forecasts
                .LatestBeforeAsync(companyId, parameter.LocationId, parameter.ItemId, parameter.ItemVariantId, today, cancellationToken)
                .ConfigureAwait(false);

            LocalAvailability local = await availability
                .GetLocalAsync(parameter.LocationId, parameter.ItemId, parameter.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<TransferCandidate> candidates = await TransferCandidatesAsync(
                companyId, parameter.ItemId, parameter.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            bool overOtb = await IsOverOpenToBuyAsync(
                companyId, today, now, cancellationToken).ConfigureAwait(false);

            inbound.TryGetValue((companyId, parameter.ItemId, parameter.ItemVariantId), out decimal incoming);

            // Forecast covers one week per snapshot; scale to the cover horizon.
            decimal horizonWeeks = Math.Max(parameter.LeadTimeDays + parameter.ReviewPeriodDays, 7) / 7m;
            decimal horizonForecast = (forecast?.Quantity ?? 0m) * horizonWeeks;

            IReadOnlyList<ProposedSuggestion> proposed = engine.Propose(new ReplenishmentInput(
                parameter.ItemId,
                parameter.ItemVariantId,
                parameter.LocationId,
                parameter.Uom,
                calc.ReorderPoint,
                calc.SafetyStock,
                local.Promise.Available.Value,
                incoming,
                horizonForecast,
                parameter.LeadTimeDays,
                candidates,
                overOtb));

            foreach (ProposedSuggestion proposal in proposed)
            {
                string key = $"{today:yyyy-MM-dd}/{companyId:D}/{parameter.LocationId:D}/{SkuKey(parameter)}/{(int)proposal.Reason}";

                if (await suggestions.FindOpenByKeyAsync(key, cancellationToken).ConfigureAwait(false) is not null)
                {
                    continue;
                }

                suggestions.Add(ReplenishmentSuggestion.Raise(
                    tenant.TenantId,
                    companyId,
                    parameter.LocationId,
                    parameter.ItemId,
                    parameter.ItemVariantId,
                    proposal.Quantity,
                    parameter.Uom,
                    proposal.Reason,
                    proposal.Source,
                    proposal.Candidate?.SourceCompanyId,
                    proposal.Candidate?.SourceLocationId,
                    key,
                    overOtb,
                    now,
                    now + SuggestionLifetime));

                raised++;
            }
        }

        ReattemptBackorderedAllocationsResult reattempt = await backorders
            .HandleAsync(new ReattemptBackorderedAllocationsCommand(), cancellationToken)
            .ConfigureAwait(false);

        return new RunReplenishmentOutcome(raised, expired, skipped, reattempt.LinesReallocated);
    }

    private async Task<int> ExpireOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReplenishmentSuggestion> overdue = await suggestions
            .ListOpenAsync(now, 500, cancellationToken)
            .ConfigureAwait(false);

        foreach (ReplenishmentSuggestion suggestion in overdue)
        {
            suggestion.Expire();
        }

        return overdue.Count;
    }

    private async Task<IReadOnlyDictionary<(Guid Company, Guid? Item, Guid? Variant), decimal>> ReadInboundAsync(
        CancellationToken cancellationToken)
    {
        var inbound = new Dictionary<(Guid Company, Guid? Item, Guid? Variant), decimal>();

        foreach (Domain.Procurement.PurchaseOrderStatus status in
                 new[]
                 {
                     Domain.Procurement.PurchaseOrderStatus.Approved,
                     Domain.Procurement.PurchaseOrderStatus.Issued,
                     Domain.Procurement.PurchaseOrderStatus.PartiallyReceived,
                 })
        {
            KeysetCursor? after = null;

            while (true)
            {
                (IReadOnlyList<Domain.Procurement.PurchaseOrder> page, bool hasMore) = await orders
                    .ListPageAsync(null, status, after, 200, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var order in page)
                {
                    if (order.CompanyId is not { } companyId)
                    {
                        continue;
                    }

                    foreach (var line in order.Lines)
                    {
                        decimal outstanding = line.Quantity.Value - line.ReceivedQuantity.Value;

                        if (outstanding <= 0m)
                        {
                            continue;
                        }

                        var key = (companyId, line.ItemId, line.ItemVariantId);
                        inbound.TryGetValue(key, out decimal current);
                        inbound[key] = current + outstanding;
                    }
                }

                if (!hasMore || page.Count == 0)
                {
                    break;
                }

                var last = page[^1];
                after = new KeysetCursor(
                    last.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture), last.Id);
            }
        }

        return inbound;
    }

    private async Task<IReadOnlyList<TransferCandidate>> TransferCandidatesAsync(
        Guid companyId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken)
    {
        GroupAvailabilityView view = await availability
            .GetGroupAsync(itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        var candidates = new List<TransferCandidate>();

        foreach (GroupAvailabilityContribution contribution in view.Contributions)
        {
            if (contribution.IsStale
                || contribution.CompanyId == companyId
                || contribution.Promise.Available.Value <= 0m)
            {
                continue;
            }

            // The link check happens at generation: no valid link means no candidate, and the
            // engine never sees the surplus at all.
            try
            {
                await links
                    .RequireLink(companyId, contribution.CompanyId, CompanyLinkScope.SharedSourcing, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CompanyLinkRequiredException)
            {
                continue;
            }

            candidates.Add(new TransferCandidate(
                contribution.CompanyId,
                contribution.LocationId,
                contribution.Promise.Available.Value,
                contribution.AsAt));
        }

        return candidates;
    }

    private async Task<bool> IsOverOpenToBuyAsync(
        Guid companyId, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // No budget, no verdict — open-to-buy is a flag, and there is nothing to flag against.
        OpenToBuyBudget? budget = await budgets
            .FindAsync(companyId, today.Year, today.Month, null, cancellationToken)
            .ConfigureAwait(false);

        if (budget is null)
        {
            return false;
        }

        OtbCommitments committed = await commitments
            .ReadCommittedAsync(budget.Currency, now, cancellationToken)
            .ConfigureAwait(false);

        return committed.Committed >= budget.PlannedAmount;
    }

    private static string SkuKey(ReplenishmentParameter parameter)
        => parameter.ItemId?.ToString("D") ?? parameter.ItemVariantId!.Value.ToString("D");
}

/// <summary>Accepts an open suggestion, creating exactly one downstream document.</summary>
/// <param name="SuggestionId">The suggestion.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AcceptReplenishmentSuggestionCommand(Guid SuggestionId) : ICommand<Guid>;

/// <summary>Rejects a malformed accept.</summary>
public sealed class AcceptReplenishmentSuggestionCommandValidator
    : AbstractValidator<AcceptReplenishmentSuggestionCommand>
{
    /// <summary>Builds the rules.</summary>
    public AcceptReplenishmentSuggestionCommandValidator()
        => RuleFor(command => command.SuggestionId).NotEmpty();
}

/// <summary>
/// Raises the downstream documents a suggestion accept needs, through Stage 12's and Stage 08's
/// commands. Shared by accept and amend-accept so the two paths cannot drift apart.
/// </summary>
public sealed class SuggestionAcceptor(
    IReplenishmentParameterRepository parameters,
    IPlanningProcurementWriter procurement,
    IPlanningTransferWriter transfers,
    IPlanningPriceReader prices,
    ITenantContext tenant,
    IClock clock)
{
    /// <summary>Raises a supplier-free requisition for a procurement suggestion.</summary>
    public async Task<Guid> RaiseRequisitionAsync(
        ReplenishmentSuggestion suggestion, decimal quantity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        int leadDays = 7;

        if (suggestion.CompanyId is { } companyId
            && await parameters
                .FindAsync(companyId, suggestion.LocationId, suggestion.ItemId, suggestion.ItemVariantId, cancellationToken)
                .ConfigureAwait(false) is { } parameter)
        {
            leadDays = parameter.LeadTimeDays;
        }

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        PriceSnapshot? price = await prices
            .TryReadAsync(
                suggestion.LocationId, suggestion.ItemId, suggestion.ItemVariantId,
                tenant.StoreId, today, cancellationToken)
            .ConfigureAwait(false);

        return await procurement.RaiseRequisitionAsync(
            suggestion.LocationId,
            today.AddDays(Math.Max(leadDays, 1)),
            $"Replenishment: {suggestion.Reason} at {suggestion.LocationId:D} (planning)",
            [new PlannedRequisitionLine(
                suggestion.ItemId,
                suggestion.ItemVariantId,
                $"Replenishment {suggestion.Reason}",
                quantity,
                suggestion.Uom,
                price is { AverageCost: { } cost } ? new Money(cost, price.Currency) : null)],
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves stock for a transfer suggestion.</summary>
    public Task<Guid> TransferAsync(
        ReplenishmentSuggestion suggestion, decimal quantity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        if (suggestion.SourceLocationId is not { } source)
        {
            throw PlanningRuleException.BadInput("A transfer suggestion without a source cannot be accepted.");
        }

        return transfers.TransferAsync(
            source,
            suggestion.LocationId,
            suggestion.ItemId,
            suggestion.ItemVariantId,
            quantity,
            suggestion.Uom,
            $"Replenishment {suggestion.Reason} (planning)",
            cancellationToken);
    }
}

/// <summary>
/// Accepts as raised. Procurement suggestions raise a supplier-free requisition through Stage 12's
/// commands; transfer suggestions move stock through Stage 08's transfer command. A second accept
/// returns the recorded document — it never creates another one.
/// </summary>
public sealed class AcceptReplenishmentSuggestionCommandHandler(
    IReplenishmentSuggestionRepository suggestions,
    SuggestionAcceptor acceptor) : ICommandHandler<AcceptReplenishmentSuggestionCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AcceptReplenishmentSuggestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ReplenishmentSuggestion suggestion = await suggestions
            .FindAsync(command.SuggestionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("replenishment suggestion", command.SuggestionId);

        if (suggestion is { Status: SuggestionStatus.Accepted, DownstreamDocumentId: { } accepted })
        {
            return accepted;
        }

        Guid downstream = suggestion.Source switch
        {
            SuggestionSource.Procurement => await acceptor
                .RaiseRequisitionAsync(suggestion, suggestion.SuggestedQuantity, cancellationToken)
                .ConfigureAwait(false),
            SuggestionSource.Transfer => await acceptor
                .TransferAsync(suggestion, suggestion.SuggestedQuantity, cancellationToken)
                .ConfigureAwait(false),
            _ => throw PlanningRuleException.BadInput($"Unknown suggestion source '{suggestion.Source}'."),
        };

        suggestion.Accept(downstream);

        return downstream;
    }
}

/// <summary>Accepts an open suggestion with an amended quantity. The recommendation is preserved.</summary>
/// <param name="SuggestionId">The suggestion.</param>
/// <param name="AmendedQuantity">What goes on the downstream document. Positive.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AmendAcceptReplenishmentSuggestionCommand(Guid SuggestionId, decimal AmendedQuantity)
    : ICommand<Guid>;

/// <summary>Rejects a malformed amend-accept.</summary>
public sealed class AmendAcceptReplenishmentSuggestionCommandValidator
    : AbstractValidator<AmendAcceptReplenishmentSuggestionCommand>
{
    /// <summary>Builds the rules.</summary>
    public AmendAcceptReplenishmentSuggestionCommandValidator()
    {
        RuleFor(command => command.SuggestionId).NotEmpty();
        RuleFor(command => command.AmendedQuantity).GreaterThan(0m);
    }
}

/// <summary>Amends and accepts. The machine's original recommendation stays on the suggestion.</summary>
public sealed class AmendAcceptReplenishmentSuggestionCommandHandler(
    IReplenishmentSuggestionRepository suggestions,
    SuggestionAcceptor acceptor)
    : ICommandHandler<AmendAcceptReplenishmentSuggestionCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AmendAcceptReplenishmentSuggestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ReplenishmentSuggestion suggestion = await suggestions
            .FindAsync(command.SuggestionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("replenishment suggestion", command.SuggestionId);

        if (suggestion is { Status: SuggestionStatus.Accepted, DownstreamDocumentId: { } accepted })
        {
            return accepted;
        }

        Guid downstream = suggestion.Source switch
        {
            SuggestionSource.Procurement => await acceptor
                .RaiseRequisitionAsync(suggestion, command.AmendedQuantity, cancellationToken)
                .ConfigureAwait(false),
            SuggestionSource.Transfer => await acceptor
                .TransferAsync(suggestion, command.AmendedQuantity, cancellationToken)
                .ConfigureAwait(false),
            _ => throw PlanningRuleException.BadInput($"Unknown suggestion source '{suggestion.Source}'."),
        };

        suggestion.AmendAndAccept(command.AmendedQuantity, downstream);

        return downstream;
    }
}

/// <summary>Rejects an open suggestion. Creates nothing downstream.</summary>
/// <param name="SuggestionId">The suggestion.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RejectReplenishmentSuggestionCommand(Guid SuggestionId) : ICommand;

/// <summary>Rejects a malformed reject.</summary>
public sealed class RejectReplenishmentSuggestionCommandValidator
    : AbstractValidator<RejectReplenishmentSuggestionCommand>
{
    /// <summary>Builds the rules.</summary>
    public RejectReplenishmentSuggestionCommandValidator()
        => RuleFor(command => command.SuggestionId).NotEmpty();
}

/// <summary>Rejects the suggestion.</summary>
public sealed class RejectReplenishmentSuggestionCommandHandler(IReplenishmentSuggestionRepository suggestions)
    : ICommandHandler<RejectReplenishmentSuggestionCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        RejectReplenishmentSuggestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ReplenishmentSuggestion suggestion = await suggestions
            .FindAsync(command.SuggestionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("replenishment suggestion", command.SuggestionId);

        suggestion.Reject();

        return Unit.Value;
    }
}
