using Klaxon.Core.Abstractions;

namespace Klaxon.Core.Tests;

/// <summary>Test-controlled clock, to simulate the passage of time.</summary>
public sealed class FakeClock(DateTimeOffset start, TimeZoneInfo? zone = null) : ISystemClock
{
    public DateTimeOffset Now { get; private set; } = start;

    public TimeZoneInfo LocalTimeZone { get; } = zone ?? TimeZoneInfo.Utc;

    /// <summary>Advances the clock forward.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), "Use SetTo para voltar no tempo.");
        }

        Now += by;
    }

    /// <summary>Jumps to an arbitrary instant, even backwards.</summary>
    public void SetTo(DateTimeOffset instant) => Now = instant;
}
