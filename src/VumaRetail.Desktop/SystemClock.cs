using VumaRetail.Application.Abstractions;

namespace VumaRetail.Desktop;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}
