#pragma warning disable CS1591
namespace VumaRetail.Domain.Logistics;

public enum LogisticsShipmentStatus { Planned, InTransit, Delivered, Exception, Cancelled }
public enum LogisticsRunStatus { Planned, Dispatched, Completed, Cancelled }
public enum DeliveryStopStatus { Planned, OutForDelivery, Delivered, Failed, Skipped }
public enum ProofOfDeliveryOutcome { Delivered, Partial, Refused, Damaged }
