using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Logistics;

/// <summary>A road vehicle owned or operated by a tenant for delivery and collection work.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Vehicle : Entity
{
    private Vehicle(Guid tenantId, Guid? storeId, string registration, string make, string model,
        VehicleType type, string? vin, decimal capacityKg, decimal odometerKm, DateOnly? nextServiceOn)
        : base(tenantId, storeId)
    {
        Registration = registration;
        Make = make;
        Model = model;
        Type = type;
        Vin = vin;
        CapacityKg = capacityKg;
        OdometerKm = odometerKm;
        NextServiceOn = nextServiceOn;
        Status = VehicleStatus.Active;
    }

    private Vehicle() { }

    /// <summary>Registration or licence plate.</summary>
    public string Registration { get; private set; } = string.Empty;
    /// <summary>Manufacturer.</summary>
    public string Make { get; private set; } = string.Empty;
    /// <summary>Manufacturer model.</summary>
    public string Model { get; private set; } = string.Empty;
    /// <summary>Vehicle category.</summary>
    public VehicleType Type { get; private set; }
    /// <summary>Vehicle identification number, where available.</summary>
    public string? Vin { get; private set; }
    /// <summary>Maximum recorded payload capacity in kilograms.</summary>
    public decimal CapacityKg { get; private set; }
    /// <summary>Current odometer reading in kilometres.</summary>
    public decimal OdometerKm { get; private set; }
    /// <summary>Next planned service date.</summary>
    public DateOnly? NextServiceOn { get; private set; }
    /// <summary>Operational status.</summary>
    public VehicleStatus Status { get; private set; }

    /// <summary>Creates a tenant-owned vehicle.</summary>
    public static Vehicle Create(Guid tenantId, Guid? storeId, string registration, string make,
        string model, VehicleType type, string? vin, decimal capacityKg, decimal odometerKm,
        DateOnly? nextServiceOn)
    {
        if (tenantId == Guid.Empty) { throw new ArgumentException("Tenant is required.", nameof(tenantId)); }
        if (type == VehicleType.Unknown) { throw new ArgumentException("Vehicle type is required.", nameof(type)); }
        if (capacityKg < 0) { throw new ArgumentOutOfRangeException(nameof(capacityKg)); }
        if (odometerKm < 0) { throw new ArgumentOutOfRangeException(nameof(odometerKm)); }
        return new Vehicle(tenantId, storeId, Required(registration, nameof(registration)).ToUpperInvariant(),
            Required(make, nameof(make)), Required(model, nameof(model)), type, Optional(vin), capacityKg,
            odometerKm, nextServiceOn);
    }

    /// <summary>Records a monotonic odometer reading.</summary>
    public void RecordOdometer(decimal readingKm)
    {
        if (readingKm < OdometerKm) { throw new InvalidOperationException("Odometer readings cannot move backwards."); }
        OdometerKm = readingKm;
    }

    /// <summary>Changes the vehicle's operational status.</summary>
    public void SetStatus(VehicleStatus status)
    {
        if (status == VehicleStatus.Unknown) { throw new ArgumentException("Vehicle status is required.", nameof(status)); }
        Status = status;
    }

    /// <summary>Schedules the next service date.</summary>
    public void ScheduleService(DateOnly serviceOn) => NextServiceOn = serviceOn;

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Physical vehicle category.</summary>
public enum VehicleType
{
    /// <summary>Not specified.</summary>
    Unknown = 0,
    /// <summary>Passenger car.</summary>
    Car = 1,
    /// <summary>Light delivery van.</summary>
    Van = 2,
    /// <summary>Heavy delivery truck.</summary>
    Truck = 3,
    /// <summary>Motorcycle or scooter.</summary>
    Motorcycle = 4,
    /// <summary>Other road vehicle.</summary>
    Other = 5,
}

/// <summary>Vehicle operational status.</summary>
public enum VehicleStatus
{
    /// <summary>Not specified.</summary>
    Unknown = 0,
    /// <summary>Available for operations.</summary>
    Active = 1,
    /// <summary>Temporarily unavailable for service.</summary>
    Maintenance = 2,
    /// <summary>Not currently assigned to operations.</summary>
    Inactive = 3,
    /// <summary>Permanently removed from service.</summary>
    Retired = 4,
}
