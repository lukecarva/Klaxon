using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

public class HabitCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void All_habits_have_a_unique_key()
    {
        var keys = HabitCatalog.All.Select(h => h.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void BuildAlarm_marks_the_habit_key_and_is_interval_based()
    {
        var water = HabitCatalog.Find("water")!;

        var alarm = HabitCatalog.BuildAlarm(water, 45, Now);

        Assert.Equal("water", alarm.HabitKey);
        Assert.Equal(water.Urgency, alarm.Urgency);
        Assert.Equal(HabitCatalog.SkipIfIdleFor, alarm.SkipIfIdleFor);

        var schedule = Assert.IsType<IntervalSchedule>(alarm.Schedule);
        Assert.Equal(TimeSpan.FromMinutes(45), schedule.Every);
        Assert.Equal(HabitCatalog.WindowFrom, schedule.ActiveFrom);
        Assert.Equal(HabitCatalog.WindowTo, schedule.ActiveTo);
    }

    [Fact]
    public void Changing_only_the_interval_resets_the_cycle_but_keeps_the_Id()
    {
        var water = HabitCatalog.Find("water")!;
        var original = HabitCatalog.BuildAlarm(water, 45, Now);
        var anchor = ((IntervalSchedule)original.Schedule).Anchor;

        var later = Now.AddHours(3);
        var adjusted = HabitCatalog.BuildAlarm(water, 30, later, original);

        // Updates in place (same Id), but changing the interval resets the anchor.
        Assert.Equal(original.Id, adjusted.Id);
        Assert.Equal(TimeSpan.FromMinutes(30), ((IntervalSchedule)adjusted.Schedule).Every);
        Assert.NotEqual(anchor, ((IntervalSchedule)adjusted.Schedule).Anchor);
    }

    [Fact]
    public void Rebuilding_with_the_same_interval_preserves_the_anchor()
    {
        var water = HabitCatalog.Find("water")!;
        var original = HabitCatalog.BuildAlarm(water, 45, Now);
        var anchor = ((IntervalSchedule)original.Schedule).Anchor;

        var rebuilt = HabitCatalog.BuildAlarm(water, 45, Now.AddHours(3), original);

        Assert.Equal(anchor, ((IntervalSchedule)rebuilt.Schedule).Anchor);
    }
}
