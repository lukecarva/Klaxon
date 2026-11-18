using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

public class AlarmSchedulerTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int min = 0) =>
        new(year, month, day, hour, min, 0, TimeSpan.Zero);

    /// <summary>Builds a scheduler and captures its triggers, with a fake clock.</summary>
    private static (AlarmScheduler Scheduler, FakeClock Clock, List<AlarmTriggeredEventArgs> Fired) Build(
        DateTimeOffset start,
        params Alarm[] alarms)
    {
        var clock = new FakeClock(start);
        var scheduler = new AlarmScheduler(clock);
        var fired = new List<AlarmTriggeredEventArgs>();

        scheduler.Triggered += (_, e) => fired.Add(e);
        scheduler.Reload(alarms);

        return (scheduler, clock, fired);
    }

    [Fact]
    public void Does_not_fire_before_the_time()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromMinutes(59));
        scheduler.Tick();

        Assert.Empty(fired);
    }

    [Fact]
    public void Fires_on_time()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();

        var trigger = Assert.Single(fired);
        Assert.Equal(TriggerKind.OnTime, trigger.Kind);
        Assert.Equal(Utc(2026, 6, 10, 7, 0), trigger.ScheduledFor);
        Assert.Equal(0, trigger.SkippedOccurrences);
    }

    [Fact]
    public void Fires_once_even_with_several_ticks()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 59), alarm);

        clock.Advance(TimeSpan.FromMinutes(1));
        scheduler.Tick();
        scheduler.Tick();
        scheduler.Tick();

        Assert.Single(fired);
    }

    [Fact]
    public void Lateness_within_window_still_counts_as_on_time()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        // Ten minutes without a tick (a stall or long GC).
        clock.Advance(TimeSpan.FromMinutes(70));
        scheduler.Tick();

        Assert.Equal(TriggerKind.OnTime, fired.Single().Kind);
        Assert.Equal(TimeSpan.FromMinutes(10), fired.Single().Delay);
    }

    [Fact]
    public void Sleeping_past_the_window_marks_the_alarm_missed()
    {
        var alarm = Alarm.New("Remédio", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        // Slept at 6am, woke at 10am; the 7am alarm must not look on-time.
        clock.Advance(TimeSpan.FromHours(4));
        scheduler.Tick();

        var trigger = Assert.Single(fired);
        Assert.Equal(TriggerKind.Missed, trigger.Kind);
        Assert.Equal(TimeSpan.FromHours(3), trigger.Delay);
    }

    [Fact]
    public void Three_days_off_produce_one_alert_not_three()
    {
        var alarm = Alarm.New("Remédio", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.SetTo(Utc(2026, 6, 13, 8, 0));
        scheduler.Tick();

        var trigger = Assert.Single(fired);
        Assert.Equal(TriggerKind.Missed, trigger.Kind);
        Assert.Equal(Utc(2026, 6, 13, 7, 0), trigger.ScheduledFor);
        Assert.Equal(3, trigger.SkippedOccurrences);
    }

    [Fact]
    public void After_firing_the_next_occurrence_is_already_scheduled()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();

        Assert.Equal(Utc(2026, 6, 11, 7, 0), scheduler.NextFireTime);
    }

    [Fact]
    public void One_time_alarm_leaves_the_schedule_after_firing()
    {
        var alarm = Alarm.New("Ligar para o dentista", new OneTimeSchedule(Utc(2026, 6, 10, 7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();

        Assert.Single(fired);
        Assert.Null(scheduler.NextFireTime);
    }

    [Fact]
    public void Clock_moving_back_recomputes_instead_of_firing_a_burst()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        // The user set the system clock ten days back.
        clock.SetTo(Utc(2026, 5, 31, 6, 0));
        scheduler.Tick();

        Assert.Empty(fired);
        Assert.Equal(Utc(2026, 5, 31, 7, 0), scheduler.NextFireTime);
    }

    [Fact]
    public void Disabled_alarm_does_not_enter_the_schedule()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0))) with { IsEnabled = false };
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(2));
        scheduler.Tick();

        Assert.Empty(fired);
        Assert.Null(scheduler.NextFireTime);
    }

    // ---------- Snooze

    [Fact]
    public void Snooze_reshows_the_alarm_after_the_delay()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        fired.Clear();

        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(5), out _));

        clock.Advance(TimeSpan.FromMinutes(4));
        scheduler.Tick();
        Assert.Empty(fired);

        clock.Advance(TimeSpan.FromMinutes(1));
        scheduler.Tick();

        Assert.Equal(TriggerKind.Snooze, fired.Single().Kind);
    }

    [Fact]
    public void Critical_level_allows_a_single_snooze()
    {
        var alarm = Alarm.New("Sair de casa", new DailySchedule(new TimeOnly(7, 0)), UrgencyLevel.Critical);
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();

        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(2), out _));
        Assert.False(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(2), out var refusal));
        Assert.Contains("uma vez", refusal);
    }

    [Fact]
    public void Whisper_level_allows_no_snooze()
    {
        var alarm = Alarm.New("Beber água", new DailySchedule(new TimeOnly(7, 0)), UrgencyLevel.Whisper);
        var (scheduler, _, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        Assert.False(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(5), out var refusal));
        Assert.Contains("Sussurro", refusal);
    }

    [Fact]
    public void New_occurrence_restores_the_snooze_budget()
    {
        var alarm = Alarm.New("Sair de casa", new DailySchedule(new TimeOnly(7, 0)), UrgencyLevel.Critical);
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(2), out _));
        Assert.Equal(1, scheduler.SnoozeCountFor(alarm.Id));

        // Next day: the snooze limit is per occurrence, not forever.
        clock.SetTo(Utc(2026, 6, 11, 7, 0));
        scheduler.Tick();

        Assert.Equal(0, scheduler.SnoozeCountFor(alarm.Id));
        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(2), out _));
    }

    [Fact]
    public void Dismiss_cancels_the_pending_snooze()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        fired.Clear();

        scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(5), out _);
        scheduler.Dismiss(alarm.Id);

        clock.Advance(TimeSpan.FromMinutes(10));
        scheduler.Tick();

        Assert.Empty(fired);
    }

    [Fact]
    public void Removing_the_alarm_drops_the_pending_snooze()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(5), out _);
        fired.Clear();

        scheduler.Reload([]);

        clock.Advance(TimeSpan.FromMinutes(10));
        scheduler.Tick();

        Assert.Empty(fired);
    }

    [Fact]
    public void NextFireTime_considers_the_snooze_when_it_comes_first()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(5), out _);

        Assert.Equal(Utc(2026, 6, 10, 7, 5), scheduler.NextFireTime);
    }
}
