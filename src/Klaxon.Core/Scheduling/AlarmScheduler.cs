using Klaxon.Core.Abstractions;
using Klaxon.Core.Localization;
using Klaxon.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klaxon.Core.Scheduling;

/// <summary>Schedules alarms and raises an event when one is due.</summary>
public interface IAlarmScheduler
{
    /// <summary>Raised when an alarm fires.</summary>
    event EventHandler<AlarmTriggeredEventArgs>? Triggered;

    /// <summary>Raised when the snooze/escalation state changes and should be persisted.</summary>
    event EventHandler? StateChanged;

    /// <summary>Next expected firing, snoozes included. Null = nothing scheduled.</summary>
    DateTimeOffset? NextFireTime { get; }

    /// <summary>Replaces the alarm set and recomputes everything.</summary>
    void Reload(IEnumerable<Alarm> alarms);

    /// <summary>Evaluates the clock now and fires whatever is due.</summary>
    void Tick();

    /// <summary>Tries to snooze an alarm; on failure returns a reason.</summary>
    bool TrySnooze(Guid alarmId, TimeSpan delay, out string? refusal);

    /// <summary>Ends the alert: cancels a pending snooze and clears the counter.</summary>
    void Dismiss(Guid alarmId);

    /// <summary>How many times the alarm has been snoozed.</summary>
    int SnoozeCountFor(Guid alarmId);

    /// <summary>Snapshots the current snooze/escalation state for persistence.</summary>
    SchedulerState CaptureState();

    /// <summary>Restores a saved state; must be called after <see cref="Reload"/>.</summary>
    void RestoreState(SchedulerState state);
}

/// <summary>Compares the wall clock on every <see cref="Tick"/> instead of using long timers, so it survives
/// sleep, time-zone and clock changes.</summary>
public sealed class AlarmScheduler : IAlarmScheduler
{
    /// <summary>Up to this lateness fires as on-time; beyond it counts as missed.</summary>
    public static readonly TimeSpan DefaultCatchUpWindow = TimeSpan.FromMinutes(15);

    private readonly ISystemClock _clock;
    private readonly ILogger<AlarmScheduler> _log;
    private readonly object _gate = new();

    private readonly Dictionary<Guid, Alarm> _alarms = [];
    private readonly Dictionary<Guid, DateTimeOffset> _next = [];
    private readonly Dictionary<Guid, DateTimeOffset> _snoozes = [];
    private readonly Dictionary<Guid, int> _snoozeCounts = [];

    /// <summary>Escalation state per alarm with an alert in progress.</summary>
    private readonly Dictionary<Guid, EscalationState> _escalations = [];

    private DateTimeOffset _lastTick;

    /// <summary>An alarm's in-progress escalation; lives until dismissed or the next occurrence.</summary>
    private sealed class EscalationState
    {
        /// <summary>The policy driving the escalation.</summary>
        public required EscalationPolicy Policy { get; init; }

        /// <summary>Current effective level; rises each step.</summary>
        public required UrgencyLevel Level { get; set; }

        /// <summary>When it rises for being ignored. Null while paused or at ceiling.</summary>
        public DateTimeOffset? IgnoreDeadline { get; set; }
    }

    public AlarmScheduler(ISystemClock clock, ILogger<AlarmScheduler>? log = null)
    {
        _clock = clock;
        _log = log ?? NullLogger<AlarmScheduler>.Instance;
        _lastTick = _clock.Now;
    }

    /// <summary>Lateness threshold between on-time and missed.</summary>
    public TimeSpan CatchUpWindow { get; init; } = DefaultCatchUpWindow;

    public event EventHandler<AlarmTriggeredEventArgs>? Triggered;

    public event EventHandler? StateChanged;

    /// <summary>Set under the lock whenever persistent state changes; drained after the lock.</summary>
    private bool _stateDirty;

