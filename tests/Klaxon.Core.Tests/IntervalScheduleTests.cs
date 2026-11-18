using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

public class IntervalScheduleTests
{
    private static readonly TimeZoneInfo Dst = TestZones.SouthernDst;

    private static DateTimeOffset At(int year, int month, int day, int hour, int min, int offsetHours) =>
        new(year, month, day, hour, min, 0, TimeSpan.FromHours(offsetHours));

    // ---------- No time range

    [Fact]
    public void Without_window_fires_each_interval_from_the_anchor()
    {
        var anchor = At(2026, 6, 10, 9, 0, -3);
        var schedule = new IntervalSchedule(TimeSpan.FromMinutes(45), anchor);

        var next = schedule.NextOccurrenceAfter(anchor, Dst);

        Assert.Equal(At(2026, 6, 10, 9, 45, -3), next);
    }

    [Fact]
    public void Keeps_the_rhythm_even_when_queried_mid_cycle()
    {
        // Asking mid-cycle at 10:00 returns the original grid point (10:30).
        var anchor = At(2026, 6, 10, 9, 0, -3);
        var schedule = new IntervalSchedule(TimeSpan.FromMinutes(45), anchor);

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 10, 0, -3), Dst);

        Assert.Equal(At(2026, 6, 10, 10, 30, -3), next);
    }

    [Fact]
    public void Works_for_instants_before_the_anchor()
    {
        var anchor = At(2026, 6, 10, 9, 0, -3);
        var schedule = new IntervalSchedule(TimeSpan.FromHours(1), anchor);

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 6, 30, -3), Dst);

        Assert.Equal(At(2026, 6, 10, 7, 0, -3), next);
    }

    [Fact]
    public void Zero_interval_schedules_nothing()
    {
        var schedule = new IntervalSchedule(TimeSpan.Zero, At(2026, 6, 10, 9, 0, -3));

        Assert.Null(schedule.NextOccurrenceAfter(At(2026, 6, 10, 9, 0, -3), Dst));
    }

    // ---------- Time range

    [Fact]
    public void Before_opening_waits_for_the_window_to_start()
    {
        var anchor = At(2026, 6, 10, 9, 0, -3);
        var schedule = new IntervalSchedule(
            TimeSpan.FromMinutes(45),
            anchor,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 6, 0, -3), Dst);

        Assert.Equal(At(2026, 6, 10, 9, 0, -3), next);
    }

    [Fact]
    public void After_closing_jumps_to_next_days_opening()
    {
        // The reminder should not fire at 3am.
        var anchor = At(2026, 6, 10, 9, 0, -3);
        var schedule = new IntervalSchedule(
            TimeSpan.FromMinutes(45),
            anchor,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 17, 50, -3), Dst);

        Assert.Equal(At(2026, 6, 11, 9, 0, -3), next);
    }

    [Fact]
    public void The_window_end_is_exclusive()
    {
        var anchor = At(2026, 6, 10, 9, 0, -3);
        var schedule = new IntervalSchedule(
            TimeSpan.FromHours(1),
            anchor,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0));

        // 18:00 sharp is already out; the last firing of the day is 17:00.
        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 17, 0, -3), Dst);

        Assert.Equal(At(2026, 6, 11, 9, 0, -3), next);
    }

    [Fact]
    public void Window_crossing_midnight_still_applies_after_midnight()
    {
        var anchor = At(2026, 6, 10, 22, 0, -3);
        var schedule = new IntervalSchedule(
            TimeSpan.FromHours(1),
            anchor,
            new TimeOnly(22, 0),
            new TimeOnly(6, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 23, 30, -3), Dst);

        Assert.Equal(At(2026, 6, 11, 0, 0, -3), next);
    }

    [Fact]
    public void Window_crossing_midnight_reopens_the_next_night()
    {
        var anchor = At(2026, 6, 10, 22, 0, -3);
        var schedule = new IntervalSchedule(
            TimeSpan.FromHours(1),
            anchor,
            new TimeOnly(22, 0),
            new TimeOnly(6, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 11, 6, 30, -3), Dst);

        Assert.Equal(At(2026, 6, 11, 22, 0, -3), next);
    }

    // ---------- DST

    [Fact]
    public void Interval_is_absolute_duration_and_does_not_shift_on_dst()
    {
        // "Every 1 hour" is 60 real minutes; the wall clock reads 01:00 because it jumped.
        var anchor = At(2026, 10, 14, 23, 0, -3);
        var schedule = new IntervalSchedule(TimeSpan.FromHours(1), anchor);

        var next = schedule.NextOccurrenceAfter(anchor, Dst);

        Assert.Equal(anchor.AddHours(1), next);

        var wall = TimeZoneInfo.ConvertTime(next!.Value, Dst);
        Assert.Equal(new TimeSpan(1, 0, 0), wall.TimeOfDay);
        Assert.Equal(TimeSpan.FromHours(-2), wall.Offset);
    }

    // ---------- Describe

    [Theory]
    [InlineData(45, "A cada 45 min")]
    [InlineData(60, "A cada 1h")]
    [InlineData(90, "A cada 1h30")]
    public void Describes_the_interval_briefly(int minutes, string expected)
    {
        var schedule = new IntervalSchedule(TimeSpan.FromMinutes(minutes), At(2026, 6, 10, 9, 0, -3));

        Assert.Equal(expected, schedule.Describe());
    }

    [Fact]
    public void Describes_the_window_alongside_when_present()
    {
        var schedule = new IntervalSchedule(
            TimeSpan.FromMinutes(45),
            At(2026, 6, 10, 9, 0, -3),
            new TimeOnly(9, 0),
            new TimeOnly(18, 0));

        Assert.Equal("A cada 45 min, 09:00 às 18:00", schedule.Describe());
    }
}
