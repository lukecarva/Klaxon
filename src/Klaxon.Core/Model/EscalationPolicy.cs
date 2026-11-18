namespace Klaxon.Core.Model;

/// <summary>Makes an alarm rise a level when ignored, up to <see cref="Ceiling"/>.</summary>
public sealed record EscalationPolicy
{
    /// <summary>Rise a level after this long undismissed. Null = trigger unused.</summary>
    public TimeSpan? AfterIgnoredFor { get; init; }

    /// <summary>Rise a level after this many snoozes. Null = trigger unused.</summary>
    public int? AfterSnoozes { get; init; }

    /// <summary>Highest level the escalation reaches.</summary>
    public UrgencyLevel Ceiling { get; init; } = UrgencyLevel.Critical;

    /// <summary>Preset offered in the UI: 10 min ignored, or 2 snoozes.</summary>
    public static EscalationPolicy Default { get; } = new()
    {
        AfterIgnoredFor = TimeSpan.FromMinutes(10),
        AfterSnoozes = 2,
    };
}
