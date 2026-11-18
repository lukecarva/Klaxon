using Klaxon.Core.Abstractions;
using Klaxon.Core.Localization;

namespace Klaxon.App;

/// <summary>Formats relative dates/times in the current language.</summary>
public static class TimeFormat
{
    /// <summary>Describes a future instant relative to now: "in 42 min", "today, 07:00", "tomorrow, …", or the
    /// short date (with the year when in another year).</summary>
    public static string Relative(DateTimeOffset when, ISystemClock clock)
    {
        var now = clock.Now;
        var remaining = when - now;

        if (remaining < TimeSpan.FromHours(1))
        {
            return Loc.Format("Rel_InMin", Math.Max(1, (int)remaining.TotalMinutes));
        }

        var local = TimeZoneInfo.ConvertTime(when, clock.LocalTimeZone);
        var today = TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).Date;
        var day = local.Date;
        var time = local.ToString("HH:mm", Loc.Culture);

        if (day == today)
        {
            return Loc.Format("Rel_Today", time);
        }

        if (day == today.AddDays(1))
        {
            return Loc.Format("Rel_Tomorrow", time);
        }

        var pattern = day.Year == today.Year ? Loc.Get("Fmt_DateShort") : Loc.Get("Fmt_DateLong");
        return $"{local.ToString(pattern, Loc.Culture)}, {time}";
    }
}
