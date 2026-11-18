using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

public class ScheduleTests
{
    private static readonly TimeZoneInfo Dst = TestZones.SouthernDst;

    private static DateTimeOffset At(int year, int month, int day, int hour, int min, int offsetHours) =>
        new(year, month, day, hour, min, 0, TimeSpan.FromHours(offsetHours));

    // ---------- Daily

    [Fact]
    public void Daily_uses_todays_time_when_it_has_not_passed()
    {
        var schedule = new DailySchedule(new TimeOnly(7, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 5, 30, -3), Dst);

        Assert.Equal(At(2026, 6, 10, 7, 0, -3), next);
    }

    [Fact]
    public void Daily_rolls_to_tomorrow_when_todays_time_passed()
    {
        var schedule = new DailySchedule(new TimeOnly(7, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 7, 0, -3), Dst);

        Assert.Equal(At(2026, 6, 11, 7, 0, -3), next);
    }

    [Fact]
    public void Daily_keeps_wall_time_across_dst_start()
    {
        // 7am stays 7am after the DST turn; only the offset changes.
        var schedule = new DailySchedule(new TimeOnly(7, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 10, 14, 12, 0, -3), Dst);

        Assert.Equal(At(2026, 10, 15, 7, 0, -2), next);
    }

    [Fact]
    public void Daily_fires_at_end_of_gap_when_the_time_does_not_exist()
    {
        // 00:30 does not exist on Oct 15; fires at the end of the gap.
        var schedule = new DailySchedule(new TimeOnly(0, 30));

        var next = schedule.NextOccurrenceAfter(At(2026, 10, 14, 23, 0, -3), Dst);

        Assert.Equal(At(2026, 10, 15, 1, 0, -2), next);
    }

    [Fact]
    public void Daily_picks_the_first_pass_when_the_time_happens_twice()
    {
        // 23:30 happens twice on Feb 14; fires on the first pass.
        var schedule = new DailySchedule(new TimeOnly(23, 30));

        var next = schedule.NextOccurrenceAfter(At(2026, 2, 14, 12, 0, -2), Dst);

        Assert.Equal(At(2026, 2, 14, 23, 30, -2), next);
    }

    [Fact]
    public void Daily_never_returns_the_queried_instant_itself()
    {
        var schedule = new DailySchedule(new TimeOnly(7, 0));
        var now = At(2026, 6, 10, 7, 0, -3);

        var next = schedule.NextOccurrenceAfter(now, Dst);

        Assert.True(next > now, "NextOccurrenceAfter must be strictly later.");
    }

    // ---------- Weekly

    [Fact]
    public void Weekly_jumps_to_the_next_enabled_day()
    {
        // 2026-06-10 is a Wednesday; alarm is Mon/Fri.
        var schedule = new WeeklySchedule(WeekDays.Monday | WeekDays.Friday, new TimeOnly(9, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 10, 12, 0, -3), Dst);

        Assert.Equal(At(2026, 6, 12, 9, 0, -3), next);
        Assert.Equal(DayOfWeek.Friday, next!.Value.DayOfWeek);
    }

    [Fact]
    public void Weekly_crosses_the_week_boundary()
    {
        // Friday night, alarm only on Monday.
        var schedule = new WeeklySchedule(WeekDays.Monday, new TimeOnly(9, 0));

        var next = schedule.NextOccurrenceAfter(At(2026, 6, 12, 22, 0, -3), Dst);

        Assert.Equal(At(2026, 6, 15, 9, 0, -3), next);
    }

    [Fact]
    public void Weekly_with_no_day_schedules_nothing()
    {
        var schedule = new WeeklySchedule(WeekDays.None, new TimeOnly(9, 0));

        Assert.Null(schedule.NextOccurrenceAfter(At(2026, 6, 10, 12, 0, -3), Dst));
    }

    [Fact]
    public void Weekly_with_weekdays_describes_briefly()
    {
        var schedule = new WeeklySchedule(WeekDays.Weekdays, new TimeOnly(8, 30));

        Assert.Equal("Dias úteis, 08:30", schedule.Describe());
    }

    [Fact]
    public void Weekly_describes_individual_days_with_abbreviations()
    {
        var schedule = new WeeklySchedule(WeekDays.Monday | WeekDays.Wednesday, new TimeOnly(8, 30));

        Assert.Equal("seg, qua, 08:30", schedule.Describe());
    }

    // ---------- One-time

    [Fact]
    public void One_time_in_the_future_returns_the_exact_instant()
    {
        var when = At(2026, 6, 10, 15, 0, -3);
        var schedule = new OneTimeSchedule(when);

        Assert.Equal(when, schedule.NextOccurrenceAfter(At(2026, 6, 10, 14, 0, -3), Dst));
    }

    [Fact]
    public void One_time_in_the_past_has_no_next_occurrence()
    {
        var schedule = new OneTimeSchedule(At(2026, 6, 10, 15, 0, -3));

        Assert.Null(schedule.NextOccurrenceAfter(At(2026, 6, 10, 15, 0, -3), Dst));
    }
}
