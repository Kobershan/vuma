using ZenithRetail.Application.Abstractions;

namespace ZenithRetail.Infrastructure.Time;

/// <summary>
/// The one place in Zenith that reads the wall clock.
/// </summary>
/// <remarks>
/// An architecture test fails the build on <c>DateTime.Now</c>, <c>DateTime.UtcNow</c> or
/// <c>DateTimeOffset.UtcNow</c> anywhere outside this type. That is a strong rule for a small class,
/// and it is worth it: the licensing ladder (ADR-028) runs over 45 days, leases expire in 72 hours,
/// and accounting periods close on a boundary. Code that reads the clock directly can only be tested
/// by waiting, so in practice it is not tested at all.
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
