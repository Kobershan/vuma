using System.Collections.Concurrent;

namespace VumaRetail.PublicApi.Loyalty;

/// <summary>
/// Fixed-window rate limiting for the public loyalty surface (Stage 20). Per-caller buckets:
/// tills 100/min, members 30/min, partners 60/min. Idempotent replays of a known key are exempt —
/// a legitimate retry must never be penalised (checked against the transaction log, not here).
/// </summary>
/// <remarks>
/// In-memory and per node: correct for a store deployment behind one server. A multi-node cloud
/// deployment moves these counters to Redis (Stage 30b) — the limits and headers stay identical.
/// </remarks>
public sealed class LoyaltyRateLimiter
{
    private readonly ConcurrentDictionary<string, Window> _windows = new();

    /// <summary>Tries to take one request from the caller's current window.</summary>
    /// <param name="key">The caller identity (credential hash).</param>
    /// <param name="limitPerMinute">The caller's limit.</param>
    /// <param name="now">The instant, UTC.</param>
    /// <param name="retryAfter">How long to wait when refused.</param>
    /// <returns>True when the request may proceed.</returns>
    public bool TryAcquire(string key, int limitPerMinute, DateTimeOffset now, out TimeSpan retryAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        while (true)
        {
            Window window = _windows.GetOrAdd(key, _ => new Window(now, 0));

            lock (window)
            {
                if (now - window.StartedAt >= TimeSpan.FromMinutes(1))
                {
                    window.StartedAt = now;
                    window.Count = 0;
                }

                if (window.Count < limitPerMinute)
                {
                    window.Count++;
                    retryAfter = TimeSpan.Zero;
                    return true;
                }

                retryAfter = window.StartedAt.AddMinutes(1) - now;
                if (retryAfter < TimeSpan.Zero)
                {
                    retryAfter = TimeSpan.Zero;
                }

                return false;
            }
        }
    }

    private sealed class Window(DateTimeOffset startedAt, int count)
    {
        public DateTimeOffset StartedAt = startedAt;
        public int Count = count;
    }
}
