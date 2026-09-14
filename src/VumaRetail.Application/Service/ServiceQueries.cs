#pragma warning disable CS1591, IDE0011
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
