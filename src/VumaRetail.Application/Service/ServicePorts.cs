#pragma warning disable CS1591
using VumaRetail.Domain.Service;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;

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
    Task<ServiceSla?> FindSlaByNameAsync(Guid companyId, string name, CancellationToken cancellationToken = default);
    void Add(ServiceTicket ticket);
    void Add(WarrantyClaim claim);
    void Add(RepairJob job);
    void Add(ServicePartUsage usage);
    void Add(ServiceSla sla);
}

/// <summary>Calculates elapsed service time using the configured working calendar.</summary>
public interface IServiceSlaClock
{
    decimal WorkingHoursBetween(DateTimeOffset startUtc, DateTimeOffset endUtc);
    DateTimeOffset AddWorkingHours(DateTimeOffset startUtc, decimal workingHours);
}

/// <summary>Runs a bounded, company-scoped SLA breach pass for operator or hosted scheduling.</summary>
public interface IServiceSlaWorker
{
    Task<IReadOnlyList<ServiceSlaBreach>> EvaluateAsync(Guid companyId, string slaName,
        DateTimeOffset asOfUtc, CancellationToken cancellationToken = default);
}

public sealed record ServiceSlaBreach(Guid TicketId, bool ResponseBreached, bool ResolutionBreached,
    DateTimeOffset ResponseDueAtUtc, DateTimeOffset ResolutionDueAtUtc);

public sealed class ServiceSlaWorker(IServiceRepository services, ICompanyContext company, ITenantContext tenant,
    IServiceSlaClock clock) : IServiceSlaWorker
{
    public async Task<IReadOnlyList<ServiceSlaBreach>> EvaluateAsync(Guid companyId, string slaName,
        DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
    {
        if (company.CompanyId is not { } active || active != companyId)
        {
            throw new InvalidOperationException("The service company is not the active company.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(slaName);
        ServiceSla sla = await services.FindSlaByNameAsync(companyId, slaName, cancellationToken)
            .ConfigureAwait(false) ?? throw new KeyNotFoundException("Service SLA was not found.");
        DateTimeOffset asOf = asOfUtc.ToUniversalTime();
        IReadOnlyList<ServiceTicket> tickets = await services.ListTicketsAsync(companyId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        List<ServiceSlaBreach> breaches = [];
        foreach (ServiceTicket ticket in tickets)
        {
            if (ticket.TenantId != tenant.TenantId || ticket.CompanyId != companyId || ticket.Status == ServiceTicketStatus.Closed)
            {
                continue;
            }
            decimal paused = ticket.CustomerWaitWorkingHours;
            if (ticket.CustomerWaitStartedAtUtc is { } waitStarted && asOf > waitStarted)
            {
                paused += clock.WorkingHoursBetween(waitStarted, asOf);
            }
            DateTimeOffset responseDue = clock.AddWorkingHours(ticket.OpenedAtUtc, sla.ResponseHours + paused);
            DateTimeOffset resolutionDue = clock.AddWorkingHours(ticket.OpenedAtUtc, sla.ResolutionHours + paused);
            bool responseBreached = ticket.Status == ServiceTicketStatus.Open && asOf > responseDue;
            bool resolutionBreached = asOf > resolutionDue;
            if (responseBreached || resolutionBreached)
            {
                breaches.Add(new(ticket.Id, responseBreached, resolutionBreached, responseDue, resolutionDue));
            }
        }
        return breaches;
    }
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

    public DateTimeOffset AddWorkingHours(DateTimeOffset startUtc, decimal workingHours)
    {
        if (workingHours < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(workingHours));
        }
        if (workingHours == 0m)
        {
            return startUtc.ToUniversalTime();
        }

        DateTimeOffset cursor = startUtc.ToUniversalTime();
        decimal remaining = workingHours;
        while (true)
        {
            if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                DateTimeOffset dayOpen = new(cursor.Date.Add(openingTime.ToTimeSpan()), TimeSpan.Zero);
                DateTimeOffset dayClose = new(cursor.Date.Add(closingTime.ToTimeSpan()), TimeSpan.Zero);
                DateTimeOffset from = cursor < dayOpen ? dayOpen : cursor;
                if (from < dayClose)
                {
                    decimal available = (decimal)(dayClose - from).TotalHours;
                    if (remaining <= available)
                    {
                        return from.AddHours((double)remaining);
                    }
                    remaining -= available;
                }
            }
            cursor = new DateTimeOffset(cursor.Date.AddDays(1), TimeSpan.Zero);
        }
    }
}
