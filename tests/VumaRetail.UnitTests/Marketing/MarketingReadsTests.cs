using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.UnitTests.Marketing;

public sealed class MarketingReadsTests
{
    [Fact]
    public async Task Deliveries_return_sent_and_failed_with_company_scope()
    {
        Guid companyId = Guid.NewGuid();
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var messages = Substitute.For<IOutboundMessageRepository>();
        OutboundMessage sent = WellFormedMessage(companyId);
        messages.ListByStatusAsync(companyId,
                Arg.Is<IReadOnlyCollection<OutboundMessageStatus>>(s =>
                    s.Contains(OutboundMessageStatus.Sent) && s.Contains(OutboundMessageStatus.Failed)),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([sent]);

        IReadOnlyList<OutboundMessageResult> results = await new ListDeliveriesQueryHandler(messages, company)
            .HandleAsync(new ListDeliveriesQuery(companyId));

        results.Should().ContainSingle().Which.Id.Should().Be(sent.Id);
    }

    [Fact]
    public async Task Suppressions_return_only_suppressed_with_company_scope()
    {
        Guid companyId = Guid.NewGuid();
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);
        var messages = Substitute.For<IOutboundMessageRepository>();
        OutboundMessage suppressed = WellFormedMessage(companyId);
        messages.ListByStatusAsync(companyId,
                Arg.Is<IReadOnlyCollection<OutboundMessageStatus>>(s =>
                    s.Count == 1 && s.Contains(OutboundMessageStatus.Suppressed)),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([suppressed]);

        IReadOnlyList<OutboundMessageResult> results = await new ListSuppressionsQueryHandler(messages, company)
            .HandleAsync(new ListSuppressionsQuery(companyId));

        results.Should().ContainSingle().Which.Id.Should().Be(suppressed.Id);
    }

    [Fact]
    public async Task Reads_reject_a_company_outside_the_active_scope()
    {
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(Guid.NewGuid());
        var messages = Substitute.For<IOutboundMessageRepository>();

        var deliveries = () => new ListDeliveriesQueryHandler(messages, company)
            .HandleAsync(new ListDeliveriesQuery(Guid.NewGuid()));
        await deliveries.Should().ThrowAsync<InvalidOperationException>();

        var suppressions = () => new ListSuppressionsQueryHandler(messages, company)
            .HandleAsync(new ListSuppressionsQuery(Guid.NewGuid()));
        await suppressions.Should().ThrowAsync<InvalidOperationException>();
    }

    private static OutboundMessage WellFormedMessage(Guid companyId)
    {
        Guid tenantId = Guid.NewGuid();
        return OutboundMessage.Queue(tenantId, null, companyId, Guid.NewGuid(), Guid.NewGuid(),
            "key-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddHours(1));
    }
}
