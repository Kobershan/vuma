#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.CustomerAccounts.Events;

public sealed record StokvelContributionReceivedEvent(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "stokvel.contribution.received";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}

public sealed record StokvelBenefitAllocatedEvent(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "stokvel.benefit.allocated";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}

public sealed record StokvelPayoutSettledEvent(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "stokvel.payout.settled";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}
