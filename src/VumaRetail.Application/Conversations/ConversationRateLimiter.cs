#pragma warning disable CS1591
using System.Collections.Concurrent;
using VumaRetail.Domain.Conversations;

namespace VumaRetail.Application.Conversations;

/// <summary>Fixed-window limits required by Stage 22b; counters are keyed by tenant and sender.</summary>
public sealed class ConversationRateLimiter
{
    private readonly ConcurrentDictionary<string, Window> windows = new(StringComparer.Ordinal);
    private readonly int messagesPerHour;
    private readonly int sensitiveRequestsPerDay;

    public ConversationRateLimiter(int messagesPerHour = 20, int sensitiveRequestsPerDay = 5)
    {
        if (messagesPerHour <= 0) { throw new ArgumentOutOfRangeException(nameof(messagesPerHour)); }
        if (sensitiveRequestsPerDay <= 0) { throw new ArgumentOutOfRangeException(nameof(sensitiveRequestsPerDay)); }
        this.messagesPerHour = messagesPerHour;
        this.sensitiveRequestsPerDay = sensitiveRequestsPerDay;
    }

    public bool TryConsume(Guid tenantId, string sender, ConversationIntent intent, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sender);
        string key = $"{tenantId:N}:{sender.Trim().ToUpperInvariant()}";
        Window window = windows.GetOrAdd(key, _ => new Window(at));
        lock (window)
        {
            if (at >= window.HourStarted.AddHours(1))
            {
                window.HourStarted = at;
                window.Messages = 0;
            }
            if (at >= window.DayStarted.AddDays(1))
            {
                window.DayStarted = at;
                window.SensitiveRequests = 0;
            }
            if (window.Messages >= messagesPerHour) { return false; }
            bool sensitive = intent is ConversationIntent.RequestStatement
                or ConversationIntent.RequestInvoiceCopy
                or ConversationIntent.RequestPod
                or ConversationIntent.RequestCreditNote;
            if (sensitive && window.SensitiveRequests >= sensitiveRequestsPerDay) { return false; }
            window.Messages++;
            if (sensitive) { window.SensitiveRequests++; }
            return true;
        }
    }

    private sealed class Window(DateTimeOffset at)
    {
        public DateTimeOffset HourStarted { get; set; } = at;
        public DateTimeOffset DayStarted { get; set; } = at;
        public int Messages { get; set; }
        public int SensitiveRequests { get; set; }
    }
}
