using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Orders.Permissions;
using VumaRetail.Domain.Orders;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Dashboard;

/// <summary>Read-only, tenant-scoped dashboard facts for the back-office and mobile clients.</summary>
public static class DashboardEndpoints
{
    /// <summary>Maps the dashboard overview query.</summary>
    public static IEndpointRouteBuilder MapVumaDashboard(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapVumaApi().MapGet("/dashboard/overview", GetOverviewAsync)
            .RequirePermission(OrdersPermissions.View)
            .Produces<DashboardOverview>()
            .WithSummary("Current order and sales facts for the operations dashboard.");
        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        VumaRetailDbContext db,
        IClock clock,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset start = new(now.UtcDateTime.Date, TimeSpan.Zero);
        DateTimeOffset tomorrow = start.AddDays(1);
        IQueryable<SalesOrder> active = db.SalesOrders
            .AsNoTracking()
            .Where(order => order.Status != SalesOrderStatus.Cancelled);

        DashboardOrder[] recent = await active
            .OrderByDescending(order => order.OrderDate)
            .Take(4)
            .Select(order => new DashboardOrder(
                order.OrderNumber,
                order.Currency,
                order.Gross.Amount,
                order.Status.ToString(),
                order.OrderDate))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        decimal salesToday = await active
            .Where(order => order.OrderDate >= start && order.OrderDate < tomorrow)
            .Select(order => order.Gross.Amount)
            .SumAsync(cancellationToken)
            .ConfigureAwait(false);

        int ordersToday = await active
            .CountAsync(order => order.OrderDate >= start && order.OrderDate < tomorrow, cancellationToken)
            .ConfigureAwait(false);

        int openOrders = await active
            .CountAsync(order => order.Status != SalesOrderStatus.Fulfilled && order.Status != SalesOrderStatus.Cancelled, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new DashboardOverview(salesToday, ordersToday, openOrders, recent, now));
    }
}

/// <summary>Tenant-scoped facts displayed by the operations dashboard.</summary>
public sealed record DashboardOverview(
    decimal SalesToday,
    int OrdersToday,
    int OpenOrders,
    IReadOnlyList<DashboardOrder> RecentOrders,
    DateTimeOffset AsAt);

/// <summary>One recent tenant order.</summary>
public sealed record DashboardOrder(
    string OrderNumber,
    string Currency,
    decimal Gross,
    string Status,
    DateTimeOffset OrderDate);
