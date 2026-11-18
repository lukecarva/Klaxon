using System.IO;
using System.Windows;
using System.Windows.Threading;
using Klaxon.Core.Abstractions;
using Klaxon.Core.Localization;
using Klaxon.Core.Persistence;
using Klaxon.Core.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Klaxon.App.Services;

/// <summary>Connects the scheduler to the real world: the one-second tick and the Windows power/time
/// events.</summary>
public sealed class SchedulerHost(
    IAlarmScheduler scheduler,
    AlarmsService alarms,
    ISchedulerStateStore stateStore,
    IAlertPresenter presenter,
    TrayIconService tray,
    ISystemClock clock,
    ILogger<SchedulerHost> log) : IHostedService, IDisposable
{
    private readonly IAlarmScheduler _scheduler = scheduler;
    private readonly AlarmsService _alarms = alarms;
    private readonly ISchedulerStateStore _stateStore = stateStore;
    private readonly IAlertPresenter _presenter = presenter;
    private readonly TrayIconService _tray = tray;
    private readonly ISystemClock _clock = clock;
    private readonly ILogger<SchedulerHost> _log = log;

    private DispatcherTimer? _timer;
    private bool _hooked;
    private string? _lastStatus;

    /// <summary>Loads alarms, starts the tick, and hooks system events.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler.Triggered += OnTriggered;

        _alarms.Load();
        _alarms.Changed += OnAlarmsChanged;
        _scheduler.Reload(_alarms.Items);

        // Restore snoozes/escalations after Reload (which needs the alarms loaded), then persist on every later
        // change.
        _scheduler.RestoreState(_stateStore.Load());
        _scheduler.StateChanged += OnStateChanged;

        // One-second tick: cheap, and immune to hibernation.
        _timer = new DispatcherTimer(DispatcherPriority.Normal, Application.Current.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };

        _timer.Tick += (_, _) =>
        {
            _scheduler.Tick();
            UpdateTrayStatus();
        };

        _timer.Start();
        UpdateTrayStatus();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
        _hooked = true;

        _log.LogInformation(
            "Scheduler is up. Next fire: {Next}",
            _scheduler.NextFireTime?.ToString("dd/MM HH:mm:ss") ?? "none");

        return Task.CompletedTask;
    }

    /// <summary>Stops the tick and unhooks system events.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Stop();
        Unhook();
        return Task.CompletedTask;
    }

    /// <summary>Reloads the scheduler when the alarm set changes.</summary>
    private void OnAlarmsChanged(object? sender, EventArgs e)
    {
        _scheduler.Reload(_alarms.Items);
        UpdateTrayStatus();
    }

    /// <summary>Shows the alert for a fired alarm.</summary>
    private void OnTriggered(object? sender, AlarmTriggeredEventArgs e)
    {
        _presenter.Show(e);
    }

    /// <summary>Persists the scheduler state; a disk error must not crash the app.</summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        try
        {
            _stateStore.Save(_scheduler.CaptureState());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Could not save the scheduler state.");
        }
    }

    /// <summary>Updates the tray tooltip only when the text actually changes.</summary>
    private void UpdateTrayStatus()
    {
        var next = _scheduler.NextFireTime;

        var text = next is null
            ? Loc.Format("Tray_Idle", Loc.Get("App_Name"))
            : Loc.Format("Status_NextPrefix", TimeFormat.Relative(next.Value, _clock));

        if (text == _lastStatus)
        {
            return;
        }

        _lastStatus = text;
        _tray.SetStatus(text);
    }

    /// <summary>On resume, ticks so catch-up runs for what came due while asleep.</summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _log.LogInformation("PC resumed. Evaluating what came due while asleep.");

        Application.Current.Dispatcher.BeginInvoke(() => _scheduler.Tick());
    }

    /// <summary>On a clock/zone change, reloads instead of firing a burst.</summary>
    private void OnTimeChanged(object? sender, EventArgs e)
    {
        _log.LogWarning("System clock or time zone changed. Recomputing the schedule.");

        Application.Current.Dispatcher.BeginInvoke(() => _scheduler.Reload(_alarms.Items));
    }

    /// <summary>Unsubscribes from the static SystemEvents and service events.</summary>
    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        _alarms.Changed -= OnAlarmsChanged;
        _scheduler.Triggered -= OnTriggered;
        _scheduler.StateChanged -= OnStateChanged;
        _hooked = false;
    }

    public void Dispose()
    {
        _timer?.Stop();
        Unhook();
    }
}
