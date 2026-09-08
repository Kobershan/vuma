using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>
/// Commits a sourcing plan as a saga: plan from the group view, one serialisable reservation leg
/// per supplying company, compensated by releases if a leg fails hard (ADR-102, ADR-116).
/// </summary>
/// <remarks>
/// <para>
/// An application service, NOT a command handler: a handler may resolve at most one company
/// context, while a commit spans several — each leg runs in its own company scope through
/// <c>ISourcingCompanyGateway</c>, and this service touches only the registry itself.
/// </para>
/// <para>
/// Two rounds, then backorder. Round one executes each leg for its planned share. Round two
/// re-sources what fell short from fresh authoritative reads, once, by releasing a partial hold
/// and re-holding the fuller amount as a plain hold under the same group reference — never a
/// second row on the leg's key, which already holds that line. Whatever is still uncovered
/// becomes backorder. A stale projection can therefore cause a re-plan; it can never cause a
/// negative, because nothing commits a company's stock except a serialisable transaction inside
/// that company's own database.
/// </para>
/// <para>
/// A hard leg failure (the company, not the arithmetic) compensates everything taken and the
/// order is not created. Compensation releases by <em>group document reference</em> per company,
/// which also catches re-sourced remainders and self-heals a crashed-then-retried commit, whose
/// ghost holds share the order's number.
/// </para>
/// <para>
/// The 06d saga <em>records</em> (<c>SagaIntent</c>/<c>SagaLeg</c>) are driven directly because
/// <c>SagaCoordinator.DispatchLegAsync</c> is a documented no-op pending 07C-004, whose plan keeps
/// its dispatch table in 07c. The gateway is written as one intent type plus leg-shaped calls for
/// exactly that convergence; see TASK-08C-002's follow-ups.
/// </para>
/// </remarks>
public sealed class SourcingCommitService : ISourcingCommitService
{
    /// <summary>The saga intent type for sourcing commits.</summary>
    public const string IntentType = "availability.sourcing-commit";

    private readonly VumaRegistryDbContext _registry;
    private readonly ISourcingPlanner _planner;
    private readonly ISourcingCompanyGateway _companies;
    private readonly ISplitDocumentBuilder _splits;
    private readonly IReservationExpiryPolicy _expiry;
    private readonly ICompanyLinkGuard _links;
    private readonly IClock _clock;
    private readonly IHybridClock _hybridClock;
    private readonly ILogger<SourcingCommitService> _logger;

