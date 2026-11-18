using System.Text.Json.Serialization;

namespace Klaxon.Core.Scheduling;

/// <summary>Days of the week as combinable flags.</summary>
[Flags]
public enum WeekDays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,

    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    Every = Weekdays | Weekend,
}

public static class WeekDaysExtensions
{
    /// <summary>Converts a day into its flag.</summary>
    public static WeekDays ToFlag(this DayOfWeek day) => (WeekDays)(1 << (int)day);

    /// <summary>Whether the set contains the given day.</summary>
    public static bool Includes(this WeekDays days, DayOfWeek day) => (days & day.ToFlag()) != 0;
}

/// <summary>A recurrence rule for an alarm.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(OneTimeSchedule), "once")]
[JsonDerivedType(typeof(DailySchedule), "daily")]
[JsonDerivedType(typeof(WeeklySchedule), "weekly")]
[JsonDerivedType(typeof(IntervalSchedule), "interval")]
public interface ISchedule
{
    /// <summary>Next occurrence strictly after <paramref name="from"/>, or null if none.</summary>
    DateTimeOffset? NextOccurrenceAfter(DateTimeOffset from, TimeZoneInfo zone);

    /// <summary>Short text for the alarm list, e.g. "Every day, 07:00".</summary>
    string Describe();
}
