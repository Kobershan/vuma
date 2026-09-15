using FluentAssertions;
using VumaRetail.Application.Service;

namespace VumaRetail.UnitTests.Service;

public sealed class ServiceCustodyExportTests
{
    [Fact]
    public void Csv_export_is_stable_and_escapes_custody_text()
    {
        Guid id = Guid.NewGuid();
        Guid company = Guid.NewGuid();
        Guid ticket = Guid.NewGuid();
        Guid customer = Guid.NewGuid();
        string csv = ServiceCustodyCsv.Serialize([
            new ServiceCustodyResult(id, company, ticket, customer, "received", "Laptop, \"blue\"",
                new DateTimeOffset(2026, 9, 15, 12, 30, 0, TimeSpan.Zero))]);

        csv.Should().Be($"id,company_id,ticket_id,customer_id,event_type,item_reference,occurred_at_utc\n" +
            $"{id:D},{company:D},{ticket:D},{customer:D},\"received\",\"Laptop, \"\"blue\"\"\",2026-09-15T12:30:00.0000000+00:00\n");
    }
}
