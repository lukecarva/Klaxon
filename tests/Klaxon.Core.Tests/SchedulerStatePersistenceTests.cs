using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

/// <summary>Snooze/escalation survive a restart via CaptureState/RestoreState.</summary>
public class SchedulerStatePersistenceTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int min = 0) =>
        new(year, month, day, hour, min, 0, TimeSpan.Zero);

    private static Alarm Daily(UrgencyLevel level = UrgencyLevel.Normal) =>
        Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)), level);

    private static Alarm Escalatable(TimeSpan afterIgnored) =>
        Daily() with
        {
            Escalation = new EscalationPolicy { AfterIgnoredFor = afterIgnored, Ceiling = UrgencyLevel.Critical },
        };

    /// <summary>Builds a scheduler with the given alarms already loaded.</summary>
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
    public void Capture_includes_the_pending_snooze_with_counter()
    {
        var alarm = Daily();
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick(); // fires at 7:00
        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(10), out _));

        var state = scheduler.CaptureState();

        var snooze = Assert.Single(state.Snoozes);
        Assert.Equal(alarm.Id, snooze.AlarmId);
        Assert.Equal(1, snooze.Count);
        Assert.Equal(Utc(2026, 6, 10, 7, 10), snooze.DueAt);
    }

    [Fact]
    public void Restore_puts_back_the_snooze_counter()
    {
        var alarm = Daily();
        var (old, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        old.Tick();
        Assert.True(old.TrySnooze(alarm.Id, TimeSpan.FromMinutes(10), out _));
        var state = old.CaptureState();

        // "Restart": a fresh scheduler reloads the alarms, then restores the state.
        var freshClock = new FakeClock(Utc(2026, 6, 10, 7, 5));
        var fresh = new AlarmScheduler(freshClock);
        fresh.Reload([alarm]);
        fresh.RestoreState(state);

        Assert.Equal(1, fresh.SnoozeCountFor(alarm.Id));
    }

    [Fact]
    public void Pending_snooze_fires_on_the_first_tick_after_restart()
    {
        var alarm = Daily();
        var (old, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        old.Tick();
        Assert.True(old.TrySnooze(alarm.Id, TimeSpan.FromMinutes(10), out _)); // returns at 7:10
        var state = old.CaptureState();

        // Restart at 7:15: the snooze came due while the app was closed.
        var freshClock = new FakeClock(Utc(2026, 6, 10, 7, 15));
        var fresh = new AlarmScheduler(freshClock);
        var fired = new List<AlarmTriggeredEventArgs>();
        fresh.Triggered += (_, e) => fired.Add(e);
        fresh.Reload([alarm]);
        fresh.RestoreState(state);

        fresh.Tick();

        var trigger = Assert.Single(fired);
        Assert.Equal(TriggerKind.Snooze, trigger.Kind);
        Assert.Equal(alarm.Id, trigger.Alarm.Id);
    }

    [Fact]
    public void Escalation_resumes_from_the_saved_level_after_restart()
    {
        var alarm = Escalatable(TimeSpan.FromMinutes(10));
        var (old, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        old.Tick(); // 7:00, Normal
        clock.Advance(TimeSpan.FromMinutes(10));
        old.Tick(); // rises to High
        Assert.Equal(UrgencyLevel.High, fired[^1].EffectiveUrgency);

        var state = old.CaptureState();
        var escalation = Assert.Single(state.Escalations);
        Assert.Equal(UrgencyLevel.High, escalation.Level);

        // Restart: escalation resumes from High and rises to Critical.
        var freshClock = new FakeClock(Utc(2026, 6, 10, 7, 20));
        var fresh = new AlarmScheduler(freshClock);
        var freshFired = new List<AlarmTriggeredEventArgs>();
        fresh.Triggered += (_, e) => freshFired.Add(e);
        fresh.Reload([alarm]);
        fresh.RestoreState(state);

        fresh.Tick();

        var escalated = Assert.Single(freshFired, d => d.Kind == TriggerKind.Escalation);
        Assert.Equal(UrgencyLevel.Critical, escalated.EffectiveUrgency);
    }

    [Fact]
    public void Restore_ignores_state_for_an_alarm_that_no_longer_exists()
    {
        var alarm = Daily();
        var state = new SchedulerState
        {
            Snoozes = [new SnoozeState(Guid.NewGuid(), 3, Utc(2026, 6, 10, 7, 10))],
        };

        var (scheduler, _, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);
        scheduler.RestoreState(state);

        // The orphan snooze was dropped, so nothing extra is pending.
        Assert.True(scheduler.CaptureState().IsEmpty);
    }

    [Fact]
    public void Restore_drops_escalation_when_the_policy_was_removed()
    {
        // The alarm still exists but no longer escalates: the saved escalation is dropped.
        var alarm = Daily();
        var state = new SchedulerState
        {
            Escalations = [new EscalationSnapshot(alarm.Id, UrgencyLevel.Critical, Utc(2026, 6, 10, 7, 10))],
        };

        var (scheduler, _, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);
        scheduler.RestoreState(state);

        Assert.Empty(scheduler.CaptureState().Escalations);
    }

    [Fact]
    public void Dismiss_leaves_the_state_empty()
    {
        var alarm = Daily();
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(10), out _));

        scheduler.Dismiss(alarm.Id);

        Assert.True(scheduler.CaptureState().IsEmpty);
    }

    [Fact]
    public void Snooze_raises_StateChanged()
    {
        var alarm = Daily();
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        var changed = 0;
        scheduler.StateChanged += (_, _) => changed++;

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick(); // a new occurrence already touches the state
        var afterTick = changed;

        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(10), out _));

        Assert.True(changed > afterTick, "Snoozing must raise StateChanged.");
    }

    [Fact]
    public void Removing_an_alarm_on_reload_raises_StateChanged_to_clear_disk()
    {
        var alarm = Daily();
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(10), out _));

        var changed = 0;
        scheduler.StateChanged += (_, _) => changed++;

        // The alarm is gone: Reload drops its snooze and must persist the removal.
        scheduler.Reload([]);

        Assert.True(changed > 0, "Dropping orphans on Reload must raise StateChanged.");
        Assert.True(scheduler.CaptureState().IsEmpty);
    }
}
