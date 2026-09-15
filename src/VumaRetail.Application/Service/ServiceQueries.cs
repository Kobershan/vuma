#pragma warning disable CS1591, IDE0011
using System.Globalization;
using System.Text;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Service;

namespace VumaRetail.Application.Service;

public sealed record ListServiceTicketsQuery(Guid CompanyId, Guid? CustomerId = null)
    : IQuery<IReadOnlyList<ServiceTicketResult>>;

public sealed record ServiceTicketResult(Guid Id, Guid CompanyId, Guid CustomerId, string Subject,
    string Status, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc);

public sealed record GetServiceSlaDeadlinesQuery(Guid CompanyId, Guid TicketId, string SlaName, DateTimeOffset AsOfUtc)
    : IQuery<ServiceSlaDeadlineResult>;

public sealed record ServiceSlaDeadlineResult(Guid TicketId, string SlaName, DateTimeOffset ResponseDueAtUtc,
    DateTimeOffset ResolutionDueAtUtc, bool ResponseBreached, bool ResolutionBreached);

public sealed record ListServiceSlaBreachesQuery(Guid CompanyId, Guid? TicketId = null)
    : IQuery<IReadOnlyList<ServiceSlaBreachResult>>;

public sealed record ServiceSlaBreachResult(Guid Id, Guid TicketId, string SlaName, string BreachType,
    DateTimeOffset DueAtUtc, DateTimeOffset ObservedAtUtc);

public sealed class ListServiceSlaBreachesQueryHandler(IServiceRepository services, ICompanyContext company,
    ITenantContext tenant) : IQueryHandler<ListServiceSlaBreachesQuery, IReadOnlyList<ServiceSlaBreachResult>>
{
    public async Task<IReadOnlyList<ServiceSlaBreachResult>> HandleAsync(ListServiceSlaBreachesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ListServiceTicketsQueryHandler.EnsureCompany(company, query.CompanyId);
        return (await services.ListSlaBreachesAsync(query.CompanyId, query.TicketId, cancellationToken)
                .ConfigureAwait(false))
            .Where(x => x.TenantId == tenant.TenantId && x.CompanyId == query.CompanyId)
            .Select(x => new ServiceSlaBreachResult(x.Id, x.TicketId, x.SlaName, x.BreachType.ToString(),
                x.DueAtUtc, x.ObservedAtUtc)).ToArray();
    }
}

public sealed class GetServiceSlaDeadlinesQueryHandler(IServiceRepository services, ITenantContext tenant, ICompanyContext company,
    IServiceSlaClock slaClock) : IQueryHandler<GetServiceSlaDeadlinesQuery, ServiceSlaDeadlineResult>
{
    public async Task<ServiceSlaDeadlineResult> HandleAsync(GetServiceSlaDeadlinesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ListServiceTicketsQueryHandler.EnsureCompany(company, query.CompanyId);
        ServiceTicket ticket = await services.FindTicketAsync(query.TicketId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Service ticket was not found.");
        if (ticket.TenantId != tenant.TenantId)
            throw new KeyNotFoundException("Service ticket was not found.");
        ListServiceTicketsQueryHandler.EnsureCompany(company, ticket.CompanyId!.Value);
        ServiceSla sla = await services.FindSlaByNameAsync(query.CompanyId, query.SlaName, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Service SLA was not found.");
        DateTimeOffset asOf = query.AsOfUtc.ToUniversalTime();
        decimal paused = ticket.CustomerWaitWorkingHours;
        if (ticket.CustomerWaitStartedAtUtc is { } waitStarted && asOf > waitStarted)
            paused += slaClock.WorkingHoursBetween(waitStarted, asOf);
        DateTimeOffset responseDue = slaClock.AddWorkingHours(ticket.OpenedAtUtc, sla.ResponseHours + paused);
        DateTimeOffset resolutionDue = slaClock.AddWorkingHours(ticket.OpenedAtUtc, sla.ResolutionHours + paused);
        return new ServiceSlaDeadlineResult(ticket.Id, sla.Name, responseDue, resolutionDue,
            ticket.Status == ServiceTicketStatus.Open && asOf > responseDue,
            ticket.Status != ServiceTicketStatus.Closed && asOf > resolutionDue);
    }
}

public sealed class ListServiceTicketsQueryHandler(IServiceRepository services, ICompanyContext company)
    : IQueryHandler<ListServiceTicketsQuery, IReadOnlyList<ServiceTicketResult>>
{
    public async Task<IReadOnlyList<ServiceTicketResult>> HandleAsync(ListServiceTicketsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureCompany(company, query.CompanyId);
        IReadOnlyList<ServiceTicket> tickets = await services.ListTicketsAsync(query.CompanyId, query.CustomerId, cancellationToken).ConfigureAwait(false);
        return tickets.Select(x => new ServiceTicketResult(x.Id, x.CompanyId!.Value, x.CustomerId, x.Subject,
            x.Status.ToString(), x.OpenedAtUtc, x.ClosedAtUtc)).ToArray();
    }

    internal static void EnsureCompany(ICompanyContext company, Guid expected)
    {
        if (company.CompanyId is not { } active || active != expected)
            throw new InvalidOperationException("The service company is not the active company.");
    }
}

public sealed record ListServiceCustodyQuery(Guid CompanyId, Guid? CustomerId = null)
    : IQuery<IReadOnlyList<ServiceCustodyResult>>;

public sealed record ServiceCustodyResult(Guid Id, Guid CompanyId, Guid TicketId, Guid CustomerId,
    string EventType, string ItemReference, DateTimeOffset OccurredAtUtc);

public static class ServiceCustodyCsv
{
    public static string Serialize(IReadOnlyCollection<ServiceCustodyResult> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        static string Escape(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        StringBuilder csv = new("id,company_id,ticket_id,customer_id,event_type,item_reference,occurred_at_utc\n");
        foreach (ServiceCustodyResult row in rows)
        {
            csv.Append(row.Id.ToString("D")).Append(',')
                .Append(row.CompanyId.ToString("D")).Append(',')
                .Append(row.TicketId.ToString("D")).Append(',')
                .Append(row.CustomerId.ToString("D")).Append(',')
                .Append(Escape(row.EventType)).Append(',')
                .Append(Escape(row.ItemReference)).Append(',')
                .Append(row.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        }
        return csv.ToString();
    }
}

public sealed class ListServiceCustodyQueryHandler(IServiceRepository services, ICompanyContext company)
    : IQueryHandler<ListServiceCustodyQuery, IReadOnlyList<ServiceCustodyResult>>
{
    public async Task<IReadOnlyList<ServiceCustodyResult>> HandleAsync(ListServiceCustodyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ListServiceTicketsQueryHandler.EnsureCompany(company, query.CompanyId);
        IReadOnlyList<ServiceCustodyEvent> events = await services.ListCustodyAsync(query.CompanyId, query.CustomerId, cancellationToken).ConfigureAwait(false);
        return events.Select(x => new ServiceCustodyResult(x.Id, x.CompanyId!.Value, x.TicketId, x.CustomerId,
            x.EventType, x.ItemReference, x.OccurredAtUtc)).ToArray();
    }
}
