using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Diagnostics;

/// <summary>
/// The default <see cref="ICorrelationContext"/>: adopts the caller's id, or mints one.
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped, so a background job and a request each get their own. The minted id is a
/// UUID v7 rendered without dashes — time-ordered, so two ids from the same afternoon sort next to
/// each other in a log file, which is the only thing anybody ever does with them.
/// </para>
/// <para>
/// A supplied id is truncated rather than rejected. It arrives from a header, so it is attacker
/// controlled; the cost of a caller sending a megabyte of "correlation id" should be that it is
/// clipped, not that a request fails for a reason nobody can act on.
/// </para>
/// </remarks>
public sealed class CorrelationContext : ICorrelationContext
{
    /// <summary>The longest correlation id accepted from a caller.</summary>
    public const int MaxLength = 128;

    private string? _correlationId;

    /// <inheritdoc />
    public string CorrelationId => _correlationId ??= UuidV7.NewGuid().ToString("n");

    /// <inheritdoc />
    public void Set(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return;
        }

        string trimmed = correlationId.Trim();

        _correlationId = trimmed.Length > MaxLength ? trimmed[..MaxLength] : trimmed;
    }
}
