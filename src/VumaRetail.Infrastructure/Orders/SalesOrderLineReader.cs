#pragma warning disable CS1591
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Warehouse;
using VumaRetail.Domain.Orders;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Orders;

/// <summary>Reads active Stage 14 demand for Stage 13b consolidated-wave building.</summary>
public sealed class SalesOrderLineReader(VumaRetailDbContext context) : IOrderLineReader
{
    public async Task<IReadOnlyList<OrderLineSummary>> ReadOpenLinesAsync(
        Guid locationId,
        DateOnly periodFrom,
        DateOnly periodTo,
        string geographyLevel,
        string geographyValue,
        Guid? companyScopeId,
        CancellationToken cancellationToken = default)
    {
        List<SalesOrder> orders = await context.SalesOrders
            .Include(order => order.Lines)
            .AsNoTracking()
            .Where(order => order.FulfillingLocationId == locationId
                && order.OrderDate >= periodFrom.ToDateTime(TimeOnly.MinValue)
                && order.OrderDate < periodTo.AddDays(1).ToDateTime(TimeOnly.MinValue)
                && order.Status != SalesOrderStatus.Draft
                && order.Status != SalesOrderStatus.Fulfilled
                && order.Status != SalesOrderStatus.Cancelled
                && order.Status != SalesOrderStatus.Closed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return orders
            .Where(order => (companyScopeId is null || order.TenantId == companyScopeId.Value)
                && MatchesGeography(order.DeliveryGeography, geographyLevel, geographyValue))
            .SelectMany(order => order.Lines
                .Where(line => line.LineStatus is not (SalesOrderLineStatus.Fulfilled or SalesOrderLineStatus.Cancelled))
                .Select(line => new OrderLineSummary(
                    order.Id,
                    line.Id,
                    line.ItemId,
                    line.ItemVariantId,
                    line.BackorderedQuantity.IsZero ? line.RequestedQuantity.Value : line.BackorderedQuantity.Value,
                    line.RequestedQuantity.UnitOfMeasure,
                    "Each",
                    order.DeliveryGeography?.City ?? string.Empty)))
            .Where(line => line.ItemId is not null || line.ItemVariantId is not null)
            .ToList();
    }

    private static bool MatchesGeography(DeliveryGeography? geography, string level, string value)
    {
        if (geography is null) return false;
        string actual = level switch
        {
            "Province" => geography.Province,
            "City" => geography.City,
            "Suburb" => geography.Suburb,
            _ => string.Empty
        };
        return string.Equals(actual.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
