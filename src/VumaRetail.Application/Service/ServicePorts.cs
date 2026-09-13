#pragma warning disable CS1591
using VumaRetail.Domain.Service;

namespace VumaRetail.Application.Service;

/// <summary>Persistence boundary for service coordination records.</summary>
public interface IServiceRepository
{
    Task<IReadOnlyList<ServiceTicket>> ListTicketsAsync(Guid companyId, Guid? customerId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceCustodyEvent>> ListCustodyAsync(Guid companyId, Guid? customerId = null, CancellationToken cancellationToken = default);
    Task<ServiceTicket?> FindTicketAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServiceTicket?> FindTicketByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<WarrantyClaim?> FindWarrantyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RepairJob?> FindRepairAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServicePartUsage?> FindPartUsageByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(ServiceTicket ticket);
    void Add(WarrantyClaim claim);
    void Add(RepairJob job);
    void Add(ServicePartUsage usage);
}

/// <summary>Calculates elapsed service time using the configured working calendar.</summary>
public interface IServiceSlaClock
{
    decimal WorkingHoursBetween(DateTimeOffset startUtc, DateTimeOffset endUtc);
}

/// <summary>UTC weekday business-hours calculator used by service SLA policies.</summary>
public sealed class BusinessHoursServiceSlaClock : IServiceSlaClock
{
    private readonly TimeOnly openingTime;
    private readonly TimeOnly closingTime;

    public BusinessHoursServiceSlaClock(TimeOnly openingTime, TimeOnly closingTime)
    {
        if (closingTime <= openingTime)
        {
            throw new ArgumentException("Closing time must follow opening time.");
        }
        this.openingTime = openingTime;
        this.closingTime = closingTime;
    }

    public decimal WorkingHoursBetween(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (endUtc <= startUtc)
        {
            return 0m;
        }
        DateTimeOffset cursor = startUtc.ToUniversalTime();
        DateTimeOffset end = endUtc.ToUniversalTime();
        decimal totalHours = 0m;
        while (cursor < end)
        {
            if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                DateTimeOffset dayOpen = new(cursor.Date.Add(openingTime.ToTimeSpan()), TimeSpan.Zero);
                DateTimeOffset dayClose = new(cursor.Date.Add(closingTime.ToTimeSpan()), TimeSpan.Zero);
                DateTimeOffset from = cursor > dayOpen ? cursor : dayOpen;
                DateTimeOffset to = end < dayClose ? end : dayClose;
                if (to > from)
                {
                    totalHours += (decimal)(to - from).TotalHours;
                }
            }
            cursor = new DateTimeOffset(cursor.Date.AddDays(1), TimeSpan.Zero);
        }
        return totalHours;
    }
}
