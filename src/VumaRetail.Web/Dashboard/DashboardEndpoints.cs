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
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        string timeZoneId = await db.Tenants
            .Where(tenant => tenant.Id == tenantContext.TenantId)
            .Select(tenant => tenant.TimeZone)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "UTC";
        TimeZoneInfo timeZone = ResolveTimeZone(timeZoneId);
        DateTime localDate = TimeZoneInfo.ConvertTime(now, timeZone).Date;
        DateTimeOffset start = new(TimeZoneInfo.ConvertTimeToUtc(localDate, timeZone), TimeSpan.Zero);
        DateTimeOffset tomorrow = start.AddDays(1);
        IQueryable<SalesOrder> orders = db.SalesOrders
            .AsNoTracking()
            .Where(order => order.Status != SalesOrderStatus.Cancelled);
        IQueryable<SalesOrder> recognized = orders
            .Where(order => order.Status == SalesOrderStatus.Fulfilled
                || order.Status == SalesOrderStatus.PartiallyFulfilled);

        DashboardOrder[] recent = await orders
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

        List<DashboardMoney> salesByCurrency = await recognized
            .Where(order => order.OrderDate >= start && order.OrderDate < tomorrow)
            .GroupBy(order => order.Currency)
            .Select(group => new DashboardMoney(group.Key, group.Sum(order => order.Gross.Amount)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int ordersToday = await recognized
            .CountAsync(order => order.OrderDate >= start && order.OrderDate < tomorrow, cancellationToken)
            .ConfigureAwait(false);

        int openOrders = await orders
            .CountAsync(order => order.Status != SalesOrderStatus.Fulfilled && order.Status != SalesOrderStatus.Cancelled, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new DashboardOverview(
            salesByCurrency.Count == 1 ? salesByCurrency[0].Amount : 0m,
            salesByCurrency,
            ordersToday,
            openOrders,
            recent,
            now));
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException) when (OperatingSystem.IsWindows()
            && string.Equals(timeZoneId, "Africa/Johannesburg", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
        }
    }
}

/// <summary>Tenant-scoped facts displayed by the operations dashboard.</summary>
public sealed record DashboardOverview(
    decimal SalesToday,
    IReadOnlyList<DashboardMoney> SalesByCurrency,
    int OrdersToday,
    int OpenOrders,
    IReadOnlyList<DashboardOrder> RecentOrders,
    DateTimeOffset AsAt);

/// <summary>One currency's recognized sales total.</summary>
public sealed record DashboardMoney(string Currency, decimal Amount);

/// <summary>One recent tenant order.</summary>
public sealed record DashboardOrder(
    string OrderNumber,
    string Currency,
    decimal Gross,
    string Status,
    DateTimeOffset OrderDate);
