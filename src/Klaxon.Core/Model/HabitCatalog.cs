using Klaxon.Core.Localization;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Model;

/// <summary>A one-click daily reminder preset.</param>
public sealed record HabitDefinition(string Key, string Emoji, int DefaultMinutes, UrgencyLevel Urgency)
{
    /// <summary>Display name in the current language.</summary>
    public string Name => Loc.Get($"Habit_{Key}_Name");

    /// <summary>Short description in the current language.</summary>
    public string Note => Loc.Get($"Habit_{Key}_Note");
}

/// <summary>The built-in daily reminders.</summary>
public static class HabitCatalog
{
    /// <summary>Reminders stay quiet outside this daily range.</summary>
    public static readonly TimeOnly WindowFrom = new(8, 0);
    public static readonly TimeOnly WindowTo = new(22, 0);

    /// <summary>Idle this long means you're away; the reminder is skipped.</summary>
    public static readonly TimeSpan SkipIfIdleFor = TimeSpan.FromMinutes(5);

    /// <summary>All presets, in display order.</summary>
    public static IReadOnlyList<HabitDefinition> All { get; } =
    [
        new("water", "💧", 45, UrgencyLevel.Whisper),
        new("standup", "🧍", 60, UrgencyLevel.Normal),
        new("eyes", "👀", 20, UrgencyLevel.Whisper),
        new("stretch", "🤸", 90, UrgencyLevel.Normal),
    ];

    /// <summary>Finds a preset by key.</summary>
    public static HabitDefinition? Find(string key) => All.FirstOrDefault(h => h.Key == key);

    /// <summary>Builds a habit's alarm at the given interval, keeping the running cycle's anchor and id when
    /// possible.</summary>
    public static Alarm BuildAlarm(
        HabitDefinition habit,
        int minutes,
        DateTimeOffset now,
        Alarm? existing = null)
    {
        var interval = TimeSpan.FromMinutes(minutes);
        var anchor = existing?.Schedule is IntervalSchedule antigo && antigo.Every == interval
            ? antigo.Anchor
            : now;

        return new Alarm
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Title = habit.Name,
            Schedule = new IntervalSchedule(interval, anchor, WindowFrom, WindowTo),
            Urgency = habit.Urgency,
            SkipIfIdleFor = SkipIfIdleFor,
            HabitKey = habit.Key,
        };
    }
}
