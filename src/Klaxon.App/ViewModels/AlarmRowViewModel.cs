using System.Windows;
using System.Windows.Media;
using Klaxon.Core.Abstractions;
using Klaxon.Core.Localization;
using Klaxon.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Klaxon.App.ViewModels;

/// <summary>One row of the alarm list.</summary>
public sealed partial class AlarmRowViewModel : ObservableObject
{
    private readonly ISystemClock _clock;
    private readonly Action<bool> _onToggled;

    // Guards the toggle callback during the initial load of IsEnabled.
    private readonly bool _suppressCallback;

    public AlarmRowViewModel(Alarm alarm, ISystemClock clock, Action<bool> onToggled)
    {
        Alarm = alarm;
        _clock = clock;
        _onToggled = onToggled;

        _suppressCallback = true;
        IsEnabled = alarm.IsEnabled;
        _suppressCallback = false;

        AccentBrush = Application.Current?.TryFindResource($"Brush.Urgency.{alarm.Urgency}") as Brush
                      ?? Brushes.Gray;
    }

    /// <summary>The underlying alarm.</summary>
    public Alarm Alarm { get; }

    /// <summary>Alarm title.</summary>
    public string Title => Alarm.Title;

    /// <summary>Schedule description.</summary>
    public string ScheduleText => Alarm.Schedule.Describe();

    /// <summary>Localized urgency name.</summary>
    public string UrgencyName => Loc.UrgencyName(Alarm.Urgency);

    /// <summary>Color of the urgency accent.</summary>
    public Brush AccentBrush { get; }

    /// <summary>Whether the alarm is enabled.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_suppressCallback)
        {
            _onToggled(value);
        }
    }

    /// <summary>Relative text of the next occurrence, or off/none.</summary>
    public string NextText
    {
        get
        {
            if (!Alarm.IsEnabled)
            {
                return Loc.Get("Row_Off");
            }

            var next = Alarm.Schedule.NextOccurrenceAfter(_clock.Now, _clock.LocalTimeZone);

            return next is null
                ? Loc.Get("Row_NoNext")
                : TimeFormat.Relative(next.Value, _clock);
        }
    }

    /// <summary>Re-reads the relative texts without recreating the row.</summary>
    public void RefreshNext() => OnPropertyChanged(nameof(NextText));
}
