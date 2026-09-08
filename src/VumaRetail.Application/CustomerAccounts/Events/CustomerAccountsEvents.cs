#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.CustomerAccounts.Events;

public sealed record LayByDepositReceivedEvent(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "layby.deposit.received";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}

public sealed record LayByInstalmentReceivedEvent(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "layby.instalment.received";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}

public sealed record LayByCompletedEvent(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "layby.completed";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}

public sealed record LayByCancelledEvent(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "layby.cancelled";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}

public sealed record AccountInterestRaisedEvent(    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "account.interest.raised";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}

public sealed record AccountPaymentReceivedEvent(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts) : IFinancialEvent
{
    public string EventType => "account.payment.received";
    public Guid? DepartmentId => null;
    public Guid? CostCentreId => null;
    public Guid? ProjectId => null;
    public Guid? ChannelId => null;
    public Guid? EmployeeId => null;
}
