using System.Globalization;
using Klaxon.Core.Abstractions;
using Klaxon.Core.Localization;
using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Klaxon.App.ViewModels;

/// <summary>Which kind of schedule the editor is building.</summary>
public enum ScheduleKind
{
    Once,
    Daily,
    Weekly,
    Interval,
}

/// <summary>A weekday toggle in the editor.</summary>
public sealed partial class DayToggle : ObservableObject
{
    public required WeekDays Flag { get; init; }

    public required string Label { get; init; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>An urgency choice in the editor.</summary>
public sealed partial class UrgencyChoice : ObservableObject
{
    public required UrgencyProfile Profile { get; init; }

    /// <summary>Localized level name.</summary>
    public string Label => Loc.UrgencyName(Profile.Level);

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>View model of the create/edit alarm dialog.</summary>
public sealed partial class AlarmEditorViewModel : ObservableObject
{
    /// <summary>Idle time treated as "away" for new alarms.</summary>
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);

    /// <summary>Current-language culture.</summary>
    private static CultureInfo Culture => Loc.Culture;

    /// <summary>Date pattern of the current language (dd/MM/yyyy or MM/dd/yyyy).</summary>
    private static string DatePattern => Loc.Get("Fmt_DateLong");

    private readonly ISystemClock _clock;
    private readonly Guid _id;

    /// <summary>Cycle anchor of the edited alarm, if it had one.</summary>
    private readonly DateTimeOffset? _originalAnchor;

    /// <summary>Interval the alarm had when opened.</summary>
    private readonly TimeSpan? _originalInterval;

    public AlarmEditorViewModel(ISystemClock clock, Alarm? existing = null)
    {
        _clock = clock;
        _id = existing?.Id ?? Guid.NewGuid();

        IsNew = existing is null;

        Days =
        [
            new DayToggle { Flag = WeekDays.Monday, Label = Loc.Get("Day_1") },
            new DayToggle { Flag = WeekDays.Tuesday, Label = Loc.Get("Day_2") },
            new DayToggle { Flag = WeekDays.Wednesday, Label = Loc.Get("Day_3") },
            new DayToggle { Flag = WeekDays.Thursday, Label = Loc.Get("Day_4") },
            new DayToggle { Flag = WeekDays.Friday, Label = Loc.Get("Day_5") },
            new DayToggle { Flag = WeekDays.Saturday, Label = Loc.Get("Day_6") },
            new DayToggle { Flag = WeekDays.Sunday, Label = Loc.Get("Day_0") },
        ];

        Urgencies = [.. UrgencyProfiles.All.Select(p => new UrgencyChoice { Profile = p })];

        // Default to a near-future rounded time so a one-time alarm is not born in the past.
        var suggestion = NextRoundTime(_clock);
        _dateText = suggestion.ToString(DatePattern, Culture);
        _timeText = suggestion.ToString("HH:mm", Culture);

        if (existing is null)
        {
            SelectUrgency(UrgencyLevel.Normal);
            Kind = ScheduleKind.Daily;
            return;
        }

        _title = existing.Title;
        _message = existing.Message ?? string.Empty;
        _customSoundPath = existing.CustomSoundPath ?? string.Empty;
        _skipWhenAway = existing.SkipIfIdleFor is not null;
        _escalate = existing.Escalation is not null;
        SelectUrgency(existing.Urgency);

        if (existing.Schedule is IntervalSchedule interval)
        {
            _originalAnchor = interval.Anchor;
            _originalInterval = interval.Every;
        }

        LoadSchedule(existing.Schedule);
    }

    /// <summary>Whether this is a new alarm.</summary>
    public bool IsNew { get; }

    /// <summary>Localized window title.</summary>
    public string WindowTitle => Loc.Get(IsNew ? "Editor_New" : "Editor_Edit");

    /// <summary>Weekday toggles.</summary>
    public IReadOnlyList<DayToggle> Days { get; }

    /// <summary>Urgency choices.</summary>
    public IReadOnlyList<UrgencyChoice> Urgencies { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _customSoundPath = string.Empty;

    /// <summary>Time as "HH:mm".</summary>
    [ObservableProperty]
    private string _timeText;

    /// <summary>Date, used only for one-time alarms.</summary>
    [ObservableProperty]
    private string _dateText;

    /// <summary>Minutes between firings, for interval schedules.</summary>
    [ObservableProperty]
    private string _intervalMinutesText = "45";

    /// <summary>Whether the interval is limited to a time range.</summary>
    [ObservableProperty]
    private bool _useWindow = true;

    [ObservableProperty]
    private string _windowFromText = "09:00";

    [ObservableProperty]
    private string _windowToText = "18:00";

    /// <summary>Don't alert while keyboard/mouse are idle.</summary>
    [ObservableProperty]
    private bool _skipWhenAway;

    /// <summary>Escalate when the alert is ignored.</summary>
    [ObservableProperty]
    private bool _escalate;

    /// <summary>Selected schedule kind.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnce))]
    [NotifyPropertyChangedFor(nameof(IsDaily))]
    [NotifyPropertyChangedFor(nameof(IsWeekly))]
    [NotifyPropertyChangedFor(nameof(IsInterval))]
    [NotifyPropertyChangedFor(nameof(HasFixedTime))]
    private ScheduleKind _kind = ScheduleKind.Daily;

    public bool IsOnce
    {
        get => Kind == ScheduleKind.Once;
        set { if (value) { Kind = ScheduleKind.Once; } }
    }

    public bool IsDaily
    {
        get => Kind == ScheduleKind.Daily;
        set { if (value) { Kind = ScheduleKind.Daily; } }
    }

    public bool IsWeekly
    {
        get => Kind == ScheduleKind.Weekly;
        set { if (value) { Kind = ScheduleKind.Weekly; } }
    }

    public bool IsInterval
    {
        get => Kind == ScheduleKind.Interval;
        set { if (value) { Kind = ScheduleKind.Interval; } }
    }

    /// <summary>Interval schedules have no fixed time.</summary>
    public bool HasFixedTime => Kind != ScheduleKind.Interval;

    /// <summary>Set when the user confirms; null if cancelled.</summary>
    public Alarm? Result { get; private set; }

    /// <summary>Raised to close the dialog (true = saved).</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>Raised with a message when validation fails.</summary>
    public event Action<string>? ValidationFailed;

    /// <summary>Now plus a small buffer, rounded up to the next 5 minutes.</summary>
    private static DateTime NextRoundTime(ISystemClock clock)
    {
        var now = TimeZoneInfo.ConvertTime(clock.Now, clock.LocalTimeZone).DateTime;

        var target = now.AddSeconds(30);
        target = target.AddTicks(-(target.Ticks % TimeSpan.TicksPerMinute)); // drop seconds

        var remainder = target.Minute % 5;
        if (remainder != 0)
        {
            target = target.AddMinutes(5 - remainder);
        }
        else if (target <= now)
        {
            target = target.AddMinutes(5);
        }

        return target;
    }

    /// <summary>Validates and, on success, produces the alarm and closes.</summary>
    [RelayCommand]
    private void Save()
    {
        if (!TryBuild(out var alarm, out var error))
        {
            ValidationFailed?.Invoke(error ?? Loc.Get("Val_SaveFailed"));
            return;
        }

        Result = alarm;
        CloseRequested?.Invoke(true);
    }

    /// <summary>Cancels without saving.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>Opens a file dialog to pick a custom sound.</summary>
    [RelayCommand]
    private void BrowseSound()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.Get("Editor_SoundDialogTitle"),
            Filter = Loc.Get("Editor_SoundFilter"),
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            CustomSoundPath = dialog.FileName;
        }
    }

    /// <summary>Clears the custom sound.</summary>
    [RelayCommand]
    private void ClearSound() => CustomSoundPath = string.Empty;

    /// <summary>Validates the fields and builds the alarm, or returns an error.</summary>
    private bool TryBuild(out Alarm? alarm, out string? error)
    {
        alarm = null;
        error = null;

        if (string.IsNullOrWhiteSpace(Title))
        {
            error = Loc.Get("Val_NeedName");
            return false;
        }

        var time = default(TimeOnly);

        if (HasFixedTime && !TimeOnly.TryParseExact(TimeText.Trim(), "HH\\:mm", Culture, DateTimeStyles.None, out time))
        {
            error = Loc.Get("Val_BadTime");
            return false;
        }

        ISchedule schedule;

        switch (Kind)
        {
            case ScheduleKind.Interval:
                if (!int.TryParse(IntervalMinutesText.Trim(), out var minutes) || minutes < 1)
                {
                    error = Loc.Get("Val_BadInterval");
                    return false;
                }

                TimeOnly? from = null;
                TimeOnly? to = null;

                if (UseWindow)
                {
                    if (!TimeOnly.TryParseExact(WindowFromText.Trim(), "HH\\:mm", Culture, DateTimeStyles.None, out var start) ||
                        !TimeOnly.TryParseExact(WindowToText.Trim(), "HH\\:mm", Culture, DateTimeStyles.None, out var end))
                    {
                        error = Loc.Get("Val_BadWindow");
                        return false;
                    }

                    if (start == end)
                    {
                        error = Loc.Get("Val_WindowEqual");
                        return false;
                    }

                    from = start;
                    to = end;
                }

                // Keep the running cycle's anchor unless the interval changed.
                var newInterval = TimeSpan.FromMinutes(minutes);
                var anchor = newInterval == _originalInterval && _originalAnchor is not null
                    ? _originalAnchor.Value
                    : _clock.Now;

                schedule = new IntervalSchedule(newInterval, anchor, from, to);
                break;

            case ScheduleKind.Once:
                if (!DateOnly.TryParseExact(DateText.Trim(), DatePattern, Culture, DateTimeStyles.None, out var date))
                {
                    error = Loc.Format("Val_BadDate", DatePattern);
                    return false;
                }

                var when = new DateTimeOffset(
                    date.ToDateTime(time),
                    _clock.LocalTimeZone.GetUtcOffset(date.ToDateTime(time)));

                if (when <= _clock.Now)
                {
                    error = Loc.Get("Val_PastInstant");
                    return false;
                }

                schedule = new OneTimeSchedule(when);
                break;

            case ScheduleKind.Weekly:
                var days = Days
                    .Where(d => d.IsChecked)
                    .Aggregate(WeekDays.None, (acc, d) => acc | d.Flag);

                if (days == WeekDays.None)
                {
                    error = Loc.Get("Val_NeedWeekday");
                    return false;
                }

                schedule = new WeeklySchedule(days, time);
                break;

            default:
                schedule = new DailySchedule(time);
                break;
        }

        var urgency = Urgencies.FirstOrDefault(u => u.IsChecked)?.Profile.Level ?? UrgencyLevel.Normal;

        alarm = new Alarm
        {
            Id = _id,
            Title = Title.Trim(),
            Message = string.IsNullOrWhiteSpace(Message) ? null : Message.Trim(),
            Schedule = schedule,
            Urgency = urgency,
            CustomSoundPath = string.IsNullOrWhiteSpace(CustomSoundPath) ? null : CustomSoundPath.Trim(),
            SkipIfIdleFor = SkipWhenAway ? IdleThreshold : null,
            Escalation = Escalate ? EscalationPolicy.Default : null,
        };

        return true;
    }

    /// <summary>Checks the urgency choice matching the level.</summary>
    private void SelectUrgency(UrgencyLevel level)
    {
        foreach (var choice in Urgencies)
        {
            choice.IsChecked = choice.Profile.Level == level;
        }
    }

    /// <summary>Fills the fields from an existing schedule.</summary>
    private void LoadSchedule(ISchedule schedule)
    {
        switch (schedule)
        {
            case OneTimeSchedule once:
                Kind = ScheduleKind.Once;
                var local = TimeZoneInfo.ConvertTime(once.At, _clock.LocalTimeZone);
                DateText = local.ToString(DatePattern, Culture);
                TimeText = local.ToString("HH:mm", Culture);
                break;

            case WeeklySchedule weekly:
                Kind = ScheduleKind.Weekly;
                TimeText = weekly.At.ToString("HH\\:mm");
                foreach (var day in Days)
                {
                    day.IsChecked = (weekly.Days & day.Flag) != 0;
                }

                break;

            case DailySchedule daily:
                Kind = ScheduleKind.Daily;
                TimeText = daily.At.ToString("HH\\:mm");
                break;

            case IntervalSchedule interval:
                Kind = ScheduleKind.Interval;
                IntervalMinutesText = ((int)interval.Every.TotalMinutes).ToString(Culture);
                UseWindow = interval.HasWindow;

                if (interval.HasWindow)
                {
                    WindowFromText = interval.ActiveFrom!.Value.ToString("HH\\:mm");
                    WindowToText = interval.ActiveTo!.Value.ToString("HH\\:mm");
                }

                break;
        }
    }
}
