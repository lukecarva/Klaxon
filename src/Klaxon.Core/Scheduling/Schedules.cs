using Klaxon.Core.Localization;

namespace Klaxon.Core.Scheduling;

/// <summary>A single firing at an absolute instant.</summary>
public sealed record OneTimeSchedule(DateTimeOffset At) : ISchedule
{
    /// <summary>The instant itself, if still in the future.</summary>
    public DateTimeOffset? NextOccurrenceAfter(DateTimeOffset from, TimeZoneInfo zone) =>
        At > from ? At : null;

    /// <summary>Localized date and time.</summary>
    public string Describe()
    {
        var local = At.ToLocalTime();
        var date = local.ToString(Loc.Get("Fmt_DateLong"), Loc.Culture);
        var time = local.ToString("HH:mm", Loc.Culture);
        return $"{date} {Loc.Get("Sched_At")} {time}";
    }
}

/// <summary>Every day at the same wall-clock time.</summary>
public sealed record DailySchedule(TimeOnly At) : ISchedule
{
    /// <summary>Next occurrence at the set time.</summary>
    public DateTimeOffset? NextOccurrenceAfter(DateTimeOffset from, TimeZoneInfo zone)
    {
        var date = WallClock.LocalDate(from, zone);

        // Extra days of slack: on a DST turn the same-day candidate may shift back.
        for (var offset = 0; offset <= 2; offset++)
        {
            var candidate = WallClock.Resolve(date.AddDays(offset).ToDateTime(At), zone);
            if (candidate > from)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Localized text.</summary>
    public string Describe() => Loc.Format("Sched_EveryDay", At.ToString("HH\\:mm", Loc.Culture));
}

/// <summary>On chosen weekdays, at the same wall-clock time.</summary>
public sealed record WeeklySchedule(WeekDays Days, TimeOnly At) : ISchedule
{
    /// <summary>Next occurrence on an enabled weekday.</summary>
    public DateTimeOffset? NextOccurrenceAfter(DateTimeOffset from, TimeZoneInfo zone)
    {
        if (Days == WeekDays.None)
        {
            return null;
        }

        var date = WallClock.LocalDate(from, zone);

        // Eight days cover the whole week even when today's slot has passed.
        for (var offset = 0; offset <= 8; offset++)
        {
            var day = date.AddDays(offset);
            if (!Days.Includes(day.DayOfWeek))
            {
                continue;
            }

            var candidate = WallClock.Resolve(day.ToDateTime(At), zone);
            if (candidate > from)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Localized text (special-cases weekdays/weekend/every day).</summary>
    public string Describe()
    {
        var time = At.ToString("HH\\:mm", Loc.Culture);

        if (Days == WeekDays.Every)
        {
            return Loc.Format("Sched_EveryDay", time);
        }

        if (Days == WeekDays.Weekdays)
        {
            return Loc.Format("Sched_Weekdays", time);
        }

        if (Days == WeekDays.Weekend)
        {
            return Loc.Format("Sched_Weekend", time);
        }

        var days = Enumerable.Range(0, 7)
            .Where(i => Days.Includes((DayOfWeek)i))
            .Select(i => Loc.Get($"Day_{i}"));

        return $"{string.Join(", ", days)}, {time}";
    }
}