    /// <summary>Builds the service. All collaborators are scoped; legs run in the gateway's scopes.</summary>
    public SourcingCommitService(
        VumaRegistryDbContext registry,
        ISourcingPlanner planner,
        ISourcingCompanyGateway companies,
        ISplitDocumentBuilder splits,
        IReservationExpiryPolicy expiry,
        ICompanyLinkGuard links,
        IClock clock,
        IHybridClock hybridClock,
        ILogger<SourcingCommitService> logger)
    {
        _registry = registry;
        _planner = planner;
        _companies = companies;
        _splits = splits;
        _expiry = expiry;
        _links = links;
        _clock = clock;
        _hybridClock = hybridClock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommittedSourcingPlan> CommitAsync(
        SourcingCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId == Guid.Empty)
        {
            throw new ArgumentException("A commit needs its tenant.", nameof(request));
        }

        if (request.OrderingCompanyId == Guid.Empty)
        {
            throw new ArgumentException("A commit needs its ordering company.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InitiatedBy);

        EnsureSourceCoherent(request.Source);
        RejectDuplicateSkus(request.Source);

        SagaIntent? existing = await _registry.SagaIntents
            .Include(intent => intent.Legs)
            .FirstOrDefaultAsync(
                intent => intent.TenantId == request.TenantId
                    && intent.Type == IntentType
                    && intent.IdempotencyKey == request.IdempotencyKey.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return await ReplayOrRecoverAsync(existing, request, cancellationToken).ConfigureAwait(false);
        }

        SourcingPlan plan = await _planner.PlanAsync(
            request.Source.Demands,
            request.OrderingCompanyId,
            request.ProximityLocations,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<Guid> suppliers = plan.SupplyingCompanies
            .Where(company => company != request.OrderingCompanyId)
            .OrderBy(company => company)
            .ToList();

        foreach (Guid supplier in suppliers)
        {
            await _links.RequireLinkAsync(
                    request.TenantId, request.OrderingCompanyId, supplier,
                    CompanyLinkScope.SharedSourcing, cancellationToken)
                .ConfigureAwait(false);
        }

        Company ordering = await _registry.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                company => company.TenantId == request.TenantId && company.Id == request.OrderingCompanyId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The ordering company is not registered.");

        if (ordering.OperatorId == Guid.Empty)
        {
            throw new InvalidOperationException("The ordering company has no Operator ID; nothing may source across companies for it (ADR-121).");
        }

        SagaIntent intent = SagaIntent.Create(
            request.TenantId,
            IntentType,
            request.IdempotencyKey.Trim(),
            _clock.UtcNow,
            SerialisePlan(request, plan));
        intent.Authorize(ordering.OperatorId, request.InitiatedBy.Trim(), _hybridClock.Next().ToString());

        // Legs for the plan's suppliers, in company order so "already taken" is well-defined when
        // a later leg fails. A backorder-only commit still records one no-op leg for the ordering
        // company: Start requires at least one leg, and the commit happened (its answer was
        // "backorder everything"), so the intent must say so rather than sit Pending for ever.
        IReadOnlyList<Guid> legCompanies = suppliers.Count > 0
            ? suppliers
            : [request.OrderingCompanyId];

        foreach (Guid company in legCompanies)
        {
            intent.AddLeg(company);
        }

        _registry.SagaIntents.Add(intent);
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        intent.Start("sourcing-commit");
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(intent, request, plan, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommittedSourcingPlan> ExecuteAsync(
        SagaIntent intent,
        SourcingCommitRequest request,
        SourcingPlan plan,
        CancellationToken cancellationToken)
    {
        TimeSpan? lifetime = await _expiry.ResolveAsync(ReservationSource.Order, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset? expiresAt = lifetime is null ? null : _clock.UtcNow.Add(lifetime.Value);

        var state = new CommitState(request);
        List<SourcingLegOutcome> legs = [];

        try
        {
            foreach (SagaLeg leg in intent.Legs.OrderBy(leg => leg.CompanyId))
            {
                SourcingLegOutcome outcome = await ExecuteLegAsync(
                    intent, leg, request, plan, expiresAt, state,
                    cancellationToken).ConfigureAwait(false);
                legs.Add(outcome);
            }

            await ResourceOnceAsync(
                request, plan, expiresAt, state,
                cancellationToken).ConfigureAwait(false);

            SourcingPlan committed = state.ToCommittedPlan(plan);
            IReadOnlyList<SplitOrderDraft> drafts = _splits.Build(request.Source, committed);

            List<Guid> splitOrderIds = [];
            foreach (SplitOrderDraft draft in drafts)
            {
                Guid orderId = await _companies.WriteSplitOrderAsync(
                    request.TenantId,
                    draft.CompanyId,
                    request.Source.OrderNumber,
                    request.Source,
                    draft,
                    cancellationToken).ConfigureAwait(false);
                splitOrderIds.Add(orderId);
            }

            intent.Complete();
            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

            return new CommittedSourcingPlan(intent.Id, committed, legs, splitOrderIds, WasReplay: false);
        }
        catch (Exception failure) when (failure is not SourcingCommitConflictException)
        {
            _logger.LogWarning(
                failure,
                "Sourcing commit {IntentId} failed; compensating {Taken} companies by release.",
                intent.Id,
                state.TakenCompanies.Count);

            await CompensateAsync(request, state.TakenCompanies, cancellationToken).ConfigureAwait(false);

            intent.Compensate();
            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

            throw new SourcingCommitFailedException(intent.Id, failure.Message, failure);
        }
    }

    /// <summary>Mutable per-commit working state: holds taken, amounts held, and what is still short.</summary>
    private sealed class CommitState(SourcingCommitRequest Request)
    {
        /// <summary>Live reservation id per (line, company).</summary>
        public Dictionary<Guid, Dictionary<Guid, Guid>> Reservations { get; } =
            Request.Source.Demands.ToDictionary(line => line.LineId, _ => new Dictionary<Guid, Guid>());

        /// <summary>Live held quantity per (line, company).</summary>
        public Dictionary<(Guid Line, Guid Company), decimal> HeldAmounts { get; } = [];

        /// <summary>Still uncovered vs the plan, per line. Grows in round one, shrinks in round two.</summary>
        public Dictionary<Guid, Quantity> Shortfalls { get; } = [];

        /// <summary>Every company holding anything under this commit, for compensation.</summary>
        public HashSet<Guid> TakenCompanies { get; } = [];

        public void RecordHold(Guid lineId, Guid companyId, Guid reservationId, decimal held)
        {
            Reservations[lineId][companyId] = reservationId;
            HeldAmounts[(lineId, companyId)] = held;
            TakenCompanies.Add(companyId);
        }

        public void RecordShortfall(Guid lineId, Quantity uncovered)
        {
            if (Shortfalls.TryGetValue(lineId, out Quantity accumulated))
            {
                Shortfalls[lineId] = accumulated + uncovered;
            }
            else
            {
                Shortfalls[lineId] = uncovered;
            }
        }

        public SourcingPlan ToCommittedPlan(SourcingPlan plan)
        {
            List<SourcingPlanLine> lines = plan.Lines.Select(planLine =>
            {
                Quantity extra = Shortfalls.TryGetValue(planLine.LineId, out Quantity shortfall)
                    ? shortfall
                    : new Quantity(0m, planLine.Backorder.UnitOfMeasure);

                return planLine with { Backorder = planLine.Backorder + extra };
            }).ToList();

            return plan with { Lines = lines };
        }
    }

    private async Task<SourcingLegOutcome> ExecuteLegAsync(
        SagaIntent intent,
        SagaLeg leg,
        SourcingCommitRequest request,
        SourcingPlan plan,
        DateTimeOffset? expiresAt,
        CommitState state,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, Guid> held = [];
        Dictionary<Guid, Quantity> shortByLine = [];

        try
        {
            leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);

            foreach (SourcingPlanLine planLine in plan.Lines)
            {
                SourcingAllocation? share = planLine.Allocations
                    .FirstOrDefault(allocation => allocation.CompanyId == leg.CompanyId);

                if (share is null || share.Quantity.IsZero)
                {
                    continue;
                }

                SourcingDemandLine demand = request.Source.Demands.First(line => line.LineId == planLine.LineId);

                ReserveOutcome outcome = await _companies.ReserveLegAsync(
                    new ReservationLegRequest(
                        request.TenantId,
                        leg.CompanyId,
                        share.LocationId,
                        demand.ItemId,
                        demand.ItemVariantId,
                        share.Quantity,
                        request.Source.OrderId,
                        request.Source.OrderNumber,
                        expiresAt,
                        intent.Id,
                        leg.LegId),
                    cancellationToken).ConfigureAwait(false);

                if (outcome.ReservationId.HasValue)
                {
                    held[planLine.LineId] = outcome.ReservationId.Value;
                    state.RecordHold(planLine.LineId, leg.CompanyId, outcome.ReservationId.Value, outcome.Held.Value);
                }

                Quantity uncovered = share.Quantity - outcome.Held;
                if (uncovered.Value > 0m)
                {
                    shortByLine[planLine.LineId] = uncovered;
                    state.RecordShortfall(planLine.LineId, uncovered);
                }
            }

            leg.Acknowledge(_clock.UtcNow);
        }
        catch (Exception failure)
        {
            leg.Fail(failure.Message);
            throw new SourcingLegFailedException(leg.CompanyId, leg.LegId, failure.Message, failure);
        }

        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        return new SourcingLegOutcome(leg.CompanyId, leg.LegId, held, shortByLine);
    }

    private async Task ResourceOnceAsync(
        SourcingCommitRequest request,
        SourcingPlan plan,
        DateTimeOffset? expiresAt,
        CommitState state,
        CancellationToken cancellationToken)
    {
        foreach (SourcingPlanLine planLine in plan.Lines)
        {
            if (!state.Shortfalls.TryGetValue(planLine.LineId, out Quantity shortfall) || shortfall.Value <= 0m)
            {
                continue;
            }

            SourcingDemandLine demand = request.Source.Demands.First(line => line.LineId == planLine.LineId);
            decimal remaining = shortfall.Value;

            // Fresh authoritative reads, in company order: the plan may be stale in either
            // direction, and only the owning company knows what is really there (ADR-102).
            // Only companies already holding this line are re-sourced: the re-source round heals
            // broken legs, it does not recruit new suppliers (that is a re-plan).
            foreach (SourcingAllocation share in planLine.Allocations
                .OrderBy(allocation => allocation.CompanyId))
            {
                if (remaining <= 0m)
                {
                    break;
                }

                if (!state.Reservations[planLine.LineId].TryGetValue(share.CompanyId, out Guid heldId))
                {
                    continue;
                }

                decimal previouslyHeld = state.HeldAmounts[(planLine.LineId, share.CompanyId)];

                LocalAvailability fresh = await _companies.ReadLocalAsync(
                    request.TenantId, share.CompanyId, share.LocationId,
                    demand.ItemId, demand.ItemVariantId, cancellationToken).ConfigureAwait(false);

                // Fresh available excludes the live hold; the release below frees it, so the
                // re-hold asks previous plus the extra the fresh figure allows.
                decimal take = Math.Min(remaining, fresh.Promise.Available.Value);
                if (take <= 0m)
                {
                    continue;
                }

                // Release the partial and re-hold the fuller amount as a plain hold under the same
                // group reference — never a second row on the leg's key, which already holds this
                // line. One live hold per (company, line) at all times, so the outcome map stays
                // one-to-one and compensation by group reference stays complete. Two transactions,
                // so a race between them backorders more rather than overselling.
                await _companies.ReleaseHoldAsync(
                    request.TenantId, share.CompanyId, heldId, cancellationToken).ConfigureAwait(false);

                ReserveOutcome topUp = await _companies.ReserveLegAsync(
                    new ReservationLegRequest(
                        request.TenantId,
                        share.CompanyId,
                        share.LocationId,
                        demand.ItemId,
                        demand.ItemVariantId,
                        new Quantity(previouslyHeld + take, demand.Demanded.UnitOfMeasure),
                        request.Source.OrderId,
                        request.Source.OrderNumber,
                        expiresAt,
                        IntentId: null,
                        LegId: null),
                    cancellationToken).ConfigureAwait(false);

                if (topUp.ReservationId.HasValue)
                {
                    state.RecordHold(planLine.LineId, share.CompanyId, topUp.ReservationId.Value, topUp.Held.Value);
                    remaining -= topUp.Held.Value - previouslyHeld;
                }
                else
                {
                    // The re-hold found nothing (lost race): the released partial is gone from the
                    // map, and the shortfall grows back by what was released.
                    state.Reservations[planLine.LineId].Remove(share.CompanyId);
                    state.HeldAmounts.Remove((planLine.LineId, share.CompanyId));
                    remaining += previouslyHeld;
                }
            }

            state.Shortfalls[planLine.LineId] = new Quantity(
                Math.Max(0m, remaining), demand.Demanded.UnitOfMeasure);
        }
    }

    private async Task CompensateAsync(
        SourcingCommitRequest request,
        IReadOnlyCollection<Guid> takenCompanies,
        CancellationToken cancellationToken)
    {
        foreach (Guid company in takenCompanies.OrderBy(company => company))
        {
            try
            {
                await _companies.ReleaseGroupHoldsAsync(
                    request.TenantId, company, request.Source.OrderNumber, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                // Compensation must keep going: a company that is down now will be caught by the
                // expiry backstop, and the in-flight report names the intent either way.
                _logger.LogError(
                    failure,
                    "Compensation failed for company {CompanyId} on intent for {GroupRef}; the holds will expire.",
                    company,
                    request.Source.OrderNumber);
            }
        }
    }

    private async Task<CommittedSourcingPlan> ReplayOrRecoverAsync(
        SagaIntent existing,
        SourcingCommitRequest request,
        CancellationToken cancellationToken)
    {
        if (existing.State == SagaIntentState.Completed)
        {
            SourcingPlan stored = DeserialisePlan(request, existing.Payload);
            return new CommittedSourcingPlan(existing.Id, stored, [], [], WasReplay: true);
        }

        // A previous attempt never completed: release whatever it left open, close it out, and
        // refuse — the caller retries with a new key rather than inheriting a half-written saga.
        // Holds share the order's group reference, so recovery finds them whatever leg took them.
        HashSet<Guid> companies = existing.Legs.Select(leg => leg.CompanyId).ToHashSet();
        await CompensateAsync(request, companies, cancellationToken).ConfigureAwait(false);

        try
        {
            existing.Compensate();
        }
        catch (InvalidOperationException)
        {
            // Already compensated or timed out by a recovery racing this one: the holds are
            // released either way, which is the part that matters.
        }

        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        throw new SourcingCommitConflictException(
            existing.Id,
            existing.State,
            "A previous commit under this key did not complete. Its holds were released; retry with a new idempotency key.");
    }

    private async Task SaveRegistryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _registry.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres
            && postgres.SqlState is "23505")
        {
            throw new SourcingCommitConflictException(
                Guid.Empty,
                SagaIntentState.Pending,
                "This commit was already recorded. Retry with the same key to replay it, or a new key to re-plan.");
        }
    }

    private static void EnsureSourceCoherent(SourcingSourceOrder source)
    {
        if (source.OrderId == Guid.Empty)
        {
            throw new ArgumentException("The source order needs its id.", nameof(source));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(source.OrderNumber);

        if (source.Demands.Count == 0)
        {
            throw new ArgumentException("A commit needs at least one demand line.", nameof(source));
        }
    }

    private static void RejectDuplicateSkus(SourcingSourceOrder source)
    {
        // One leg holds at most one row per line, keyed by (intent, leg, location, sku): two demand
        // lines for one stock-keeping unit would alias onto one hold and misattribute the second
        // line's shortfall. Orders should not duplicate SKUs across lines; refuse loudly.
        HashSet<(Guid? Item, Guid? Variant)> seen = [];
        foreach (SourcingDemandLine demand in source.Demands)
        {
            if (!seen.Add((demand.ItemId, demand.ItemVariantId)))
            {
                throw new ArgumentException(
                    $"Demand lines duplicate a stock-keeping unit (line {demand.LineId}). "
                    + "Merge them into one line before committing.",
                    nameof(source));
            }
        }
    }

    private static string SerialisePlan(SourcingCommitRequest request, SourcingPlan plan)
        => JsonSerializer.Serialize(new
        {
            orderId = request.Source.OrderId,
            orderNumber = request.Source.OrderNumber,
            orderingCompany = request.OrderingCompanyId,
            lines = plan.Lines.Select(line => new
            {
                lineId = line.LineId,
                allocations = line.Allocations.Select(allocation => new
                {
                    company = allocation.CompanyId,
                    location = allocation.LocationId,
                    quantity = allocation.Quantity.Value,
                    uom = allocation.Quantity.UnitOfMeasure,
                }).ToArray(),
                backorder = new
                {
                    quantity = line.Backorder.Value,
                    uom = line.Backorder.UnitOfMeasure,
                },
            }).ToArray(),
        });

    private static SourcingPlan DeserialisePlan(SourcingCommitRequest request, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);

        List<SourcingPlanLine> lines = [];
        foreach (JsonElement line in document.RootElement.GetProperty("lines").EnumerateArray())
        {
            Guid lineId = line.GetProperty("lineId").GetGuid();
            string uom = line.GetProperty("backorder").GetProperty("uom").GetString()
                ?? request.Source.Demands.First(demand => demand.LineId == lineId).Demanded.UnitOfMeasure;

            List<SourcingAllocation> allocations = line.GetProperty("allocations")
                .EnumerateArray()
                .Select(allocation => new SourcingAllocation(
                    allocation.GetProperty("company").GetGuid(),
                    allocation.GetProperty("location").GetGuid(),
                    new Quantity(allocation.GetProperty("quantity").GetDecimal(), uom)))
                .ToList();

            JsonElement backorder = line.GetProperty("backorder");
            lines.Add(new SourcingPlanLine(
                lineId,
                allocations,
                new Quantity(backorder.GetProperty("quantity").GetDecimal(), uom)));
        }

        return new SourcingPlan(request.OrderingCompanyId, lines);
    }
}

/// <summary>The commit failed operationally after holds were taken; every taken hold was released.</summary>
/// <param name="IntentId">The compensated intent.</param>
/// <param name="Reason">Which leg failed and why.</param>
/// <param name="Inner">The leg failure.</param>
public sealed class SourcingCommitFailedException(Guid IntentId, string Reason, Exception? Inner = null)
    : InvalidOperationException($"Sourcing commit {IntentId} failed and was compensated: {Reason}", Inner);

/// <summary>One reservation leg failed hard (the company, not the arithmetic).</summary>
/// <param name="CompanyId">The failed leg's company.</param>
/// <param name="LegId">The failed leg.</param>
/// <param name="Reason">Why it failed.</param>
/// <param name="Inner">The underlying failure.</param>
public sealed class SourcingLegFailedException(Guid CompanyId, Guid LegId, string Reason, Exception? Inner = null)
    : InvalidOperationException($"Reservation leg {LegId} in company {CompanyId} failed: {Reason}", Inner);

/// <summary>A commit key is already in use by an incomplete attempt, or raced a duplicate.</summary>
/// <param name="IntentId">The existing intent, or empty when it could not be loaded.</param>
/// <param name="State">The existing intent's state.</param>
/// <param name="Advice">What the caller should do.</param>
public sealed class SourcingCommitConflictException(Guid IntentId, SagaIntentState State, string Advice)
    : InvalidOperationException($"Sourcing commit conflict on intent {IntentId} ({State}): {Advice}");
