using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Orders;

/// <summary>
/// The one place Stage 14 touches Stage 08c's reservation ledger (ADR-103): hold on allocation,
/// release on cancel, consume on fulfilment. Handlers call these; the ledger owns the figures and
/// this stage keeps only the hold pointer (<see cref="SalesOrderLine.ReservationId"/>).
/// </summary>
/// <remarks>
/// Ledger writes run in a company-bound child scope (the gateway shape): the order pipeline's
/// ambient scope stays unbound, so binding a company here never narrows what the pipeline's own
/// repositories can see. The company resolves as the command's, then the order's, then the
/// tenant's single active company — several active companies is a refusal, never a pick-first.
/// </remarks>
internal static class OrderReservationMaintenance
{
    /// <summary>
    /// Holds up to <paramref name="quantity"/> for one line, replacing any live hold first so a
    /// re-attempt never orphans the earlier hold. Attaches the new hold when anything was held.
    /// </summary>
    /// <param name="scopes">Opens the company-bound scope the hold is written in.</param>
    /// <param name="directory">Resolves the acting company.</param>
    /// <param name="companyId">The fulfilling company, when the caller named one.</param>
    /// <param name="order">The order, for the hold's document reference.</param>
    /// <param name="line">The line to hold for.</param>
    /// <param name="locationId">Where the stock is held.</param>
    /// <param name="quantity">How much to hold. Zero or negative holds nothing.</param>
    /// <param name="reason">Why, for the ledger row.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How much the ledger actually held (shortfall stays unheld, never negative).</returns>
    internal static async Task<Quantity> ReholdAsync(
        IServiceScopeFactory scopes,
        ICompanyDirectory directory,
        Guid? companyId,
        SalesOrder order,
        SalesOrderLine line,
        Guid locationId,
        Quantity quantity,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(line);

        Guid company = await ResolveCompanyAsync(companyId, order, directory, cancellationToken)
            .ConfigureAwait(false);

        return await RunInCompanyScopeAsync(scopes, order.TenantId, company,
                (reservations, token) => ReholdInScopeAsync(
                    reservations, order, line, locationId, quantity, reason, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Releases a line's live hold, if any, and detaches the pointer.</summary>
    internal static async Task ReleaseAsync(
        IServiceScopeFactory scopes,
        ICompanyDirectory directory,
        Guid? companyId,
        SalesOrder order,
        SalesOrderLine line,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(line);

        if (!line.ReservationId.HasValue)
        {
            return;
        }

        Guid company = await ResolveCompanyAsync(companyId, order, directory, cancellationToken)
            .ConfigureAwait(false);
        Guid reservationId = line.ReservationId.Value;

        await RunInCompanyScopeAsync(scopes, order.TenantId, company,
                (reservations, token) => reservations.ReleaseAsync(reservationId, reason, token),
                cancellationToken)
            .ConfigureAwait(false);
        line.DetachReservation();
    }

    /// <summary>
    /// Consumes the holds of lines that became <c>Fulfilled</c> since <paramref name="fulfilledBefore"/>
    /// was captured — the stock left the building, so the hold becomes a consumption, not a release.
    /// </summary>
    internal static async Task ConsumeNewlyFulfilledAsync(
        IServiceScopeFactory scopes,
        ICompanyDirectory directory,
        Guid? companyId,
        SalesOrder order,
        IReadOnlySet<Guid> fulfilledBefore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(fulfilledBefore);

        List<Guid> newlyFulfilled = [.. order.Lines
            .Where(line => line.LineStatus == SalesOrderLineStatus.Fulfilled
                && !fulfilledBefore.Contains(line.Id)
                && line.ReservationId.HasValue)
            .Select(line => line.Id)];

        if (newlyFulfilled.Count == 0)
        {
            return;
        }

        Guid company = await ResolveCompanyAsync(companyId, order, directory, cancellationToken)
            .ConfigureAwait(false);

        await RunInCompanyScopeAsync(scopes, order.TenantId, company,
                async (reservations, token) =>
                {
                    foreach (Guid lineId in newlyFulfilled)
                    {
                        SalesOrderLine line = order.RequireLine(lineId);
                        if (!line.ReservationId.HasValue)
                        {
                            continue;
                        }

                        await reservations
                            .ConsumeAsync(line.ReservationId.Value, order.Id, token)
                            .ConfigureAwait(false);
                        line.DetachReservation();
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The acting company, in order: the command's explicit company, the order's assigned company,
    /// the tenant's single active company. Anything vaguer throws rather than guesses.
    /// </summary>
    internal static async Task<Guid> ResolveCompanyAsync(
        Guid? commandCompanyId,
        SalesOrder order,
        ICompanyDirectory directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(directory);

        if (commandCompanyId.HasValue && commandCompanyId.Value != Guid.Empty)
        {
            return commandCompanyId.Value;
        }

        if (order.CompanyId.HasValue && order.CompanyId.Value != Guid.Empty)
        {
            return order.CompanyId.Value;
        }

        return await directory
            .RequireSingleActiveAsync(order.TenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Quantity> ReholdInScopeAsync(
        IReservationService reservations,
        SalesOrder order,
        SalesOrderLine line,
        Guid locationId,
        Quantity quantity,
        string reason,
        CancellationToken cancellationToken)
    {
        if (line.ReservationId.HasValue)
        {
            await reservations
                .ReleaseAsync(line.ReservationId.Value, reason, cancellationToken)
                .ConfigureAwait(false);
            line.DetachReservation();
        }

        if (quantity.IsNegative || quantity.IsZero)
        {
            return Quantity.Zero(quantity.UnitOfMeasure);
        }

        ReserveOutcome outcome = await reservations.ReserveAsync(
            locationId,
            line.ItemId,
            line.ItemVariantId,
            quantity,
            ReservationSource.Order,
            order.Id,
            order.OrderNumber,
            reason: reason,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (outcome.ReservationId.HasValue && !outcome.Held.IsZero)
        {
            line.AttachReservation(outcome.ReservationId.Value);
        }

        return outcome.Held;
    }

    private static async Task RunInCompanyScopeAsync(
        IServiceScopeFactory scopes,
        Guid tenantId,
        Guid companyId,
        Func<IReservationService, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
        IReservationService reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();
        await action(reservations, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Quantity> RunInCompanyScopeAsync(
        IServiceScopeFactory scopes,
        Guid tenantId,
        Guid companyId,
        Func<IReservationService, CancellationToken, Task<Quantity>> action,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
        IReservationService reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();
        return await action(reservations, cancellationToken).ConfigureAwait(false);
    }
}
