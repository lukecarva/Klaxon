using Klaxon.Core.Abstractions;

namespace Klaxon.Core.Tests;

/// <summary>Smoke tests for the time abstraction.</summary>
public class SystemClockTests
{
    [Fact]
    public void SystemClock_Now_is_close_to_the_real_clock()
    {
        ISystemClock clock = new SystemClock();

        var difference = (clock.Now - DateTimeOffset.Now).Duration();

        Assert.True(difference < TimeSpan.FromSeconds(1), $"Unexpected difference: {difference}.");
    }

    [Fact]
    public void SystemClock_Now_carries_the_local_offset()
    {
        ISystemClock clock = new SystemClock();

        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(clock.Now), clock.Now.Offset);
    }

    [Fact]
    public void FakeClock_advances_exactly_as_asked()
    {
        var start = new DateTimeOffset(2026, 3, 14, 7, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);

        clock.Advance(TimeSpan.FromMinutes(90));

        Assert.Equal(start.AddMinutes(90), clock.Now);
    }

    [Fact]
    public void FakeClock_rejects_a_negative_advance()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void FakeClock_allows_going_back_in_time_via_SetTo()
    {
        var start = new DateTimeOffset(2026, 3, 14, 7, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);

        clock.SetTo(start.AddHours(-2));

        Assert.Equal(start.AddHours(-2), clock.Now);
    }
}
