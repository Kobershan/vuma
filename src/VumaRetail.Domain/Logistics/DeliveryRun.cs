#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Logistics;

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class DeliveryRun : Entity
{
    private DeliveryRun(Guid tenantId, Guid? storeId, Guid? carrierId, string runNumber, DateOnly plannedDate, string? driverName, string? vehicleRegistration) : base(tenantId, storeId)
    { CarrierId = carrierId; RunNumber = runNumber; PlannedDate = plannedDate; DriverName = driverName; VehicleRegistration = vehicleRegistration; Status = LogisticsRunStatus.Planned; }
    private DeliveryRun() { }
    public Guid? CarrierId { get; private set; }
    public string RunNumber { get; private set; } = string.Empty;
    public DateOnly PlannedDate { get; private set; }
    public string? DriverName { get; private set; }
    public string? VehicleRegistration { get; private set; }
    public LogisticsRunStatus Status { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public static DeliveryRun Create(Guid tenantId, Guid? storeId, Guid? carrierId, string runNumber, DateOnly plannedDate, string? driverName, string? vehicleRegistration)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(runNumber)) { throw new ArgumentException("Tenant and run number are required."); }
        return new DeliveryRun(tenantId, storeId, carrierId, runNumber.Trim(), plannedDate, Clean(driverName, 160), Clean(vehicleRegistration, 32));
    }
    public void Dispatch(DateTimeOffset at) { if (Status != LogisticsRunStatus.Planned) { throw new InvalidOperationException("Only planned runs can be dispatched."); } Status = LogisticsRunStatus.Dispatched; DispatchedAt = at; }
    public void Complete(DateTimeOffset at) { if (Status != LogisticsRunStatus.Dispatched) { throw new InvalidOperationException("Only dispatched runs can be completed."); } Status = LogisticsRunStatus.Completed; CompletedAt = at; }
    public void Cancel() { if (Status == LogisticsRunStatus.Completed) { throw new InvalidOperationException("Completed run cannot be cancelled."); } Status = LogisticsRunStatus.Cancelled; }
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
