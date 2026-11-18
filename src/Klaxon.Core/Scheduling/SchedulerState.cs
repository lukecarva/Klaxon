using Klaxon.Core.Model;

namespace Klaxon.Core.Scheduling;

/// <summary>Runtime state that must survive a restart: pending snoozes and in-progress escalations.</summary>
public sealed record SchedulerState
{
    /// <summary>Snooze budget and pending wake time per alarm.</summary>
    public List<SnoozeState> Snoozes { get; init; } = [];

    /// <summary>Escalations in progress per alarm.</summary>
    public List<EscalationSnapshot> Escalations { get; init; } = [];

    /// <summary>Whether there is nothing to persist.</summary>
    public bool IsEmpty => Snoozes.Count == 0 && Escalations.Count == 0;
}

/// <summary>An alarm's snooze budget, with an optional pending wake time.</summary>
public sealed record SnoozeState(Guid AlarmId, int Count, DateTimeOffset? DueAt);

/// <summary>An alarm's in-progress escalation; the policy is re-attached from the alarm on restore.</summary>
public sealed record EscalationSnapshot(Guid AlarmId, UrgencyLevel Level, DateTimeOffset? IgnoreDeadline);
