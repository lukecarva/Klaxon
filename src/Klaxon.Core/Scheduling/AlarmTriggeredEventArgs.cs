using Klaxon.Core.Model;

namespace Klaxon.Core.Scheduling;

/// <summary>Why an alert is showing now.</summary>
public enum TriggerKind
{
    /// <summary>On time, or within the catch-up window.</summary>
    OnTime,

    /// <summary>Should have fired earlier (PC asleep, off, or app not running).</summary>
    Missed,

    /// <summary>Returning from a snooze.</summary>
    Snooze,

    /// <summary>Re-shown one level higher because it was ignored.</summary>
    Escalation,
}

/// <summary>Details of an alarm that just fired.</summary>
public sealed class AlarmTriggeredEventArgs : EventArgs
{
    /// <summary>The alarm that fired.</summary>
    public required Alarm Alarm { get; init; }

    /// <summary>When it was scheduled for.</summary>
    public required DateTimeOffset ScheduledFor { get; init; }

    /// <summary>When the scheduler noticed.</summary>
    public required DateTimeOffset FiredAt { get; init; }

    /// <summary>Why it is showing.</summary>
    public required TriggerKind Kind { get; init; }

    /// <summary>Level to show now — the alarm's own, or higher after escalation.</summary>
    public required UrgencyLevel EffectiveUrgency { get; init; }

    /// <summary>Profile for <see cref="EffectiveUrgency"/>.</summary>
    public UrgencyProfile EffectiveProfile => UrgencyProfiles.Get(EffectiveUrgency);

    /// <summary>Sound at the current level, with the user's file if any.</summary>
    public SoundSpec EffectiveSound =>
        Alarm.CustomSoundPath is null
            ? EffectiveProfile.Sound
            : EffectiveProfile.Sound with { FilePath = Alarm.CustomSoundPath, IsSilent = false };

    /// <summary>Earlier missed occurrences folded into this one alert.</summary>
    public int SkippedOccurrences { get; init; }

    /// <summary>How late the alert is.</summary>
    public TimeSpan Delay => FiredAt - ScheduledFor;
}
