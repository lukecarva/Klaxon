namespace Klaxon.Core.Tests;

/// <summary>Hand-built time zones with explicit rules, for deterministic DST tests.</summary>
public static class TestZones
{
    /// <summary>Southern-hemisphere zone: −03:00, +1h DST between Oct 15 and Feb 15.</summary>
    public static TimeZoneInfo SouthernDst { get; } = BuildSouthernDst();

    private static TimeZoneInfo BuildSouthernDst()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            timeOfDay: new DateTime(1, 1, 1, 0, 0, 0),
            month: 10,
            day: 15);

        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            timeOfDay: new DateTime(1, 1, 1, 0, 0, 0),
            month: 2,
            day: 15);

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            dateStart: DateTime.MinValue.Date,
            dateEnd: DateTime.MaxValue.Date,
            daylightDelta: TimeSpan.FromHours(1),
            daylightTransitionStart: start,
            daylightTransitionEnd: end);

        return TimeZoneInfo.CreateCustomTimeZone(
            id: "Test/DaylightSaving",
            baseUtcOffset: TimeSpan.FromHours(-3),
            displayName: "Test (daylight saving)",
            standardDisplayName: "Test Standard",
            daylightDisplayName: "Test Daylight",
            adjustmentRules: [rule]);
    }
}
