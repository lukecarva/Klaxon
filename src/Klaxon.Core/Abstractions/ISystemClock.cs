namespace Klaxon.Core.Abstractions;

/// <summary>Provides the current time to the domain.</summary>
public interface ISystemClock
{
    /// <summary>Current instant, with local offset.</summary>
    DateTimeOffset Now { get; }

    /// <summary>Local time zone.</summary>
    TimeZoneInfo LocalTimeZone { get; }
}

/// <summary>System clock backed by Windows.</summary>
public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}
