#pragma warning disable CS1591
namespace VumaRetail.Domain.Quality;

/// <summary>Lifecycle of a quality hold.</summary>
public enum QualityHoldStatus
{
    Held,
    Released,
    Rejected,
}
