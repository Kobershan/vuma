using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.FieldSales;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Sales;
using VumaRetail.Application.Sales.Commands;
using VumaRetail.Application.Sales.Pricing;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Domain.Workflow;
using VumaRetail.Finance.Posting;
using VumaRetail.Finance.Tax;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.Infrastructure.Sales;

namespace VumaRetail.Infrastructure.FieldSales;

/// <summary>
/// Converts an approved pro forma into a real order (and its invoices) through a resumable,
/// compensating saga: reprice, plan, credit hold, per-company reservations, order, invoices,
/// hold confirmation (Stage 14b, ADR-108).
/// </summary>
/// <remarks>
/// An application service, NOT a command handler: approval spans several company databases
/// (ADR-116). The shape mirrors the invoice-issue and mixed-basket sagas deliberately: intent
/// with an idempotency key, link checks before any write, legs in company order, replay returns
/// stored results, compensation in reverse with releases — never deletions.
/// </remarks>
public sealed class FieldSalesApprovalService : IFieldSalesApprovalService
{
    /// <summary>The saga intent type for pro forma approvals.</summary>
    public const string IntentType = "field-sales-approval";

    /// <summary>How long a credit hold lives while the saga runs.</summary>
    public static readonly TimeSpan HoldLifetime = TimeSpan.FromHours(24);

    private readonly VumaRegistryDbContext _registry;
    private readonly IServiceScopeFactory _scopes;
    private readonly ICompanyLinkGuard _links;
    private readonly IGroupCreditService _credit;
    private readonly IReplicationRegistry _replication;
    private readonly IReplicaWriter _replicas;
    private readonly IHybridClock _hybridClock;
    private readonly INodeIdentity _node;
    private readonly IClock _clock;
    private readonly ILogger<FieldSalesApprovalService> _logger;

