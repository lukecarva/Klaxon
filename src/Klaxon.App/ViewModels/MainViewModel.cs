using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Threading;
using Klaxon.App.Services;
using Klaxon.Core.Abstractions;
using Klaxon.Core.Localization;
using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Klaxon.App.ViewModels;

/// <summary>View model of the main window: alarms list and daily habits.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AlarmsService _alarms;
    private readonly IAlarmScheduler _scheduler;
    private readonly AlarmDialogs _dialogs;
    private readonly StartupRegistrar _startup;
    private readonly ISystemClock _clock;
    private readonly ILogger<MainViewModel> _log;
    private readonly DispatcherTimer _refresh;

    public MainViewModel(
        AlarmsService alarms,
        IAlarmScheduler scheduler,
        AlarmDialogs dialogs,
        StartupRegistrar startup,
        ISystemClock clock,
        ILogger<MainViewModel> log)
    {
        _alarms = alarms;
        _scheduler = scheduler;
        _dialogs = dialogs;
        _startup = startup;
        _clock = clock;
        _log = log;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText = Loc.Format("Main_Version", version);

        _startWithWindows = _startup.IsEnabled;

        _alarms.Changed += (_, _) => Rebuild();
        Rebuild();

        // Refreshes the relative texts periodically; only runs while visible.
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _refresh.Tick += (_, _) => RefreshRelativeTexts();
    }

    /// <summary>Starts/stops the periodic refresh with the window's visibility.</summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            RefreshRelativeTexts();
            _refresh.Start();
        }
        else
        {
            _refresh.Stop();
        }
    }

    /// <summary>User alarms (habits excluded).</summary>
    public ObservableCollection<AlarmRowViewModel> Alarms { get; } = [];

    /// <summary>Daily-habit reminders.</summary>
    public ObservableCollection<HabitRowViewModel> Habits { get; } = [];

    /// <summary>Footer version text.</summary>
    public string VersionText { get; }

    /// <summary>Whether there are any user alarms.</summary>
    public bool HasAlarms => Alarms.Count > 0;

    /// <summary>Subtitle summarizing the next alarm.</summary>
    [ObservableProperty]
    private string _nextAlarmSummary = string.Empty;

    /// <summary>Whether the app starts with Windows.</summary>
    [ObservableProperty]
    private bool _startWithWindows;

    partial void OnStartWithWindowsChanged(bool value) => _startup.SetEnabled(value);

    /// <summary>Opens the editor to create an alarm.</summary>
    [RelayCommand]
    private void NewAlarm()
    {
        _log.LogDebug("Abrindo o editor para um alarme novo.");

        var created = _dialogs.Edit(null);
        if (created is not null)
        {
            _alarms.AddOrUpdate(created);
        }
    }

    /// <summary>Opens the editor for an existing alarm.</summary>
    [RelayCommand]
    private void Edit(AlarmRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var edited = _dialogs.Edit(row.Alarm);
        if (edited is not null)
        {
            _alarms.AddOrUpdate(edited);
        }
    }

    /// <summary>Deletes an alarm after confirmation.</summary>
    [RelayCommand]
    private void Delete(AlarmRowViewModel? row)
    {
        if (row is null || !_dialogs.ConfirmDelete(row.Title))
        {
            return;
        }

        _alarms.Remove(row.Alarm.Id);
    }

    /// <summary>Rebuilds the alarm and habit rows from the service state.</summary>
    private void Rebuild()
    {
        Alarms.Clear();

        // The alarm list hides habits; they live in the "daily" tab.
        var regular = _alarms.Items
            .Where(a => a.HabitKey is null)
            .OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase);

        foreach (var alarm in regular)
        {
            var id = alarm.Id;
            Alarms.Add(new AlarmRowViewModel(alarm, _clock, enabled => _alarms.SetEnabled(id, enabled)));
        }

        RebuildHabits();

        OnPropertyChanged(nameof(HasAlarms));
        RefreshRelativeTexts();
    }

    /// <summary>Rebuilds the habit rows from the catalog and saved state.</summary>
    private void RebuildHabits()
    {
        Habits.Clear();

        foreach (var def in HabitCatalog.All)
        {
            var existing = _alarms.Items.FirstOrDefault(a => a.HabitKey == def.Key);
            Habits.Add(new HabitRowViewModel(def, existing, ToggleHabit, SetHabitInterval));
        }
    }

    /// <summary>Creates or removes a habit's alarm when toggled.</summary>
    private void ToggleHabit(HabitDefinition def, bool active, int minutes)
    {
        var existing = _alarms.Items.FirstOrDefault(a => a.HabitKey == def.Key);

        if (active)
        {
            _alarms.AddOrUpdate(HabitCatalog.BuildAlarm(def, minutes, _clock.Now, existing));
            _log.LogInformation("Hábito {Habito} ativado (a cada {Min} min).", def.Key, minutes);
        }
        else if (existing is not null)
        {
            _alarms.Remove(existing.Id);
            _log.LogInformation("Hábito {Habito} desativado.", def.Key);
        }
    }

    /// <summary>Updates an active habit's interval.</summary>
    private void SetHabitInterval(HabitDefinition def, int minutes)
    {
        var existing = _alarms.Items.FirstOrDefault(a => a.HabitKey == def.Key);
        if (existing is not null)
        {
            _alarms.AddOrUpdate(HabitCatalog.BuildAlarm(def, minutes, _clock.Now, existing));
        }
    }

    /// <summary>Refreshes every relative text and the subtitle.</summary>
    private void RefreshRelativeTexts()
    {
        foreach (var row in Alarms)
        {
            row.RefreshNext();
        }

        NextAlarmSummary = DescribeNext();
    }

    /// <summary>Builds the "next alarm …" subtitle.</summary>
    private string DescribeNext()
    {
        var next = _scheduler.NextFireTime;

        if (next is null)
        {
            return Loc.Get(HasAlarms ? "Status_NoneActive" : "Status_NoneConfigured");
        }

        return Loc.Format("Status_NextPrefix", TimeFormat.Relative(next.Value, _clock));
    }
}
