#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using VumaRetail.Domain.Sales.Analytics;

namespace VumaRetail.Application.Hr;

public sealed record GetWorkforceLabourCostQuery(Guid CompanyId, DateOnly From, DateOnly To)
    : IQuery<IReadOnlyList<WorkforceLabourCostResult>>;

public sealed record WorkforceLabourCostResult(string Currency, decimal Hours, decimal LabourCost,
    decimal SalesRevenue, decimal LabourCostPercentageOfSales);

/// <summary>Compares worked attendance cost with the company sales read model.</summary>
public sealed class GetWorkforceLabourCostQueryHandler(
    IEmployeeRepository employees,
    IEmploymentContractRepository contracts,
    IAttendanceRepository attendance,
    ISalesAnalyticsRepository sales,
    ICompanyContext company,
    ITenantContext tenant)
    : IQueryHandler<GetWorkforceLabourCostQuery, IReadOnlyList<WorkforceLabourCostResult>>
{
    public async Task<IReadOnlyList<WorkforceLabourCostResult>> HandleAsync(
        GetWorkforceLabourCostQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.To < query.From)
        {
            throw new ArgumentException("Labour-cost period cannot end before it starts.", nameof(query));
        }
        if (company.CompanyId is not { } active || active != query.CompanyId)
        {
            throw new InvalidOperationException("The workforce company is not the active company.");
        }

        DateTimeOffset from = new(query.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset to = new(query.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        IReadOnlyList<AttendanceRecord> events = await attendance.ListAsync(from, to, null, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, (decimal Hours, decimal Cost)> labour = new(StringComparer.OrdinalIgnoreCase);
        foreach (Employee employee in await employees.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (employee.TenantId != tenant.TenantId || employee.CompanyId != query.CompanyId)
            {
                continue;
            }
            AttendanceRecord[] employeeEvents = events.Where(x => x.EmployeeId == employee.Id)
                .OrderBy(x => x.OccurredAt).ToArray();
            decimal hours = PayrollHoursCalculator.Calculate(employeeEvents);
            if (hours == 0m)
            {
                continue;
            }
            EmploymentContract? contract = (await contracts.ListAsync(employee.Id, cancellationToken)
                    .ConfigureAwait(false))
                .Where(x => x.TenantId == tenant.TenantId && x.StartsOn <= query.To
                    && (x.EndsOn is null || x.EndsOn >= query.From))
                .OrderByDescending(x => x.StartsOn).FirstOrDefault();
            if (contract is null)
            {
                throw new InvalidOperationException($"Employee {employee.EmployeeNumber} has no contract for the labour-cost period.");
            }
            (decimal existingHours, decimal existingCost) = labour.TryGetValue(contract.Currency, out var current)
                ? current : (0m, 0m);
            labour[contract.Currency] = (existingHours + hours,
                existingCost + decimal.Round(hours * contract.HourlyRate, 2, MidpointRounding.AwayFromZero));
        }

        IReadOnlyList<SalesAnalytics> salesRows = await sales.GetByCompanyAsync(query.CompanyId,
            AnalyticsPeriod.Daily, from, to, cancellationToken).ConfigureAwait(false);
        Dictionary<string, decimal> revenue = salesRows
            .Where(x => x.TenantId == tenant.TenantId && x.CompanyId == query.CompanyId && x.CategoryCode is null)
            .GroupBy(x => x.Currency, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Revenue.Amount), StringComparer.OrdinalIgnoreCase);
        return labour.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new WorkforceLabourCostResult(x.Key, x.Value.Hours, x.Value.Cost,
                revenue.GetValueOrDefault(x.Key), revenue.GetValueOrDefault(x.Key) == 0m
                    ? 0m
                    : decimal.Round(x.Value.Cost / revenue.GetValueOrDefault(x.Key) * 100m, 2,
                        MidpointRounding.AwayFromZero)))
            .ToArray();
    }
}
