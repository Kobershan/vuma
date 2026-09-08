using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Sales;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry.Trading;
using VumaRetail.Domain.Sales;
using VumaRetail.Finance.Posting;
using VumaRetail.Infrastructure.Inventory;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Splits a mixed-basket return by origin company (ADR-128).</summary>
public sealed class MixedBasketReturnService(
    ITradingSessionRepository sessions,
    ITradingCompanyGateway gateway,
    IServiceScopeFactory scopes,
    IReplicationRegistry replication,
    IReplicaWriter replicas,
    IHybridClock hybridClock,
    INodeIdentity node,
    IClock clock,
    ILogger<MixedBasketReturnService> logger) : IMixedBasketReturnService
{
    /// <inheritdoc />
    public async Task<Guid> ReturnLinesAsync(
        Guid sessionId,
        Guid companyId,
        IReadOnlyList<ReturnLineRequest> lines,
        string invoiceNumber,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (sessionId == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("A return names its session and its origin company.");
        }

        TradingSession session = await sessions.FindAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {sessionId}.");

        if (session.Status is not TradingSessionStatus.Completed)
        {
            throw TradingSessionException.IllegalTransition(session.Status, "be returned from");
        }

        TradingSessionSegment segment = session.Segments
            .FirstOrDefault(s => s.CompanyId == companyId && !s.IsRemoved)
            ?? throw new TradingSessionException(
                "TRADING_SEGMENT_NOT_FOUND", $"Company {companyId} has no segment on session {session.SessionNumber}.");

        // There is no cross-company credit note, in any circumstance: the named invoice must
        // be this company's own, and the refusal names both numbers for the cashier.
        if (!string.Equals(segment.ResultingInvoiceNumber, invoiceNumber.Trim(), StringComparison.Ordinal))
        {
            string own = segment.ResultingInvoiceNumber ?? "(none)";
            throw TradingSessionException.ReturnWrongCompany(own, invoiceNumber.Trim());
        }

        if (segment.ResultingSaleId is not { } saleId)
        {
            throw new TradingSessionException(
                "TRADING_SALE_MISSING", $"Company {companyId}'s segment carries no posted sale.");
        }

        using IServiceScope scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(session.TenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);

        await using VumaRetailDbContext companyDb = await scope.ServiceProvider
            .GetRequiredService<ICompanyDbContextFactory>()
            .CreateAsync(CompanyAccessMode.Write, cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        AuditStamper audit = scope.ServiceProvider.GetRequiredService<AuditStamper>();
        ILoggerFactory loggers = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        Sale sale = await companyDb.Sales
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SALE_MISSING", $"Company {companyId} has no sale {saleId}.");

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var numbers = new DocumentNumberSequence(companyDb, scopedTenant);
            string returnNumber = await numbers.NextAsync("RTN", cancellationToken).ConfigureAwait(false);

            SalesReturn salesReturn = SalesReturn.Raise(
                sale,
                returnNumber,
                reason.Trim(),
                MapTender(session.TenderType),
                session.CashierUserId,
                clock.UtcNow);

            foreach (ReturnLineRequest requested in lines)
            {
                // Till line ids are deterministic in the session line (TradingLegIds), so the
                // return names the exact till line it credits — no description matching.
                Guid soldLineId = TradingLegIds.SaleLineId(session.Id, companyId, requested.SessionLineId);
                SaleLine soldLine = sale.LiveLines.FirstOrDefault(line => line.Id == soldLineId)
                    ?? throw new TradingSessionException(
                        "TRADING_LINE_NOT_FOUND",
                        $"No live sale line for session line {requested.SessionLineId}.");

                salesReturn.AddLine(
                    soldLine,
                    new Quantity(requested.QuantityValue, soldLine.Quantity.UnitOfMeasure),
                    previouslyReturned: 0m);
            }

            salesReturn.Complete(clock.UtcNow);
            salesReturn.AssignCompany(companyId);

            PostingRuleEngine engine = new(
                new PostingRuleRepository(companyDb),
                new AccountingPeriodRepository(companyDb),
                new JournalRepository(companyDb),
                new DocumentNumberSequence(companyDb, scopedTenant),
                clock);

            await new FinancialSalesReturnEventPublisher(engine, loggers.CreateLogger<FinancialSalesReturnEventPublisher>())
                .PublishAsync(
                    new SalesReturnCompletedEvent(
                        session.TenantId, scopedTenant.StoreId, salesReturn.Id, returnNumber,
                        sale.Id, salesReturn.Net, salesReturn.Tax, salesReturn.Gross, clock.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            StockLocation? location = await new StockLocationRepository(companyDb)
                .FindAsync(sale.LocationId, cancellationToken)
                .ConfigureAwait(false);
            if (location is not null)
            {
                StockLedgerPoster stock = new(
                    new StockBalanceRepository(companyDb),
                    new StockLedgerRepository(companyDb),
                    new FinancialInventoryValuationEventPublisher(
                        engine, loggers.CreateLogger<FinancialInventoryValuationEventPublisher>()),
                    clock);

                foreach (SalesReturnLine returnLine in salesReturn.Lines)
                {
                    SaleLine soldLine = sale.RequireLine(returnLine.SaleLineId);
                    Money unitCost = soldLine.StockLedgerEntryId is { } entryId
                        && await companyDb.StockLedgerEntries
                            .AsNoTracking()
                            .FirstOrDefaultAsync(entry => entry.Id == entryId, cancellationToken)
                            .ConfigureAwait(false) is { } entry
                        // A refused line has no ledger entry: fall back to the rung price so the
                        // return still restores the shelf count. Valuation on this path is
                        // approximate by construction (ADR-145).
                        ? entry.UnitCost
                        : soldLine.UnitPrice;

                    await stock.ReceiveForSalesReturnAsync(
                            location,
                            soldLine.ItemId,
                            soldLine.ItemVariantId,
                            new Quantity(returnLine.Quantity.Value, returnLine.Quantity.UnitOfMeasure),
                            unitCost,
                            salesReturn.Id,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            companyDb.SalesReturns.Add(salesReturn);
            foreach (SalesReturnLine returnLine in salesReturn.Lines)
            {
                returnLine.AssignCompany(companyId);
                companyDb.SalesReturnLines.Add(returnLine);
            }

            CompanyOutboxCapture.Capture(
                companyDb, replication, replicas, hybridClock, node, clock, salesReturn);

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            logger.LogDebug(
                "Trading session {SessionNumber} returned {Count} line(s) in company {CompanyId} as {ReturnNumber}.",
                session.SessionNumber, lines.Count, companyId, returnNumber);

            return salesReturn.Id;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static TenderType MapTender(string? tenderType)
        => Enum.TryParse<TenderType>(tenderType, ignoreCase: true, out TenderType parsed)
            ? parsed
            : TenderType.Cash;
}
