using System.Text.Json.Serialization;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Model;

/// <summary>An alarm: what to say, when, and how insistently.</summary>
public sealed record Alarm
{
    /// <summary>Unique identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Shown large in the alert.</summary>
    public required string Title { get; init; }

    /// <summary>Optional detail below the title.</summary>
    public string? Message { get; init; }

    /// <summary>When the alarm fires.</summary>
    public required ISchedule Schedule { get; init; }

    /// <summary>Urgency level.</summary>
    public UrgencyLevel Urgency { get; init; } = UrgencyLevel.Normal;

    /// <summary>Overrides the level's sound with a file. Null = use the profile's.</summary>
    public string? CustomSoundPath { get; init; }

    /// <summary>Disabled alarms stay saved but are not scheduled.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Marks a daily-habit reminder and which one; null = a normal user alarm.</summary>
    public string? HabitKey { get; init; }

    /// <summary>Skips the on-time alert when keyboard/mouse have been idle at least this long.</summary>
    public TimeSpan? SkipIfIdleFor { get; init; }

    /// <summary>Makes the alarm escalate when ignored. Null = no escalation.</summary>
    public EscalationPolicy? Escalation { get; init; }

    /// <summary>Urgency profile for this alarm; not serialized (derived from <see cref="Urgency"/>).</summary>
    [JsonIgnore]
    public UrgencyProfile Profile => UrgencyProfiles.Get(Urgency);

    /// <summary>Effective sound: the profile's, with the user's file if any.</summary>
    [JsonIgnore]
    public SoundSpec EffectiveSound =>
        CustomSoundPath is null ? Profile.Sound : Profile.Sound with { FilePath = CustomSoundPath, IsSilent = false };

    /// <summary>Creates a new alarm with a fresh id.</summary>
    public static Alarm New(string title, ISchedule schedule, UrgencyLevel urgency = UrgencyLevel.Normal) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Schedule = schedule,
        Urgency = urgency,
    };
}