    /// <summary>Raises <see cref="StateChanged"/> once if state changed since the last drain.</summary>
    private void RaiseStateChangedIfDirty()
    {
        bool changed;
        lock (_gate)
        {
            changed = _stateDirty;
            _stateDirty = false;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DateTimeOffset? NextFireTime
    {
        get
        {
            lock (_gate)
            {
                var candidates = _next.Values.Concat(_snoozes.Values);
                return candidates.Any() ? candidates.Min() : null;
            }
        }
    }

    public void Reload(IEnumerable<Alarm> alarms)
    {
        lock (_gate)
        {
            var now = _clock.Now;

            _alarms.Clear();
            foreach (var alarm in alarms.Where(a => a.IsEnabled))
            {
                _alarms[alarm.Id] = alarm;
            }

            // Drop snoozes, counters and escalations of alarms that are gone.
            foreach (var orphan in _snoozes.Keys.Where(id => !_alarms.ContainsKey(id)).ToList())
            {
                _snoozes.Remove(orphan);
                _stateDirty = true;
            }

            foreach (var orphan in _snoozeCounts.Keys.Where(id => !_alarms.ContainsKey(id)).ToList())
            {
                _snoozeCounts.Remove(orphan);
                _stateDirty = true;
            }

            foreach (var orphan in _escalations.Keys.Where(id => !_alarms.ContainsKey(id)).ToList())
            {
                _escalations.Remove(orphan);
                _stateDirty = true;
            }

            RecomputeAll(now);
        }

        RaiseStateChangedIfDirty();
    }

    public void Tick()
    {
        List<AlarmTriggeredEventArgs> fired;

        lock (_gate)
        {
            var now = _clock.Now;

            if (now < _lastTick)
            {
                _log.LogWarning(
                    "Relógio andou para trás ({Anterior:o} -> {Agora:o}). Recalculando todas as ocorrências.",
                    _lastTick,
                    now);

                RecomputeAll(now);
            }

            fired = Collect(now);
            _lastTick = now;
        }

        // Raise outside the lock: a handler may open a window or call Reload.
        foreach (var trigger in fired)
        {
            Triggered?.Invoke(this, trigger);
        }

        RaiseStateChangedIfDirty();
    }

    public bool TrySnooze(Guid alarmId, TimeSpan delay, out string? refusal)
    {
        bool success;

        lock (_gate)
        {
            success = TrySnoozeLocked(alarmId, delay, out refusal);
        }

        // Persist outside the lock so the handler's disk write doesn't block ticks.
        if (success)
        {
            RaiseStateChangedIfDirty();
        }

        return success;
    }

    /// <summary>Snooze logic, run under the lock.</summary>
    private bool TrySnoozeLocked(Guid alarmId, TimeSpan delay, out string? refusal)
    {
        if (!_alarms.TryGetValue(alarmId, out var alarm))
        {
            refusal = Loc.Get("Snooze_NotFound");
            return false;
        }

        // Snooze policy follows the effective (possibly escalated) level.
        var level = _escalations.TryGetValue(alarmId, out var esc) ? esc.Level : alarm.Urgency;
        var profile = UrgencyProfiles.Get(level);
        var policy = profile.Snooze;

        if (!policy.IsEnabled)
        {
            refusal = Loc.Format("Snooze_NotAllowed", Loc.UrgencyName(level));
            return false;
        }

        var used = _snoozeCounts.GetValueOrDefault(alarmId);
        if (used >= policy.MaxCount)
        {
            refusal = policy.MaxCount == 1
                ? Loc.Get("Snooze_Once")
                : Loc.Format("Snooze_Max", policy.MaxCount);

            return false;
        }

        _snoozeCounts[alarmId] = used + 1;
        _snoozes[alarmId] = _clock.Now + delay;
        _stateDirty = true;

        if (esc is not null)
        {
            // Snoozing hides the alert, so the "ignored" clock pauses.
            esc.IgnoreDeadline = null;

            if (esc.Policy.AfterSnoozes is { } max && used + 1 >= max)
            {
                var raised = Bump(esc.Level, esc.Policy.Ceiling);
                if (raised != esc.Level)
                {
                    _log.LogInformation(
                        "Alarme {Titulo} subiu para {Nivel} por adiamento.",
                        alarm.Title,
                        raised);
                }

                esc.Level = raised;
            }
        }

        _log.LogInformation(
            "Alarme {Titulo} adiado por {Minutos} min ({Usados}/{Max}).",
            alarm.Title,
            delay.TotalMinutes,
            used + 1,
            policy.MaxCount);

        refusal = null;
        return true;
    }

    public void Dismiss(Guid alarmId)
    {
        lock (_gate)
        {
            var removed = _snoozes.Remove(alarmId);
            removed |= _snoozeCounts.Remove(alarmId);
            removed |= _escalations.Remove(alarmId);

            if (removed)
            {
                _stateDirty = true;
            }
        }

        RaiseStateChangedIfDirty();
    }

    /// <summary>Rises one level, capped at the ceiling and Critical.</summary>
    private static UrgencyLevel Bump(UrgencyLevel level, UrgencyLevel ceiling)
    {
        var next = (UrgencyLevel)((int)level + 1);

        if (next > ceiling)
        {
            next = ceiling;
        }

        return next > UrgencyLevel.Critical ? UrgencyLevel.Critical : next;
    }

    public int SnoozeCountFor(Guid alarmId)
    {
        lock (_gate)
        {
            return _snoozeCounts.GetValueOrDefault(alarmId);
        }
    }

    public SchedulerState CaptureState()
    {
        lock (_gate)
        {
            var ids = _snoozeCounts.Keys.Union(_snoozes.Keys);

            var snoozes = ids
                .Select(id => new SnoozeState(
                    id,
                    _snoozeCounts.GetValueOrDefault(id),
                    _snoozes.TryGetValue(id, out var at) ? at : null))
                .ToList();

            var escalations = _escalations
                .Select(kv => new EscalationSnapshot(kv.Key, kv.Value.Level, kv.Value.IgnoreDeadline))
                .ToList();

            return new SchedulerState { Snoozes = snoozes, Escalations = escalations };
        }
    }

    public void RestoreState(SchedulerState state)
    {
        lock (_gate)
        {
            foreach (var s in state.Snoozes)
            {
                // Skip state for alarms that no longer exist.
                if (!_alarms.ContainsKey(s.AlarmId))
                {
                    continue;
                }

                if (s.Count > 0)
                {
                    _snoozeCounts[s.AlarmId] = s.Count;
                }

                if (s.DueAt is { } at)
                {
                    _snoozes[s.AlarmId] = at;
                }
            }

            foreach (var e in state.Escalations)
            {
                // Re-attach the policy from the alarm; drop it if the alarm or its policy is gone.
                if (!_alarms.TryGetValue(e.AlarmId, out var alarm) || alarm.Escalation is not { } policy)
                {
                    continue;
                }

                _escalations[e.AlarmId] = new EscalationState
                {
                    Policy = policy,
                    Level = e.Level,
                    IgnoreDeadline = e.IgnoreDeadline,
                };
            }
        }
    }

    /// <summary>Recomputes the next occurrence of every alarm.</summary>
    private void RecomputeAll(DateTimeOffset now)
    {
        var zone = _clock.LocalTimeZone;

        _next.Clear();

        foreach (var alarm in _alarms.Values)
        {
            var next = alarm.Schedule.NextOccurrenceAfter(now, zone);
            if (next is not null)
            {
                _next[alarm.Id] = next.Value;
            }
        }
    }

    /// <summary>Collects everything due now: snoozes, escalations and occurrences.</summary>
    private List<AlarmTriggeredEventArgs> Collect(DateTimeOffset now)
    {
        var fired = new List<AlarmTriggeredEventArgs>();
        var zone = _clock.LocalTimeZone;

        foreach (var (id, at) in _snoozes.Where(kv => kv.Value <= now).ToList())
        {
            _snoozes.Remove(id);
            _stateDirty = true;

            if (_alarms.TryGetValue(id, out var alarm))
            {
                // Alert is back on screen: the "ignored" clock restarts from now.
                var level = alarm.Urgency;
                if (_escalations.TryGetValue(id, out var esc))
                {
                    level = esc.Level;
                    esc.IgnoreDeadline = esc.Policy.AfterIgnoredFor is { } d && esc.Level < esc.Policy.Ceiling
                        ? now + d
                        : null;
                }

                fired.Add(new AlarmTriggeredEventArgs
                {
                    Alarm = alarm,
                    ScheduledFor = at,
                    FiredAt = now,
                    Kind = TriggerKind.Snooze,
                    EffectiveUrgency = level,
                });
            }
        }

        fired.AddRange(CollectEscalations(now));

        foreach (var (id, _) in _next.Where(kv => kv.Value <= now).ToList())
        {
            var alarm = _alarms[id];

            // Advance to the future, counting how many occurrences were skipped.
            var due = _next[id];
            var occurrences = 0;
            DateTimeOffset? cursor = due;

            while (cursor is not null && cursor.Value <= now)
            {
                due = cursor.Value;
                occurrences++;
                cursor = alarm.Schedule.NextOccurrenceAfter(cursor.Value, zone);

                if (occurrences > 100_000)
                {
                    _log.LogError("Agenda de {Titulo} não avança. Desagendando.", alarm.Title);
                    cursor = null;
                }
            }

            if (cursor is null)
            {
                _next.Remove(id);
            }
            else
            {
                _next[id] = cursor.Value;
            }

            // New occurrence: reset the snooze budget and restart escalation.
            _snoozeCounts.Remove(id);
            ResetEscalation(id, alarm, now);
            _stateDirty = true;

            var lateness = now - due;

            fired.Add(new AlarmTriggeredEventArgs
            {
                Alarm = alarm,
                ScheduledFor = due,
                FiredAt = now,
                Kind = lateness <= CatchUpWindow ? TriggerKind.OnTime : TriggerKind.Missed,
                SkippedOccurrences = occurrences - 1,
                EffectiveUrgency = alarm.Urgency,
            });
        }

        return fired;
    }

    /// <summary>Arms (or clears) the escalation state for a new occurrence.</summary>
    private void ResetEscalation(Guid id, Alarm alarm, DateTimeOffset now)
    {
        if (alarm.Escalation is not { } policy)
        {
            _escalations.Remove(id);
            return;
        }

        _escalations[id] = new EscalationState
        {
            Policy = policy,
            Level = alarm.Urgency,
            IgnoreDeadline = policy.AfterIgnoredFor is { } d && alarm.Urgency < policy.Ceiling
                ? now + d
                : null,
        };
    }

    /// <summary>Re-shows, one level higher, alarms whose "ignored" deadline passed.</summary>
    private List<AlarmTriggeredEventArgs> CollectEscalations(DateTimeOffset now)
    {
        var fired = new List<AlarmTriggeredEventArgs>();

        var dueEscalations = _escalations
            .Where(kv => kv.Value.IgnoreDeadline is { } dl && dl <= now)
            .ToList();

        // Every branch below mutates escalation state, so persist afterwards.
        if (dueEscalations.Count > 0)
        {
            _stateDirty = true;
        }

        foreach (var (id, esc) in dueEscalations)
        {
            if (!_alarms.TryGetValue(id, out var alarm))
            {
                _escalations.Remove(id);
                continue;
            }

            var raised = Bump(esc.Level, esc.Policy.Ceiling);

            if (raised == esc.Level)
            {
                // Already at ceiling: stop counting.
                esc.IgnoreDeadline = null;
                continue;
            }

            esc.Level = raised;
            esc.IgnoreDeadline = esc.Policy.AfterIgnoredFor is { } d && raised < esc.Policy.Ceiling
                ? now + d
                : null;

            _log.LogInformation("Alarme {Titulo} subiu para {Nivel} por ficar ignorado.", alarm.Title, raised);

            fired.Add(new AlarmTriggeredEventArgs
            {
                Alarm = alarm,
                ScheduledFor = now,
                FiredAt = now,
                Kind = TriggerKind.Escalation,
                EffectiveUrgency = raised,
            });
        }

        return fired;
    }
}
