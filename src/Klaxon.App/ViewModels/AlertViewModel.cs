using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Klaxon.Core.Localization;
using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Klaxon.App.ViewModels;

/// <summary>A snooze choice shown on the alert.</summary>
public sealed record SnoozeOption(TimeSpan Duration, string Label);

/// <summary>View model for an alert window.</summary>
public sealed partial class AlertViewModel : ObservableObject, IDisposable
{
    private readonly IAlarmScheduler _scheduler;
    private readonly DispatcherTimer? _clockTimer;

    public AlertViewModel(AlarmTriggeredEventArgs trigger, PresentationMode mode, IAlarmScheduler scheduler)
    {
        _scheduler = scheduler;

        Trigger = trigger;
        Mode = mode;

        var profile = trigger.EffectiveProfile;

        SnoozeOptions = [.. profile.Snooze.Options.Select(d => new SnoozeOption(d, $"{d.TotalMinutes:0} min"))];
        DismissPhrase = profile.DismissPhrase ?? Loc.Get("Dismiss_DefaultPhrase");
        DismissMode = profile.Dismiss;
        UrgencyName = Loc.UrgencyName(profile.Level);

        AccentBrush = Application.Current?.TryFindResource($"Brush.Urgency.{profile.Level}") as Brush
                      ?? Brushes.OrangeRed;

        if (mode == PresentationMode.Fullscreen)
        {
            // Live clock, updated each second, for the full-screen overlay.
            NowText = DateTime.Now.ToString("HH:mm", Loc.Culture);
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => NowText = DateTime.Now.ToString("HH:mm", Loc.Culture);
            _clockTimer.Start();
        }
    }

    /// <summary>The trigger being shown.</summary>
    public AlarmTriggeredEventArgs Trigger { get; }

    /// <summary>Presentation mode of this alert.</summary>
    public PresentationMode Mode { get; }

    /// <summary>How the alert is dismissed.</summary>
    public DismissMode DismissMode { get; }

    /// <summary>Phrase required by type-to-dismiss.</summary>
    public string DismissPhrase { get; }

    /// <summary>Localized urgency name.</summary>
    public string UrgencyName { get; }

    /// <summary>Urgency accent color.</summary>
    public Brush AccentBrush { get; }

    /// <summary>Available snooze options.</summary>
    public IReadOnlyList<SnoozeOption> SnoozeOptions { get; }

    /// <summary>Alarm title.</summary>
    public string Title => Trigger.Alarm.Title;

    /// <summary>Optional detail.</summary>
    public string? Message => Trigger.Alarm.Message;

    /// <summary>Whether there is a detail message.</summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>Whether this is a missed alarm.</summary>
    public bool IsMissed => Trigger.Kind == TriggerKind.Missed;

    /// <summary>Whether snoozing is offered.</summary>
    public bool CanSnooze => SnoozeOptions.Count > 0;

    /// <summary>Context line: on time, snoozed, escalated, or missed.</summary>
    public string WhenText
    {
        get
        {
            var time = Trigger.ScheduledFor.ToLocalTime().ToString("HH:mm", Loc.Culture);

            if (Trigger.Kind == TriggerKind.Escalation)
            {
                return Loc.Format("Alert_When_Escalated", UrgencyName);
            }

            if (Trigger.Kind == TriggerKind.Snooze)
            {
                return Loc.Format("Alert_When_Snoozed", time);
            }

            if (!IsMissed)
            {
                return Loc.Format("Alert_When_Scheduled", time);
            }

            var text = Loc.Format("Alert_When_Missed", time, Humanize(Trigger.Delay));

            return Trigger.SkippedOccurrences > 0
                ? Loc.Format("Alert_When_MissedExtra", text, Trigger.SkippedOccurrences)
                : text;
        }
    }

    /// <summary>Big clock text on the full-screen overlay.</summary>
    [ObservableProperty]
    private string _nowText = string.Empty;

    /// <summary>Set when the scheduler refuses a snooze.</summary>
    [ObservableProperty]
    private string? _snoozeRefusal;

    /// <summary>0..1, driven by the hold-to-dismiss button.</summary>
    [ObservableProperty]
    private double _holdProgress;

    /// <summary>Raised when the window should close.</summary>
    public event Action? CloseRequested;

    /// <summary>Dismisses the alarm and closes.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        _scheduler.Dismiss(Trigger.Alarm.Id);
        CloseRequested?.Invoke();
    }

    /// <summary>Closes on timeout without dismissing, so escalation can continue.</summary>
    public void TimeoutClose() => CloseRequested?.Invoke();

    /// <summary>Snoozes for the chosen duration, or shows the refusal.</summary>
    [RelayCommand]
    private void Snooze(SnoozeOption? option)
    {
        var duration = option?.Duration ?? Trigger.EffectiveProfile.Snooze.DefaultOption;

        if (_scheduler.TrySnooze(Trigger.Alarm.Id, duration, out var refusal))
        {
            CloseRequested?.Invoke();
            return;
        }

        SnoozeRefusal = refusal;
    }

    /// <summary>Formats a delay as a short human-readable span.</summary>
    private static string Humanize(TimeSpan span)
    {
        if (span.TotalMinutes < 1)
        {
            return Loc.Get("Dur_LessThanMinute");
        }

        if (span.TotalHours < 1)
        {
            return Loc.Format("Dur_Min", (int)span.TotalMinutes);
        }

        if (span.TotalDays < 1)
        {
            return Loc.Format("Dur_HourMin", span.Hours, span.Minutes);
        }

        return Loc.Format("Dur_Days", (int)span.TotalDays);
    }

    public void Dispose() => _clockTimer?.Stop();
}
