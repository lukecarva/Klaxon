namespace Klaxon.Core.Scheduling;

/// <summary>Fires every N minutes, optionally only within a daily time range.</summary>
public sealed record IntervalSchedule(
    TimeSpan Every,
    DateTimeOffset Anchor,
    TimeOnly? ActiveFrom = null,
    TimeOnly? ActiveTo = null) : ISchedule
{
    /// <summary>Upper bound for the loop that seeks the next window opening.</summary>
    private const int MaxWindowHops = 400;

    /// <summary>Whether an active time range is set.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasWindow => ActiveFrom is not null && ActiveTo is not null;

    /// <summary>Next firing after <paramref name="from"/>, skipping outside the range.</summary>
    public DateTimeOffset? NextOccurrenceAfter(DateTimeOffset from, TimeZoneInfo zone)
    {
        if (Every <= TimeSpan.Zero)
        {
            return null;
        }

        var candidate = AlignToGrid(from);

        for (var hop = 0; hop < MaxWindowHops; hop++)
        {
            if (IsInWindow(candidate, zone))
            {
                return candidate;
            }

            var opening = NextWindowOpening(candidate, zone);
            if (opening is null)
            {
                return null;
            }

            candidate = AlignToGrid(opening.Value - TimeSpan.FromTicks(1));
        }

        return null;
    }

    /// <summary>First cycle instant strictly after <paramref name="from"/>.</summary>
    private DateTimeOffset AlignToGrid(DateTimeOffset from)
    {
        var elapsed = from - Anchor;
        var cycles = Math.Floor(elapsed / Every) + 1;

        var candidate = Anchor + (Every * cycles);

        while (candidate <= from)
        {
            candidate += Every;
        }

        return candidate;
    }

    /// <summary>Whether an instant falls inside the active range.</summary>
    private bool IsInWindow(DateTimeOffset instant, TimeZoneInfo zone)
    {
        if (!HasWindow)
        {
            return true;
        }

        var time = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

        // Range crossing midnight (22:00-06:00) needs the inverted test.
        return ActiveFrom!.Value <= ActiveTo!.Value
            ? time >= ActiveFrom.Value && time < ActiveTo.Value
            : time >= ActiveFrom.Value || time < ActiveTo.Value;
    }

    /// <summary>Next time the active range opens after <paramref name="from"/>.</summary>
    private DateTimeOffset? NextWindowOpening(DateTimeOffset from, TimeZoneInfo zone)
    {
        if (!HasWindow)
        {
            return null;
        }

        var date = WallClock.LocalDate(from, zone);

        for (var dayOffset = 0; dayOffset <= 2; dayOffset++)
        {
            var opening = WallClock.Resolve(date.AddDays(dayOffset).ToDateTime(ActiveFrom!.Value), zone);
            if (opening > from)
            {
                return opening;
            }
        }

        return null;
    }

    /// <summary>Short text, e.g. "Every 45 min, 09:00 to 18:00".</summary>
    public string Describe()
    {
        var text = Localization.Loc.Format("Sched_Every", FormatEvery());

        return HasWindow
            ? $"{text}, {ActiveFrom:HH\\:mm} {Localization.Loc.Get("Sched_WindowSep")} {ActiveTo:HH\\:mm}"
            : text;
    }

    /// <summary>Formats the interval as "45 min", "1h" or "1h30".</summary>
    private string FormatEvery()
    {
        if (Every < TimeSpan.FromHours(1))
        {
            return $"{Every.TotalMinutes:0} min";
        }

        var hours = (int)Every.TotalHours;
        var minutes = Every.Minutes;

        return minutes == 0 ? $"{hours}h" : $"{hours}h{minutes:00}";
    }
}
