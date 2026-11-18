namespace Klaxon.Core.Scheduling;

/// <summary>Resolves a wall-clock time into an absolute instant, handling DST.</summary>
internal static class WallClock
{
    /// <summary>Upper bound for the loop that skips a spring-forward gap.</summary>
    private const int MaxGapMinutes = 24 * 60;

    /// <summary>Converts a wall-clock time to an instant; on a nonexistent time fires at the end of the gap,
    /// on an ambiguous time picks the first pass.</summary>
    public static DateTimeOffset Resolve(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);

        var guard = 0;
        while (zone.IsInvalidTime(wall) && guard++ < MaxGapMinutes)
        {
            wall = wall.AddMinutes(1);
        }

        if (zone.IsAmbiguousTime(wall))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(wall);
            return new DateTimeOffset(wall, offsets.Max());
        }

        return new DateTimeOffset(wall, zone.GetUtcOffset(wall));
    }

    /// <summary>Local wall-clock date of an instant in the given zone.</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
