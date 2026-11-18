using System.Globalization;
using Klaxon.Core.Localization;
using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Klaxon.App.ViewModels;

/// <summary>One row of the "daily" tab: a ready reminder with an on/off toggle.</summary>
public sealed partial class HabitRowViewModel : ObservableObject
{
    private readonly HabitDefinition _habit;
    private readonly Action<HabitDefinition, bool, int> _onToggled;
    private readonly Action<HabitDefinition, int> _onIntervalChanged;

    // While the row is (re)built from saved state, setters must not call back.
    private readonly bool _building;

    public HabitRowViewModel(
        HabitDefinition habit,
        Alarm? existing,
        Action<HabitDefinition, bool, int> onToggled,
        Action<HabitDefinition, int> onIntervalChanged)
    {
        _habit = habit;
        _onToggled = onToggled;
        _onIntervalChanged = onIntervalChanged;

        _building = true;

        var minutos = existing?.Schedule is IntervalSchedule agenda
            ? (int)agenda.Every.TotalMinutes
            : habit.DefaultMinutes;

        _isActive = existing is { IsEnabled: true };
        _intervalText = minutos.ToString(CultureInfo.InvariantCulture);

        _building = false;
    }

    /// <summary>Habit icon.</summary>
    public string Emoji => _habit.Emoji;

    /// <summary>Habit name.</summary>
    public string Name => _habit.Name;

    /// <summary>Short description.</summary>
    public string Note => _habit.Note;

    /// <summary>Whether the reminder is active.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Interval in minutes, as text.</summary>
    [ObservableProperty]
    private string _intervalText;

    partial void OnIsActiveChanged(bool value)
    {
        if (!_building)
        {
            _onToggled(_habit, value, CurrentMinutes());
        }
    }

    partial void OnIntervalTextChanged(string value)
    {
        if (!_building && IsActive)
        {
            _onIntervalChanged(_habit, CurrentMinutes());
        }
    }

    /// <summary>Parsed interval, or the default when invalid.</summary>
    private int CurrentMinutes() =>
        int.TryParse(IntervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m >= 1
            ? m
            : _habit.DefaultMinutes;
}
