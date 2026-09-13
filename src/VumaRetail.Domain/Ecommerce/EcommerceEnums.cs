#pragma warning disable CS1591
namespace VumaRetail.Domain.Ecommerce;

/// <summary>Lifecycle of a storefront/channel registration.</summary>
public enum ChannelConnectionStatus
{
    Draft = 0,
    Active = 1,
    Suspended = 2,
    Ended = 3,
}