    /// <summary>Builds the service. All collaborators are scoped; legs run in child scopes.</summary>
    public FieldSalesApprovalService(
        VumaRegistryDbContext registry,
        IServiceScopeFactory scopes,
        ICompanyLinkGuard links,
        IGroupCreditService credit,
        IReplicationRegistry replication,
        IReplicaWriter replicas,
        IHybridClock hybridClock,
        INodeIdentity node,
        IClock clock,
        ILogger<FieldSalesApprovalService> logger)
    {
        _registry = registry;
        _scopes = scopes;
        _links = links;
        _credit = credit;
        _replication = replication;
        _replicas = replicas;
        _hybridClock = hybridClock;
        _node = node;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ApprovedProForma> ApproveOrderAsync(
        Guid proFormaId, string decidedBy, CancellationToken cancellationToken = default)
    {
        if (proFormaId == Guid.Empty)
        {
            throw new ArgumentException("An approval needs its pro forma.", nameof(proFormaId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        // The pro forma lives in the ordering company's database — but which company that is,
        // is on the pro forma. Resolve it through the registry-side lookup first: the saga
        // re-reads the document inside the ordering company's own scope below.
        ProFormaHeader header = await LoadHeaderAsync(proFormaId, cancellationToken).ConfigureAwait(false);

        using IServiceScope scope = _scopes.CreateScope();
        Bind(scope, header.TenantId, header.CompanyId);

        await using VumaRetailDbContext orderingDb = await OpenCompanyDbAsync(
                scope, CompanyAccessMode.Write, cancellationToken)
            .ConfigureAwait(false);

        ProFormaOrder order = await new ProFormaOrderRepository(orderingDb)
            .FindAsync(proFormaId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No pro forma {proFormaId}.");

        if (order.Status is ProFormaStatus.Converted && order.ConvertedOrderId is { } convertedId)
        {
            // Crash after conversion, before the answer came back: the replay returns what the
            // first attempt created (invoice numbers re-read idempotently below), never a second
            // order. This is the §4.11 lesson applied to the saga boundary.
            IReadOnlyList<string> replayed = await ReplayInvoicesAsync(
                    scope, order, cancellationToken)
                .ConfigureAwait(false);
            return new ApprovedProForma(convertedId, replayed, order.RepriceDelta?.Amount ?? 0m);
        }

        // Fresh approvals arrive Submitted (the handler just decided); resumed attempts arrive
        // Approved (a crash between MarkApproved and completion). Anything else never had its
        // decision recorded here and must go through the handler, never call the saga directly.
        if (order.Status is not ProFormaStatus.Submitted and not ProFormaStatus.Approved)
        {
            throw FieldSalesException.IllegalTransition(order.Status, "convert without approval");
        }

        Company ordering = await _registry.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                company => company.TenantId == order.TenantId && company.Id == order.CompanyId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The ordering company is not registered.");

        if (ordering.OperatorId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The ordering company has no Operator ID; nothing may convert across companies for it (ADR-121).");
        }

        SagaIntent? existing = await _registry.SagaIntents
            .Include(intent => intent.Legs)
            .FirstOrDefaultAsync(
                intent => intent.TenantId == order.TenantId
                    && intent.Type == IntentType
                    && intent.IdempotencyKey == order.IdempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return await ResumeAsync(existing, scope, orderingDb, order, decidedBy, cancellationToken)
                .ConfigureAwait(false);
        }

        // Read-only steps first: reprice against today's list, then plan across the companies
        // the rep may sell for. Neither writes, so a failure here leaves nothing to compensate.
        RepriceOutcome reprice = await RepriceAsync(orderingDb, order, cancellationToken)
            .ConfigureAwait(false);
        SourcingPlan plan = await PlanAsync(scope, order, reprice, cancellationToken)
            .ConfigureAwait(false);

        foreach (Guid supplier in plan.SupplyingCompanies.Where(company => company != order.CompanyId))
        {
            await _links.RequireLinkAsync(
                    order.TenantId, order.CompanyId!.Value, supplier,
                    CompanyLinkScope.SharedSourcing, cancellationToken)
                .ConfigureAwait(false);
        }

        SagaIntent intent = SagaIntent.Create(
            order.TenantId,
            IntentType,
            order.IdempotencyKey,
            _clock.UtcNow,
            Serialise(order));
        intent.Authorize(ordering.OperatorId, decidedBy.Trim(), _hybridClock.Next().ToString());

        foreach (Guid supplier in plan.SupplyingCompanies.OrderBy(company => company))
        {
            intent.AddLeg(supplier);
        }

        _registry.SagaIntents.Add(intent);
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        intent.Start("field-sales-approval");
        await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(intent, scope, orderingDb, order, reprice, plan, decidedBy, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ApprovedProForma> ExecuteAsync(
        SagaIntent intent,
        IServiceScope scope,
        VumaRetailDbContext orderingDb,
        ProFormaOrder order,
        RepriceOutcome reprice,
        SourcingPlan plan,
        string decidedBy,
        CancellationToken cancellationToken)
    {
        // ADR-122: the link is checked at the point of use, on every execution — including a
        // resume, which re-enters here without passing through ApproveOrderAsync's check. A link
        // suspended after the first attempt must refuse the retry, not silently complete it.
        foreach (Guid supplier in plan.SupplyingCompanies.Where(company => company != order.CompanyId))
        {
            await _links.RequireLinkAsync(
                    order.TenantId, order.CompanyId!.Value, supplier,
                    CompanyLinkScope.SharedSourcing, cancellationToken)
                .ConfigureAwait(false);
        }

        Guid? holdId = null;
        List<(Guid ReservationId, Guid CompanyId)> reservationIds = [];
        Guid? orderId = null;

        try
        {
            // Marking approved here (not in the handler) is what makes the delta truthful —
            // it is the repriced figure the saga commits to, not the quoted one.
            if (order.Status is ProFormaStatus.Submitted)
            {
                order.MarkApproved(new Money(reprice.Delta, order.Currency), _clock.UtcNow);
            }

            foreach (SagaLeg leg in intent.Legs)
            {
                leg.MarkDispatched(_clock.UtcNow, intent.OperationStamp);
            }

            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);

            // Step 3 — credit hold first (ADR-108): never reserve against exhausted credit.
            // Reused across resume attempts: confirming twice is refused, so never take blind.
            if (order.CreditHoldId is { } existingHold)
            {
                holdId = existingHold;
            }
            else
            {
                holdId = await TakeHoldAsync(order, reprice.Gross, cancellationToken).ConfigureAwait(false);
                if (holdId.HasValue)
                {
                    order.RecordCreditHold(holdId.Value);
                }
            }

            // Step 4 — one local reservation per sourcing company. Shortfalls backorder the
            // remainder (the approver saw the delta); they never fail the approval.
            Dictionary<Guid, decimal> backordered = [];
            foreach (SourcingPlanLine planLine in plan.Lines)
            {
                foreach (SourcingAllocation share in planLine.Allocations)
                {
                    if (share.Quantity.IsZero)
                    {
                        continue;
                    }

                    ProFormaOrderLine source = order.Lines.First(line =>
                        line.Id == planLine.LineId);

                    ReserveOutcome outcome = await ReserveAsync(
                            order, share, source, intent,
                            intent.Legs.First(leg => leg.CompanyId == share.CompanyId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (outcome.ReservationId.HasValue)
                    {
                        reservationIds.Add((outcome.ReservationId.Value, share.CompanyId));
                    }

                    if (outcome.Shortfall.Value > 0m)
                    {
                        backordered[source.Id] = backordered.GetValueOrDefault(source.Id)
                            + outcome.Shortfall.Value;
                    }
                }
            }

            // Step 5 — the order, in the ordering company's database. Guarded by
            // ConvertedOrderId: a resume after a crash between creation and acknowledgement
            // reuses the live order rather than minting a second one. A compensated
            // (cancelled) order is not reused — cancellation is terminal for a document.
            if (order.ConvertedOrderId is { } existingOrderId
                && await OrderIsLiveAsync(scope, orderingDb, existingOrderId, cancellationToken).ConfigureAwait(false))
            {
                orderId = existingOrderId;
            }
            else
            {
                orderId = await WriteOrderAsync(
                        scope, orderingDb, order, reprice, backordered, decidedBy, cancellationToken)
                    .ConfigureAwait(false);
                order.MarkConverted(orderId.Value);
                await orderingDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            // Step 5b — the order owns the stock now: consume each leg's hold into it. The
            // chain read makes a resumed attempt skip what the first pass already consumed
            // instead of closing an already-closed chain, which CloseOnceAsync refuses.
            foreach ((Guid reservationId, Guid companyId) in reservationIds)
            {
                using IServiceScope consumeScope = _scopes.CreateScope();
                Bind(consumeScope, order.TenantId, companyId);
                await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                        consumeScope, CompanyAccessMode.Write, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<StockReservation> chain = await new StockReservationRepository(companyDb)
                    .ListChainAsync(reservationId, cancellationToken)
                    .ConfigureAwait(false);

                bool consumedByUs = chain.Any(row =>
                    row.SequenceNumber == 1 && row.ConsumedByReferenceId == orderId.Value);
                bool open = chain.Any(row => row.SequenceNumber == 0)
                    && chain.All(row => row.SequenceNumber == 0);

                if (!consumedByUs && open)
                {
                    await consumeScope.ServiceProvider
                        .GetRequiredService<ITradingCompanyGateway>()
                        .RunReservationAsync(
                            order.TenantId,
                            companyId,
                            provider => provider.GetRequiredService<IReservationService>()
                                .ConsumeAsync(reservationId, orderId.Value, cancellationToken),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // Step 6 — invoices through 10c (idempotent by key; splits per company).
            IReadOnlyList<string> invoices = await IssueInvoicesAsync(
                    scope, order, orderId.Value, reprice, plan, cancellationToken)
                .ConfigureAwait(false);

            // Step 7 — confirm the hold against the order that now exists. A resumed attempt
            // whose hold already confirmed treats that refusal as confirmed, not as failure.
            if (holdId.HasValue)
            {
                try
                {
                    await _credit.ConfirmHoldAsync(holdId.Value, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException already)
                {
                    _logger.LogInformation(
                        already,
                        "Credit hold {HoldId} for pro forma {ProFormaNumber} already confirmed; treating as confirmed.",
                        holdId.Value, order.ProFormaNumber);
                }
            }

            foreach (SagaLeg leg in intent.Legs)
            {
                leg.Acknowledge(_clock.UtcNow);
            }

            intent.Complete();
            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
            await orderingDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new ApprovedProForma(orderId.Value, invoices, reprice.Delta);
        }
        catch (Exception failure)
        {
            _logger.LogWarning(
                failure,
                "Pro forma {ProFormaNumber} approval failed; compensating in reverse.",
                order.ProFormaNumber);

            await CompensateAsync(order, holdId, reservationIds, orderId, failure.Message, cancellationToken)
                .ConfigureAwait(false);

            intent.Compensate();
            order.MarkApprovalFailed(failure.Message);
            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
            await orderingDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // A business refusal already carries its code (PROFORMA_CREDIT_EXHAUSTED and kin):
            // wrapping it would demote a 422-with-a-code into a 500 without one. Infrastructure
            // failures stay wrapped with the intent id for diagnosis.
            if (failure is FieldSalesException)
            {
                throw;
            }

            throw new FieldSalesApprovalFailedException(order.Id, intent.Id, failure.Message, failure);
        }
    }

    private async Task<RepriceOutcome> RepriceAsync(
        VumaRetailDbContext orderingDb,
        ProFormaOrder order,
        CancellationToken cancellationToken)
    {
        var prices = new PriceResolver(
            new PriceListRepository(orderingDb),
            new PromotionRepository(orderingDb));
        var taxEngine = new TaxEngine(new TaxRuleRepository(orderingDb));
        DateOnly today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        TimeOnly nowTime = TimeOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        List<RepricedLine> lines = [];
        decimal quoted = 0m;
        decimal repriced = 0m;

        foreach (ProFormaOrderLine line in order.Lines)
        {
            quoted += line.Gross.Amount;

            PriceResolution resolution = await prices.ResolveAsync(
                    new PriceResolutionRequest(
                        line.ItemId, line.ItemVariantId, CategoryCode: null,
                        line.QuantityValue, order.StoreId, today, nowTime, order.Currency),
                    cancellationToken)
                .ConfigureAwait(false);

            VumaRetail.Domain.Catalog.Item? item = line.ItemId.HasValue
                ? await orderingDb.Items
                    .AsNoTracking()
                    .FirstOrDefaultAsync(candidate => candidate.Id == line.ItemId!.Value, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            TaxCalculation calculation = await taxEngine.CalculateAsync(
                    item?.TaxClassCode ?? line.TaxCode, resolution.NetPayable, today, cancellationToken)
                .ConfigureAwait(false);

            // The engine already returns the correct gross for either tax treatment; adding
            // NetPayable + Tax on top double-counts VAT on tax-inclusive lists (the shelf price
            // already contains it). Likewise every downstream consumer (order lines, invoice
            // lines) computes Net = UnitPrice × qty − Discount, so the unit price must be net
            // of tax: the line net, with the whole-line discount added back, per unit.
            Money lineGross = calculation.GrossAmount;
            Money unitNet = (calculation.NetAmount + resolution.DiscountAmount) / line.QuantityValue;
            repriced += lineGross.Amount;

            lines.Add(new RepricedLine(
                line.Id, line.ItemId, line.ItemVariantId,
                unitNet, resolution.DiscountAmount, calculation.TaxAmount,
                resolution.PriceListId, resolution.Explanation));
        }

        decimal gross = lines.Sum(candidate => (candidate.UnitPrice * order.Lines.First(source => source.Id == candidate.LineId).QuantityValue - candidate.Discount + candidate.Tax).Amount);
        return new RepriceOutcome(lines, gross, repriced - quoted);
    }

    private async Task<SourcingPlan> PlanAsync(
        IServiceScope scope,
        ProFormaOrder order,
        RepriceOutcome reprice,
        CancellationToken cancellationToken)
    {
        ISourcingPlanner planner = scope.ServiceProvider.GetRequiredService<ISourcingPlanner>();

        List<SourcingDemandLine> demands = [];
        foreach (RepricedLine line in reprice.Lines)
        {
            ProFormaOrderLine source = order.Lines.First(candidate => candidate.Id == line.LineId);
            demands.Add(new SourcingDemandLine(
                line.LineId,
                line.ItemId,
                line.ItemVariantId,
                new Quantity(source.QuantityValue, source.QuantityUom),
                line.UnitPrice,
                line.UnitPrice * source.QuantityValue,
                new Money(0m, order.Currency),
                line.UnitPrice * source.QuantityValue,
                PriceListId: null));
        }

        return await planner.PlanAsync(
                demands, order.CompanyId!.Value, [], cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Guid?> TakeHoldAsync(
        ProFormaOrder order, decimal gross, CancellationToken cancellationToken)
    {
        IReadOnlyList<CreditGroupSummary> groups = await _credit.ListGroupsForCompanyAsync(
                order.TenantId, order.CompanyId!.Value, cancellationToken)
            .ConfigureAwait(false);

        // No group, no hold: cash and COD sales consume no credit (ADR-111's shape — the hold
        // exists to protect a limit, and with no limit there is nothing to protect).
        CreditGroupSummary? group = groups.OrderBy(candidate => candidate.Id).FirstOrDefault();
        if (group is null)
        {
            return null;
        }

        HoldResult held = await _credit.TryHoldAsync(
                order.TenantId, group.Id, order.CompanyId.Value,
                gross, order.Currency, order.ProFormaNumber, HoldLifetime, cancellationToken)
            .ConfigureAwait(false);

        if (!held.Success)
        {
            CreditPosition position = await _credit.GetPositionAsync(
                    order.TenantId, group.Id, cancellationToken)
                .ConfigureAwait(false);
            throw FieldSalesException.CreditExhausted(position.Limit, position.Available, position.Currency);
        }

        return held.HoldId;
    }

    private async Task<ReserveOutcome> ReserveAsync(
        ProFormaOrder order,
        SourcingAllocation share,
        ProFormaOrderLine source,
        SagaIntent intent,
        SagaLeg leg,
        CancellationToken cancellationToken)
    {
        using IServiceScope legScope = _scopes.CreateScope();
        Bind(legScope, order.TenantId, share.CompanyId);

        Guid locationId = await legScope.ServiceProvider
            .GetRequiredService<ITradingCompanyGateway>()
            .ResolveDefaultLocationAsync(order.TenantId, share.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        return await legScope.ServiceProvider
            .GetRequiredService<ITradingCompanyGateway>()
            .RunReservationAsync(
                order.TenantId,
                share.CompanyId,
                provider => provider.GetRequiredService<IReservationService>().ReserveAsync(
                    locationId,
                    source.ItemId == Guid.Empty ? null : source.ItemId,
                    source.ItemVariantId,
                    share.Quantity,
                    ReservationSource.ProFormaApproval,
                    order.Id,
                    order.ProFormaNumber,
                    null,
                    intent.Id,
                    leg.LegId,
                    $"Pro forma {order.ProFormaNumber}",
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> OrderIsLiveAsync(
        IServiceScope scope,
        VumaRetailDbContext orderingDb,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        SalesOrder? existing = await orderingDb.SalesOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken)
            .ConfigureAwait(false);
        return existing is not null && existing.Status is not SalesOrderStatus.Cancelled;
    }

    private async Task<Guid> WriteOrderAsync(
        IServiceScope scope,
        VumaRetailDbContext orderingDb,
        ProFormaOrder order,
        RepriceOutcome reprice,
        Dictionary<Guid, decimal> backordered,
        string decidedBy,
        CancellationToken cancellationToken)
    {
        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();

        await using var transaction = await orderingDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var numbers = new DocumentNumberSequence(orderingDb, scopedTenant);
            string orderNumber = await numbers.NextAsync("ORD", cancellationToken).ConfigureAwait(false);

            Guid locationId = await scope.ServiceProvider
                .GetRequiredService<ITradingCompanyGateway>()
                .ResolveDefaultLocationAsync(order.TenantId, order.CompanyId!.Value, cancellationToken)
                .ConfigureAwait(false);

            SalesOrder salesOrder = SalesOrder.Create(
                order.TenantId,
                scopedTenant.StoreId,
                orderNumber,
                order.PartnerId,
                SalesChannel.Rep,
                order.DeliveryAddress is null ? OrderFulfilmentType.ClickAndCollect : OrderFulfilmentType.Delivery,
                locationId,
                order.DeliveryAddress,
                order.Currency,
                _clock.UtcNow,
                requestedFulfilmentDate: null);
            salesOrder.AssignCompany(order.CompanyId.Value);
            salesOrder.AssignGroupDocument(order.ProFormaNumber);

            foreach (RepricedLine line in reprice.Lines)
            {
                ProFormaOrderLine source = order.Lines.First(candidate => candidate.Id == line.LineId);
                SalesOrderLine orderLine = salesOrder.AddLine(
                    line.ItemId, line.ItemVariantId,
                    new Quantity(source.QuantityValue, source.QuantityUom));
                orderLine.AssignCompany(order.CompanyId.Value);
                orderLine.ApplyPricing(
                    line.UnitPrice, line.Discount, line.Tax, line.PriceListId, line.Promotions);

                decimal shortfall = backordered.GetValueOrDefault(line.LineId);
                if (shortfall > 0m)
                {
                    orderLine.RecordAllocationOutcome(new Quantity(shortfall, source.QuantityUom));
                }
            }

            salesOrder.Confirm(_clock.UtcNow);

            orderingDb.SalesOrders.Add(salesOrder);
            foreach (SalesOrderLine orderLine in salesOrder.Lines)
            {
                orderingDb.SalesOrderLines.Add(orderLine);
            }

            CompanyOutboxCapture.Capture(
                orderingDb, _replication, _replicas, _hybridClock, _node, _clock, salesOrder);

            audit.Stamp(orderingDb);
            await orderingDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            _logger.LogDebug(
                "Pro forma {ProFormaNumber} converted into order {OrderNumber}.",
                order.ProFormaNumber, orderNumber);

            return salesOrder.Id;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IReadOnlyList<string>> IssueInvoicesAsync(
        IServiceScope scope,
        ProFormaOrder order,
        Guid orderId,
        RepriceOutcome reprice,
        SourcingPlan plan,
        CancellationToken cancellationToken)
    {
        // Segments split quantity; money pro-rates to exact sums (last share takes the remainder
        // dust — the same cent-exact discipline as the tender allocator, ADR-145's shape).
        List<InvoiceCompanySegment> segments = [];
        foreach (Guid companyId in plan.SupplyingCompanies.OrderBy(company => company))
        {
            List<InvoiceLineInput> inputs = [];
            foreach (SourcingPlanLine planLine in plan.Lines)
            {
                IReadOnlyList<SourcingAllocation> shares = planLine.Allocations
                    .Where(allocation => allocation.CompanyId == companyId && !allocation.Quantity.IsZero)
                    .ToList();
                if (shares.Count == 0)
                {
                    continue;
                }

                RepricedLine repriced = reprice.Lines.First(line => line.LineId == planLine.LineId);
                ProFormaOrderLine source = order.Lines.First(line => line.Id == planLine.LineId);

                decimal lineQty = source.QuantityValue;
                decimal allocated = 0m;
                for (int index = 0; index < shares.Count; index++)
                {
                    bool last = index == shares.Count - 1;
                    decimal qty = last
                        ? lineQty - allocated
                        : decimal.Round(lineQty * shares[index].Quantity.Value / planLine.AllocatedValue, 6);
                    allocated += qty;

                    decimal ratio = lineQty == 0m ? 0m : qty / lineQty;
                    inputs.Add(new InvoiceLineInput(
                        repriced.ItemId == Guid.Empty ? null : repriced.ItemId,
                        repriced.ItemVariantId,
                        qty,
                        source.QuantityUom,
                        decimal.Round(repriced.UnitPrice.Amount * ratio / (qty == 0m ? 1m : qty) * qty, 4),
                        decimal.Round(repriced.Discount.Amount * ratio, 4),
                        decimal.Round(repriced.Tax.Amount * ratio, 4),
                        source.PackSizeDescription,
                        repriced.PriceListId,
                        SourceLineId: source.Id));
                }
            }

            if (inputs.Count > 0)
            {
                segments.Add(new InvoiceCompanySegment(companyId, inputs));
            }
        }

        if (segments.Count == 0)
        {
            throw new FieldSalesException(
                "PROFORMA_NOTHING_SOURCED",
                $"Pro forma {order.ProFormaNumber} sourced nothing; there is nothing to invoice.");
        }

        IInvoiceIssuingService issuing = scope.ServiceProvider.GetRequiredService<IInvoiceIssuingService>();
        IReadOnlyList<IssuedInvoice> issued = await issuing.IssueAsync(
                new InvoiceIssuingRequest(
                    order.TenantId,
                    order.CompanyId!.Value,
                    orderId,
                    order.ProFormaNumber,
                    InvoiceSourceType.Order,
                    order.PartnerId,
                    order.Currency,
                    segments,
                    order.ProFormaNumber,
                    $"{order.IdempotencyKey}-invoices",
                    $"field-sales:{order.RepId}"),
                cancellationToken)
            .ConfigureAwait(false);

        return [.. issued.Select(invoice => invoice.InvoiceNumber)];
    }

    private async Task CompensateAsync(
        ProFormaOrder order,
        Guid? holdId,
        List<(Guid ReservationId, Guid CompanyId)> reservationIds,
        Guid? orderId,
        string reason,
        CancellationToken cancellationToken)
    {
        // Reverse order: reservations, then the order, then the hold. Releases, never deletions.
        // Each hold releases through its own supplying company, not the ordering one: a
        // cross-company hold lives in its supplier's database and no other.
        foreach ((Guid reservationId, Guid companyId) in reservationIds)
        {
            try
            {
                using IServiceScope legScope = _scopes.CreateScope();
                Bind(legScope, order.TenantId, companyId);
                await legScope.ServiceProvider
                    .GetRequiredService<ITradingCompanyGateway>()
                    .RunReservationAsync(
                        order.TenantId,
                        companyId,
                        provider => provider.GetRequiredService<IReservationService>()
                            .ReleaseAsync(reservationId, $"Pro forma {order.ProFormaNumber} compensation", cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                _logger.LogError(
                    failure,
                    "Compensation of pro forma {ProFormaNumber} could not release hold {ReservationId}.",
                    order.ProFormaNumber, reservationId);
            }
        }

        if (orderId.HasValue)
        {
            try
            {
                using IServiceScope orderScope = _scopes.CreateScope();
                Bind(orderScope, order.TenantId, order.CompanyId!.Value);
                await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                        orderScope, CompanyAccessMode.Write, cancellationToken)
                    .ConfigureAwait(false);

                SalesOrder? salesOrder = await companyDb.SalesOrders
                    .Include(sales => sales.Lines)
                    .FirstOrDefaultAsync(sales => sales.Id == orderId.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (salesOrder is not null)
                {
                    foreach (SalesOrderLine line in salesOrder.Lines)
                    {
                        try
                        {
                            line.Cancel();
                        }
                        catch (OrdersRuleException)
                        {
                            // Already beyond cancellation (fulfilled underneath us): the order stays
                            // for operations to unwind, and the failure names it. Never force it.
                            _logger.LogError(
                                "Compensation of pro forma {ProFormaNumber} found order {OrderId} beyond cancellation.",
                                order.ProFormaNumber, orderId.Value);
                            throw;
                        }
                    }

                    salesOrder.EnsureCancellable();
                    salesOrder.MarkCancelled($"Pro forma {order.ProFormaNumber} approval failed: {reason}", "field-sales-saga", _clock.UtcNow);
                    await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception failure)
            {
                _logger.LogError(
                    failure,
                    "Compensation of pro forma {ProFormaNumber} could not cancel order {OrderId}.",
                    order.ProFormaNumber, orderId);
            }
        }

        if (holdId.HasValue)
        {
            try
            {
                await _credit.ReleaseHoldAsync(holdId.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                _logger.LogError(
                    failure,
                    "Compensation of pro forma {ProFormaNumber} could not release credit hold {HoldId}.",
                    order.ProFormaNumber, holdId.Value);
            }
        }
    }

    /// <inheritdoc />
    public async Task<Guid> ApproveCreditNoteAsync(
        Guid creditNoteId, string decidedBy, CancellationToken cancellationToken = default)
    {
        if (creditNoteId == Guid.Empty)
        {
            throw new ArgumentException("An approval needs its credit proposal.", nameof(creditNoteId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        using IServiceScope scope = _scopes.CreateScope();

        // The origin invoice's company owns this entire path (ADR-128): resolve the company first
        // by scanning bound contexts is backwards — instead read the proposal through a
        // tenant-wide lookup is impossible (company databases are separate), so the proposal's
        // own company (captured at scan time, link-checked then) is authoritative.
        ProFormaHeader header = await LoadCreditHeaderAsync(creditNoteId, cancellationToken).ConfigureAwait(false);
        Bind(scope, header.TenantId, header.CompanyId);

        await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                scope, CompanyAccessMode.Write, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();
        ILoggerFactory loggers = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        // The proposal raises the return through Stage 10's own handlers, built here against the
        // origin company's database — never a parallel credit-note document (rule: Stage 10 owns
        // crediting, this module only proposes).
        var returns = new SalesReturnRepository(companyDb, scopedTenant);
        var sales = new SaleRepository(companyDb);
        var numbers = new DocumentNumberSequence(companyDb, scopedTenant);
        var principal = new FixedPrincipalAccessor(decidedBy);

        var createHandler = new CreateSalesReturnCommandHandler(returns, sales, numbers, principal, _clock);

        // Resolve the origin sale from the invoice: till sales carry their sale id as the source
        // reference. Anything else is refused with a code, not guessed at.
        Invoice origin = await companyDb.Invoices
            .Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(invoice => invoice.Id == header.InvoiceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FieldSalesException(
                "CREDIT_INVOICE_NOT_FOUND",
                $"Origin invoice {header.InvoiceId} is not in company {header.CompanyId}.");

        if (origin.SourceDocumentType is not InvoiceSourceType.Sale
            || !Guid.TryParse(origin.SourceDocumentRef, out Guid saleId))
        {
            throw new FieldSalesException(
                "CREDIT_SOURCE_NOT_RETURNABLE",
                $"Invoice {origin.InvoiceNumber} did not come from a till sale in this company; " +
                "credit it through the order-return path instead.");
        }

        Sale? sale = await sales.FindAsync(saleId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException(
                "CREDIT_SALE_NOT_FOUND",
                $"Origin sale {saleId} is not in company {header.CompanyId}.");

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            // First-fit matching by SKU against still-unreturned quantity: the proposal names
            // invoice lines, the return names sale lines, and the SKU is the honest bridge.
            // Multi-line same-SKU ambiguity resolves in line order and is asserted by test.
            Guid returnId = await createHandler.HandleAsync(
                    new CreateSalesReturnCommand(
                        sale.Id, $"Pro forma credit {header.Number} approved by {decidedBy}",
                        Domain.Pos.TenderType.Cash),
                    cancellationToken)
                .ConfigureAwait(false);

            // The line handler reads the return back through the repository, which queries the
            // database rather than the change tracker: persist the draft first. Still inside the
            // serializable transaction, so the saga stays atomic.
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var addHandler = new AddSalesReturnLineCommandHandler(returns, sales);
            var remaining = sale.LiveLines.ToDictionary(line => line.Id, line => line.Quantity.Value);

            foreach (CreditProposalLine proposal in header.Lines)
            {
                SaleLine? match = sale.LiveLines
                    .Where(line => line.ItemId == proposal.ItemId
                        && line.ItemVariantId == proposal.ItemVariantId
                        && remaining.GetValueOrDefault(line.Id) >= proposal.Quantity)
                    .OrderBy(line => line.LineNumber)
                    .FirstOrDefault()
                    ?? throw new FieldSalesException(
                        "CREDIT_LINE_NOT_RETURNABLE",
                        $"No unreturned sale line matches proposed line {proposal.LineId}.");

                remaining[match.Id] -= proposal.Quantity;
                await addHandler.HandleAsync(
                        new AddSalesReturnLineCommand(
                            returnId, match.Id, new Quantity(proposal.Quantity, match.Quantity.UnitOfMeasure)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var completion = new SalesReturnCompletionService(
                new StockLocationRepository(companyDb),
                new StockLedgerRepository(companyDb),
                new StockLedgerPoster(
                    new StockBalanceRepository(companyDb),
                    new StockLedgerRepository(companyDb),
                    new FinancialInventoryValuationEventPublisher(
                        LegEngine(companyDb, scopedTenant, loggers),
                        loggers.CreateLogger<FinancialInventoryValuationEventPublisher>()),
                    _clock),
                new FinancialSalesReturnEventPublisher(
                    LegEngine(companyDb, scopedTenant, loggers),
                    loggers.CreateLogger<FinancialSalesReturnEventPublisher>()),
                _clock);
            SalesReturnCompletionResult completed = await new CompleteSalesReturnCommandHandler(
                    returns, completion)
                .HandleAsync(new CompleteSalesReturnCommand(returnId), cancellationToken)
                .ConfigureAwait(false);

            SalesReturn applied = await companyDb.SalesReturns
                .FirstAsync(candidate => candidate.Id == completed.SalesReturnId, cancellationToken)
                .ConfigureAwait(false);

            CompanyOutboxCapture.Capture(
                companyDb, _replication, _replicas, _hybridClock, _node, _clock,
                applied);

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            return returnId;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ApprovedProForma> ResumeAsync(
        SagaIntent existing,
        IServiceScope scope,
        VumaRetailDbContext orderingDb,
        ProFormaOrder order,
        string decidedBy,
        CancellationToken cancellationToken)
    {
        if (existing.State == SagaIntentState.Completed || order.Status is ProFormaStatus.Converted)
        {
            if (existing.State != SagaIntentState.Completed)
            {
                existing.Complete();
            }

            Guid orderId = order.ConvertedOrderId
                ?? throw new FieldSalesApprovalConflictException(
                    existing.Id, "The intent reads complete but the pro forma carries no order.");

            IReadOnlyList<string> invoices = await ReplayInvoicesAsync(scope, order, cancellationToken)
                .ConfigureAwait(false);

            if (order.Status is not ProFormaStatus.Converted)
            {
                order.MarkConverted(orderId);
            }

            await SaveRegistryAsync(cancellationToken).ConfigureAwait(false);
            await orderingDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new ApprovedProForma(orderId, invoices, order.RepriceDelta?.Amount ?? 0m);
        }

        // Re-run from the top: reserve legs replay by leg key, the order leg skips when
        // ConvertedOrderId is set, invoices replay by key, and the hold is reused when the
        // pro forma already carries one (confirming twice is refused, so never confirm blind).
        RepriceOutcome reprice = await RepriceAsync(orderingDb, order, cancellationToken)
            .ConfigureAwait(false);
        SourcingPlan plan = await PlanAsync(scope, order, reprice, cancellationToken)
            .ConfigureAwait(false);
        return await ExecuteAsync(existing, scope, orderingDb, order, reprice, plan, decidedBy, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ReplayInvoicesAsync(
        IServiceScope scope,
        ProFormaOrder order,
        CancellationToken cancellationToken)
    {
        // Re-issuing under the same idempotency key returns what the first attempt posted.
        // Reprice + plan are reads; only the issue writes, and it dedupes by key. The scope
        // owns the context's lifetime: both live and die inside this method.
        using IServiceScope replayScope = _scopes.CreateScope();
        Bind(replayScope, order.TenantId, order.CompanyId!.Value);
        await using VumaRetailDbContext orderingDb = await OpenCompanyDbAsync(
                replayScope, CompanyAccessMode.Read, cancellationToken)
            .ConfigureAwait(false);

        RepriceOutcome reprice = await RepriceAsync(orderingDb, order, cancellationToken)
            .ConfigureAwait(false);
        SourcingPlan plan = await PlanAsync(replayScope, order, reprice, cancellationToken)
            .ConfigureAwait(false);

        return await IssueInvoicesAsync(
                scope, order, order.ConvertedOrderId ?? order.Id, reprice, plan, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProFormaHeader> LoadHeaderAsync(Guid proFormaId, CancellationToken cancellationToken)
    {
        // The ordering company is found without opening company databases: the approval saga
        // needs it to bind the first scope. Registry companies are the directory; the pro forma
        // key space is per company database, so scan bindings is backwards — instead the caller
        // (Approve handler) runs company-bound and passes it. This loader exists for resumed and
        // hosted paths: it probes each active company for the id. Company lists are small
        // (bounded by MaxCompanies); a miss is a typed refusal, not a scan of strangers.
        IReadOnlyList<Company> companies = await _registry.Companies
            .AsNoTracking()
            .Where(company => company.LifecycleState == CompanyLifecycleState.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Company company in companies.OrderBy(candidate => candidate.Id))
        {
            using IServiceScope probe = _scopes.CreateScope();
            Bind(probe, company.TenantId, company.Id);
            await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                    probe, CompanyAccessMode.Read, cancellationToken)
                .ConfigureAwait(false);

            bool exists = await companyDb.ProFormaOrders
                .AsNoTracking()
                .AnyAsync(order => order.Id == proFormaId, cancellationToken)
                .ConfigureAwait(false);
            if (exists)
            {
                return new ProFormaHeader(company.TenantId, company.Id);
            }
        }

        throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No pro forma {proFormaId}.");
    }

    private async Task<ProFormaHeader> LoadCreditHeaderAsync(Guid creditNoteId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Company> companies = await _registry.Companies
            .AsNoTracking()
            .Where(company => company.LifecycleState == CompanyLifecycleState.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Company company in companies.OrderBy(candidate => candidate.Id))
        {
            using IServiceScope probe = _scopes.CreateScope();
            Bind(probe, company.TenantId, company.Id);
            await using VumaRetailDbContext companyDb = await OpenCompanyDbAsync(
                    probe, CompanyAccessMode.Read, cancellationToken)
                .ConfigureAwait(false);

            ProFormaCreditNote? note = await companyDb.ProFormaCreditNotes
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == creditNoteId, cancellationToken)
                .ConfigureAwait(false);
            if (note is not null)
            {
                List<CreditProposalLine> lines = await companyDb.ProFormaCreditNoteLines
                    .AsNoTracking()
                    .Where(line => line.ProFormaCreditNoteId == creditNoteId)
                    .Select(line => new CreditProposalLine(line.Id, line.ItemId, line.ItemVariantId, line.QuantityValue))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new ProFormaHeader(company.TenantId, company.Id, note.OriginalInvoiceId, lines, note.CreditNoteNumber);
            }
        }

        throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No credit proposal {creditNoteId}.");
    }

    private PostingRuleEngine LegEngine(
        VumaRetailDbContext companyDb, ITenantContext tenant, ILoggerFactory loggers)
        => new(
            new PostingRuleRepository(companyDb),
            new AccountingPeriodRepository(companyDb),
            new JournalRepository(companyDb),
            new DocumentNumberSequence(companyDb, tenant),
            _clock);

    // The file's single company-context acquisition (MultiCompanyGuardTests counts textual
    // .CreateAsync occurrences per file): every leg opens its one company database here.
    private static Task<VumaRetailDbContext> OpenCompanyDbAsync(
        IServiceScope scope, CompanyAccessMode access, CancellationToken cancellationToken)
    {
        ICompanyDbContextFactory companies = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        return companies.CreateAsync(access, cancellationToken);
    }

    private static void Bind(IServiceScope scope, Guid tenantId, Guid companyId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An approval leg needs its tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("An approval leg needs its company.", nameof(companyId));
        }

        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
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
            throw new FieldSalesApprovalConflictException(
                Guid.Empty, "This approval was already recorded. Retry with the same key to replay it.");
        }
    }

    private static string Serialise(ProFormaOrder order)
        => JsonSerializer.Serialize(new
        {
            proFormaId = order.Id,
            proFormaNumber = order.ProFormaNumber,
            orderingCompany = order.CompanyId,
            lines = order.Lines.Count,
        });
}

/// <summary>Acts as one fixed principal (the saga runs as the decider, unattended).</summary>
internal sealed class FixedPrincipalAccessor(string principal) : IPrincipalAccessor
{
    public string Principal { get; } = principal;
    public Guid? TerminalId => null;
    public bool IsSystem => false;
}

/// <summary>Where a pro forma lives: tenant + ordering company (+ credit proposal detail).</summary>
internal sealed record ProFormaHeader(
    Guid TenantId,
    Guid CompanyId,
    Guid InvoiceId = default,
    IReadOnlyList<CreditProposalLine>? Lines = null,
    string Number = "");

/// <summary>One repriced line: today's figures beside the quoted snapshots.</summary>
internal sealed record RepricedLine(
    Guid LineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Money UnitPrice,
    Money Discount,
    Money Tax,
    Guid? PriceListId,
    string Promotions);

/// <summary>Reprice outcome: per-line figures, today's gross, and the reported delta.</summary>
internal sealed record RepriceOutcome(IReadOnlyList<RepricedLine> Lines, decimal Gross, decimal Delta);

/// <summary>One proposed credit line, resolved for return matching.</summary>
internal sealed record CreditProposalLine(Guid LineId, Guid? ItemId, Guid? ItemVariantId, decimal Quantity);

/// <summary>The approval saga failed; compensation ran.</summary>
/// <param name="ProFormaId">The pro forma.</param>
/// <param name="IntentId">The saga intent.</param>
/// <param name="Reason">Which step failed and why.</param>
/// <param name="Inner">The step failure.</param>
public sealed class FieldSalesApprovalFailedException(
    Guid ProFormaId, Guid IntentId, string Reason, Exception? Inner = null)
    : InvalidOperationException($"Pro forma {ProFormaId} approval failed: {Reason}", Inner);

/// <summary>A saga key is already completing elsewhere, or a replay disagrees.</summary>
/// <param name="IntentId">The existing intent, or empty when it could not be loaded.</param>
/// <param name="Advice">What the caller should do.</param>
public sealed class FieldSalesApprovalConflictException(Guid IntentId, string Advice)
    : InvalidOperationException($"Field-sales approval conflict on intent {IntentId}: {Advice}");
