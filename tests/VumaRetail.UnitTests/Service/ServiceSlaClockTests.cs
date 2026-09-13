using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Service;
using VumaRetail.Domain.Service;

namespace VumaRetail.UnitTests.Service;

public sealed class ServiceSlaClockTests
{
    private readonly BusinessHoursServiceSlaClock clock = new(new TimeOnly(9, 0), new TimeOnly(17, 0));

    [Fact]
    public void Counts_only_weekday_business_hours()
    {
        DateTimeOffset start = new(2026, 9, 14, 15, 0, 0, TimeSpan.Zero); // Monday
        DateTimeOffset end = new(2026, 9, 15, 11, 0, 0, TimeSpan.Zero);

        clock.WorkingHoursBetween(start, end).Should().Be(4m);
    }

    [Fact]
    public void Returns_zero_for_reversed_or_weekend_interval()
    {
        clock.WorkingHoursBetween(new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 13, 17, 0, 0, TimeSpan.Zero)).Should().Be(0m);
        clock.WorkingHoursBetween(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-1)).Should().Be(0m);
    }

    [Fact]
    public void Adds_working_hours_across_close_and_weekend_boundaries()
    {
        DateTimeOffset start = new(2026, 9, 18, 16, 0, 0, TimeSpan.Zero); // Friday

        clock.AddWorkingHours(start, 2m).Should().Be(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Rejects_negative_sla_duration()
    {
        var action = () => clock.AddWorkingHours(DateTimeOffset.UtcNow, -1m);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Deadline_query_uses_working_hours_and_reports_breaches()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var opened = new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero);
        var ticket = ServiceTicket.Open(tenantId, null, companyId, Guid.NewGuid(), Guid.NewGuid(), "Repair", opened);
        var sla = ServiceSla.Create(tenantId, companyId, "Standard", 4m, 12m);
        var repository = Substitute.For<IServiceRepository>();
        repository.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        repository.FindSlaByNameAsync(companyId, "Standard", Arg.Any<CancellationToken>()).Returns(sla);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        var result = await new GetServiceSlaDeadlinesQueryHandler(repository, company, clock)
            .HandleAsync(new GetServiceSlaDeadlinesQuery(companyId, ticket.Id, "Standard",
                new DateTimeOffset(2026, 9, 15, 15, 1, 0, TimeSpan.Zero)));

        result.ResponseDueAtUtc.Should().Be(new DateTimeOffset(2026, 9, 15, 11, 0, 0, TimeSpan.Zero));
        result.ResolutionDueAtUtc.Should().Be(new DateTimeOffset(2026, 9, 16, 11, 0, 0, TimeSpan.Zero));
        result.ResponseBreached.Should().BeTrue();
        result.ResolutionBreached.Should().BeFalse();
    }
}
