using FluentAssertions;
using VumaRetail.Application.Service;

namespace VumaRetail.UnitTests.Service;

public sealed class ServiceSlaClockTests
{
    private readonly BusinessHoursServiceSlaClock clock = new(new TimeOnly(9, 0), new TimeOnly(17, 0));

    [Fact]
    public void Counts_only_weekday_business_hours()
    {
        DateTimeOffset start = new(2026, 9, 14, 15, 0, 0, TimeSpan.Zero); // Monday
        DateTimeOffset end = new(2026, 9, 15, 11, 0, 0, TimeSpan.Zero);

        clock.WorkingHoursBetween(start, end).Should().Be(4m);
    }

    [Fact]
    public void Returns_zero_for_reversed_or_weekend_interval()
    {
        clock.WorkingHoursBetween(new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 13, 17, 0, 0, TimeSpan.Zero)).Should().Be(0m);
        clock.WorkingHoursBetween(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-1)).Should().Be(0m);
    }
}
