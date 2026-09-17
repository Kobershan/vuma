#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Reporting;

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class DashboardMeasure : Entity
{
    private DashboardMeasure(Guid tenantId, Guid? storeId, Guid companyId, DateOnly businessDate,
        string name, string currency, decimal value, DateTimeOffset asAtUtc) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        BusinessDate = businessDate;
        Name = name.Trim().ToUpperInvariant();
        Currency = currency.Trim().ToUpperInvariant();
        Value = value;
        AsAtUtc = asAtUtc.ToUniversalTime();
    }

    private DashboardMeasure() { }
    public DateOnly BusinessDate { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public decimal Value { get; private set; }
    public DateTimeOffset AsAtUtc { get; private set; }

    public static DashboardMeasure Record(Guid tenantId, Guid? storeId, Guid companyId, DateOnly businessDate,
        string name, string currency, decimal value, DateTimeOffset asAtUtc)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("Measure scope is required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be a three-letter code.", nameof(currency));
        }
        return new(tenantId, storeId, companyId, businessDate, name, currency, value, asAtUtc);
    }

    public void Replace(decimal value, DateTimeOffset asAtUtc)
    {
        if (asAtUtc < AsAtUtc)
        {
            throw new InvalidOperationException("A dashboard measure cannot move backwards in time.");
        }
        Value = value;
        AsAtUtc = asAtUtc.ToUniversalTime();
    }
}
