using System.Windows;
using Klaxon.App.Audio;
using Klaxon.App.Interop;
using Klaxon.App.ViewModels;
using Klaxon.App.Views;
using Klaxon.Core.Abstractions;
using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace Klaxon.App.Services;

/// <summary>Chooses and builds the alert window from the urgency profile.</summary>
public sealed class WpfAlertPresenter(
    IAlarmScheduler scheduler,
    TrayIconService tray,
    IIdleDetector idle,
    ILogger<WpfAlertPresenter> log) : IAlertPresenter
{
    private readonly IAlarmScheduler _scheduler = scheduler;
    private readonly TrayIconService _tray = tray;
    private readonly IIdleDetector _idle = idle;
    private readonly ILogger<WpfAlertPresenter> _log = log;

    /// <summary>On-screen alerts by alarm; prevents two alerts for one alarm.</summary>
    private readonly Dictionary<Guid, AlertSession> _openAlerts = [];

    /// <summary>Open corner cards, bottom-most first, kept stacked without overlap.</summary>
    private readonly List<AlertWindow> _cornerCards = [];

    /// <summary>Presents a triggered alarm (skips, toast, window or escalation).</summary>
    public void Show(AlarmTriggeredEventArgs trigger)
    {
        if (ShouldSkipForAbsence(trigger, out var idleFor))
        {
            _log.LogInformation(
                "Alarm {Title} skipped: keyboard and mouse idle for {Idle}.",
                trigger.Alarm.Title,
                idleFor);

            _scheduler.Dismiss(trigger.Alarm.Id);
            return;
        }

        var resolved = Resolve(trigger);

        if (resolved is null)
        {
            _log.LogInformation(
                "Alarm {Title} missed and discarded per the {Level} level.",
                trigger.Alarm.Title,
                trigger.EffectiveUrgency);

            return;
        }

        var (mode, sound) = resolved.Value;

        if (_openAlerts.TryGetValue(trigger.Alarm.Id, out var existing))
        {
            if (trigger.Kind != TriggerKind.Escalation)
            {
                _log.LogDebug("Alert for {Title} is already on screen.", trigger.Alarm.Title);
                return;
            }

            // Escalation replaces the current window with a more intrusive one.
            _log.LogInformation("Alert for {Title} replaced by the escalated version.", trigger.Alarm.Title);
            existing.Window.Close();
        }

        _log.LogInformation(
            "Alert: {Title} [{Level}/{Mode}] reason={Reason}.",
            trigger.Alarm.Title,
            trigger.EffectiveUrgency,
            mode.ToString(),
            trigger.Kind.ToString());

        if (mode == PresentationMode.Toast)
        {
            _tray.ShowBalloon(trigger.Alarm.Title, trigger.Alarm.Message ?? trigger.Alarm.Schedule.Describe());
            _scheduler.Dismiss(trigger.Alarm.Id);
            return;
        }

        ShowWindow(trigger, mode, sound);
    }

    /// <summary>Opens the alert window and starts its sound.</summary>
    private void ShowWindow(AlarmTriggeredEventArgs trigger, PresentationMode mode, SoundSpec sound)
    {
        var viewModel = new AlertViewModel(trigger, mode, _scheduler);
        var window = new AlertWindow(viewModel);
        var session = new AlertSession(window, new AlarmAudioPlayer(_log));

        _openAlerts[trigger.Alarm.Id] = session;
        window.Closed += (_, _) => CloseSession(trigger.Alarm.Id);

        if (mode == PresentationMode.Fullscreen)
        {
            ShowFullscreen(window, viewModel, session);
        }
        else
        {
            // The content sizes the card; a fixed height would clip the buttons.
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.MinWidth = 380;
            window.MaxWidth = 460;

            window.WindowStartupLocation = mode == PresentationMode.Modal
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.Manual;

            window.ShowActivated = mode != PresentationMode.Corner;
            window.Show();

            if (mode == PresentationMode.Corner)
            {
                // Stack above any card already in the corner instead of covering it.
                _cornerCards.Add(window);
                RestackCorners();
            }
        }

        session.Audio.Play(sound);
    }

    /// <summary>Lays the open corner cards out upward from the bottom, without overlap.</summary>
    private void RestackCorners()
    {
        const int gap = 8;
        var offset = 0;

        foreach (var card in _cornerCards)
        {
            card.UpdateLayout();
            Win32Windows.PlaceInCorner(card, offset);
            offset += Win32Windows.PhysicalHeight(card) + gap;
        }
    }

    /// <summary>Covers every monitor: main window plus one overlay per extra screen.</summary>
    private static void ShowFullscreen(AlertWindow window, AlertViewModel viewModel, AlertSession session)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var primary = System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];

        window.Show();
        Win32Windows.PlacePhysical(window, primary.Bounds, activate: true);

        foreach (var screen in screens.Where(t => !t.Equals(primary)))
        {
            var overlay = new OverlayWindow(viewModel);
            overlay.Show();
            Win32Windows.PlacePhysical(overlay, screen.Bounds, activate: false);
            session.Overlays.Add(overlay);
        }
    }

    /// <summary>Whether to skip an on-time alert because the user is away.</summary>
    private bool ShouldSkipForAbsence(AlarmTriggeredEventArgs trigger, out TimeSpan idleFor)
    {
        idleFor = TimeSpan.Zero;

        if (trigger.Kind != TriggerKind.OnTime || trigger.Alarm.SkipIfIdleFor is not { } limit)
        {
            return false;
        }

        idleFor = _idle.IdleFor;
        return idleFor >= limit;
    }

    /// <summary>Resolves how to show now, downgrading missed alarms per policy.</summary>
    private static (PresentationMode Mode, SoundSpec Sound)? Resolve(AlarmTriggeredEventArgs trigger)
    {
        var profile = trigger.EffectiveProfile;

        if (trigger.Kind != TriggerKind.Missed)
        {
            return (profile.Presentation, trigger.EffectiveSound);
        }

        return profile.WhenAway switch
        {
            MissedAlarmBehavior.Discard => null,

            // A missed alarm returns as a silent corner card.
            MissedAlarmBehavior.ShowOnReturn => (PresentationMode.Corner, SoundSpec.Silent),

            MissedAlarmBehavior.FireOnReturn => (profile.Presentation, trigger.EffectiveSound),

            _ => null,
        };
    }

    /// <summary>Closes an alert session: stops sound and closes overlays.</summary>
    private void CloseSession(Guid alarmId)
    {
        if (!_openAlerts.Remove(alarmId, out var session))
        {
            return;
        }

        session.Audio.Dispose();

        foreach (var overlay in session.Overlays)
        {
            overlay.Close();
        }

        // Reflow the remaining corner cards so no gap is left behind.
        if (_cornerCards.Remove(session.Window))
        {
            RestackCorners();
        }
    }

    /// <summary>An open alert: its window, sound and extra-monitor overlays.</summary>
    private sealed record AlertSession(AlertWindow Window, AlarmAudioPlayer Audio)
    {
        public List<OverlayWindow> Overlays { get; } = [];
    }
}
