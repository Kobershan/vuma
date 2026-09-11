#pragma warning disable CS1591, IDE0011
namespace VumaRetail.Domain.Connect;

public enum TradingConnectionStatus
{
    Invited,
    Pending,
    Active,
    Suspended,
    Ended
}

public enum ConnectProposalStatus
{
    Pending,
    PartiallyAccepted,
    Accepted,
    Rejected,
    RolledBack
}
