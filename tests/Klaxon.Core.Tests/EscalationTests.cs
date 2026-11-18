using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

public class EscalationTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int min = 0) =>
        new(year, month, day, hour, min, 0, TimeSpan.Zero);

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

    private static Alarm Escalatable(
        UrgencyLevel baseLevel = UrgencyLevel.Normal,
        TimeSpan? afterIgnored = null,
        int? afterSnoozes = null,
        UrgencyLevel ceiling = UrgencyLevel.Critical) =>
        Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)), baseLevel) with
        {
            Escalation = new EscalationPolicy
            {
                AfterIgnoredFor = afterIgnored,
                AfterSnoozes = afterSnoozes,
                Ceiling = ceiling,
            },
        };

    // ---------- Initial trigger

    [Fact]
    public void Normal_trigger_carries_the_base_level_as_effective()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)), UrgencyLevel.Normal);
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();

        Assert.Equal(UrgencyLevel.Normal, fired.Single().EffectiveUrgency);
    }

    // ---------- Escalation by being ignored

    [Fact]
    public void Ignored_past_the_deadline_reshows_one_level_higher()
    {
        var alarm = Escalatable(UrgencyLevel.Normal, afterIgnored: TimeSpan.FromMinutes(10));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1)); // fires at 7:00
        scheduler.Tick();
        fired.Clear();

        clock.Advance(TimeSpan.FromMinutes(9));
        scheduler.Tick();
        Assert.Empty(fired); // still within the 10 min

        clock.Advance(TimeSpan.FromMinutes(1));
        scheduler.Tick();

        var escalation = Assert.Single(fired);
        Assert.Equal(TriggerKind.Escalation, escalation.Kind);
        Assert.Equal(UrgencyLevel.High, escalation.EffectiveUrgency);
    }

    [Fact]
    public void Repeatedly_ignored_rises_step_by_step_to_the_ceiling()
    {
        var alarm = Escalatable(UrgencyLevel.Normal, afterIgnored: TimeSpan.FromMinutes(10));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        fired.Clear();

        clock.Advance(TimeSpan.FromMinutes(10));
        scheduler.Tick();
        Assert.Equal(UrgencyLevel.High, fired[^1].EffectiveUrgency);

        clock.Advance(TimeSpan.FromMinutes(10));
        scheduler.Tick();
        Assert.Equal(UrgencyLevel.Critical, fired[^1].EffectiveUrgency);

        // At the ceiling (Critical) it stops rising and re-showing.
        var before = fired.Count;
        clock.Advance(TimeSpan.FromMinutes(30));
        scheduler.Tick();
        Assert.Equal(before, fired.Count);
    }

    [Fact]
    public void Respects_the_configured_ceiling()
    {
        var alarm = Escalatable(UrgencyLevel.Normal, afterIgnored: TimeSpan.FromMinutes(5), ceiling: UrgencyLevel.High);
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        fired.Clear();

        clock.Advance(TimeSpan.FromMinutes(5));
        scheduler.Tick();
        Assert.Equal(UrgencyLevel.High, fired[^1].EffectiveUrgency);

        // Ceiling is High: it never reaches Critical.
        var before = fired.Count;
        clock.Advance(TimeSpan.FromMinutes(30));
        scheduler.Tick();
        Assert.Equal(before, fired.Count);
    }

    [Fact]
    public void Dismiss_ends_the_escalation()
    {
        var alarm = Escalatable(UrgencyLevel.Normal, afterIgnored: TimeSpan.FromMinutes(10));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        scheduler.Dismiss(alarm.Id);
        fired.Clear();

        clock.Advance(TimeSpan.FromMinutes(20));
        scheduler.Tick();

        Assert.Empty(fired);
    }

    [Fact]
    public void Without_a_policy_it_does_not_escalate()
    {
        var alarm = Alarm.New("Reunião", new DailySchedule(new TimeOnly(7, 0)));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        fired.Clear();

        clock.Advance(TimeSpan.FromHours(2));
        scheduler.Tick();

        Assert.DoesNotContain(fired, d => d.Kind == TriggerKind.Escalation);
    }

    // ---------- Escalation by snoozing

    [Fact]
    public void Snoozing_to_the_limit_raises_the_level_on_return()
    {
        var alarm = Escalatable(UrgencyLevel.Normal, afterSnoozes: 2);
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        fired.Clear();

        // 1st snooze: still Normal when it returns.
        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(5), out _));
        clock.Advance(TimeSpan.FromMinutes(5));
        scheduler.Tick();
        Assert.Equal(UrgencyLevel.Normal, fired[^1].EffectiveUrgency);

        // 2nd snooze hits the threshold: returns as Important.
        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(5), out _));
        clock.Advance(TimeSpan.FromMinutes(5));
        scheduler.Tick();

        Assert.Equal(TriggerKind.Snooze, fired[^1].Kind);
        Assert.Equal(UrgencyLevel.High, fired[^1].EffectiveUrgency);
    }

    [Fact]
    public void Snoozing_pauses_the_ignored_clock()
    {
        // While snoozed the alert is off screen, so the ignore clock is paused.
        var alarm = Escalatable(UrgencyLevel.Normal, afterIgnored: TimeSpan.FromMinutes(10));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(30), out _));
        fired.Clear();

        // 20 min snoozed: past 10 min, but paused, so nothing happens.
        clock.Advance(TimeSpan.FromMinutes(20));
        scheduler.Tick();
        Assert.Empty(fired);
    }

    [Fact]
    public void New_occurrence_restarts_escalation_from_the_base_level()
    {
        var alarm = Escalatable(UrgencyLevel.Normal, afterIgnored: TimeSpan.FromMinutes(10));
        var (scheduler, clock, fired) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        clock.Advance(TimeSpan.FromMinutes(10));
        scheduler.Tick(); // rose to High
        Assert.Equal(UrgencyLevel.High, fired[^1].EffectiveUrgency);
        fired.Clear();

        // Next day: starts over at Normal.
        clock.SetTo(Utc(2026, 6, 11, 7, 0));
        scheduler.Tick();

        Assert.Equal(UrgencyLevel.Normal, fired.Single(d => d.Kind == TriggerKind.OnTime).EffectiveUrgency);
    }

    [Fact]
    public void Escalating_to_critical_applies_criticals_snooze_limit()
    {
        // After escalating to Critical, snooze follows Critical's 1x limit.
        var alarm = Escalatable(UrgencyLevel.Normal, afterIgnored: TimeSpan.FromMinutes(5));
        var (scheduler, clock, _) = Build(Utc(2026, 6, 10, 6, 0), alarm);

        clock.Advance(TimeSpan.FromHours(1));
        scheduler.Tick();
        clock.Advance(TimeSpan.FromMinutes(5));
        scheduler.Tick(); // High
        clock.Advance(TimeSpan.FromMinutes(5));
        scheduler.Tick(); // Critical

        Assert.True(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(2), out _));
        Assert.False(scheduler.TrySnooze(alarm.Id, TimeSpan.FromMinutes(2), out var refusal));
        Assert.Contains("uma vez", refusal);
    }
}
