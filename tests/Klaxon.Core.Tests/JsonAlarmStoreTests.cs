using Klaxon.Core.Model;
using Klaxon.Core.Persistence;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

public class JsonAlarmStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly string _file;

    public JsonAlarmStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "KlaxonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "alarms.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked file must not fail the test.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Missing_file_loads_an_empty_list()
    {
        var store = new JsonAlarmStore(_file);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Round_trip_preserves_the_schedule_type()
    {
        // ISchedule is polymorphic; the "$type" discriminator must round-trip.
        var store = new JsonAlarmStore(_file);

        var originals = new[]
        {
            Alarm.New("Diário", new DailySchedule(new TimeOnly(7, 0))),
            Alarm.New("Semanal", new WeeklySchedule(WeekDays.Weekdays, new TimeOnly(8, 30)), UrgencyLevel.High),
            Alarm.New("Único", new OneTimeSchedule(new DateTimeOffset(2026, 12, 25, 9, 0, 0, TimeSpan.FromHours(-3)))),
            Alarm.New(
                "Cíclico",
                new IntervalSchedule(
                    TimeSpan.FromMinutes(45),
                    new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.FromHours(-3)))),
        };

        store.Save(originals);
        var loaded = store.Load();

        Assert.Equal(4, loaded.Count);
        Assert.Equal<Alarm>(originals, loaded);
        Assert.IsType<DailySchedule>(loaded[0].Schedule);
        Assert.IsType<WeeklySchedule>(loaded[1].Schedule);
        Assert.IsType<OneTimeSchedule>(loaded[2].Schedule);
        Assert.IsType<IntervalSchedule>(loaded[3].Schedule);
        Assert.Equal(UrgencyLevel.High, loaded[1].Urgency);
    }

    [Fact]
    public void Interval_reminder_preserves_anchor_window_and_absence_rule()
    {
        var store = new JsonAlarmStore(_file);
        var anchor = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.FromHours(-3));

        var original = Alarm.New(
            "Beber água",
            new IntervalSchedule(TimeSpan.FromMinutes(45), anchor, new TimeOnly(9, 0), new TimeOnly(18, 0)),
            UrgencyLevel.Whisper) with
        {
            SkipIfIdleFor = TimeSpan.FromMinutes(5),
        };

        store.Save([original]);
        var loaded = store.Load().Single();

        Assert.Equal(original, loaded);

        var schedule = Assert.IsType<IntervalSchedule>(loaded.Schedule);

        // The anchor must round-trip so the cycle survives an app restart.
        Assert.Equal(anchor, schedule.Anchor);
        Assert.Equal(new TimeOnly(9, 0), schedule.ActiveFrom);
        Assert.Equal(TimeSpan.FromMinutes(5), loaded.SkipIfIdleFor);
    }

    [Fact]
    public void Saving_over_leaves_a_backup_of_the_previous_version()
    {
        var store = new JsonAlarmStore(_file);

        store.Save([Alarm.New("Primeiro", new DailySchedule(new TimeOnly(7, 0)))]);
        store.Save([Alarm.New("Segundo", new DailySchedule(new TimeOnly(8, 0)))]);

        Assert.True(File.Exists(_file + ".bak"), "The atomic write must leave a .bak.");
        Assert.False(File.Exists(_file + ".tmp"), "The temp file must not be left behind.");
        Assert.Equal("Segundo", store.Load().Single().Title);
    }

    [Fact]
    public void Does_not_write_computed_properties_to_the_file()
    {
        // Derived (computed) members must not be serialized.
        var store = new JsonAlarmStore(_file);

        store.Save(
        [
            Alarm.New(
                "Alongar",
                new IntervalSchedule(TimeSpan.FromHours(1), DateTimeOffset.UnixEpoch, new TimeOnly(9, 0), new TimeOnly(18, 0)),
                UrgencyLevel.High),
        ]);

        var json = File.ReadAllText(_file);

        Assert.DoesNotContain("\"Profile\"", json);
        Assert.DoesNotContain("\"EffectiveSound\"", json);
        Assert.DoesNotContain("\"DisplayName\"", json);
        Assert.DoesNotContain("\"HasWindow\"", json);
        Assert.DoesNotContain("\"DefaultOption\"", json);

        // What must be present is still present.
        Assert.Contains("\"Urgency\": \"High\"", json);
        Assert.Contains("\"$type\": \"interval\"", json);
        Assert.Contains("\"ActiveFrom\"", json);
    }

    [Fact]
    public void Corrupt_file_is_quarantined_instead_of_overwritten()
    {
        File.WriteAllText(_file, "{ isto não é json válido");

        var store = new JsonAlarmStore(_file);
        var loaded = store.Load();

        Assert.Empty(loaded);
        Assert.False(File.Exists(_file), "The unreadable file must be moved aside.");

        var quarantine = Directory.GetFiles(_folder, "alarms.json.corrompido-*");
        Assert.Single(quarantine);
        Assert.Contains("isto não é json válido", File.ReadAllText(quarantine[0]));
    }

    [Fact]
    public void Disabled_alarm_with_message_survives_the_round_trip()
    {
        var store = new JsonAlarmStore(_file);

        var original = Alarm.New("Alongar", new DailySchedule(new TimeOnly(15, 0)), UrgencyLevel.Whisper) with
        {
            Message = "Levanta e anda um pouco",
            IsEnabled = false,
            CustomSoundPath = @"C:\sons\gongo.wav",
        };

        store.Save([original]);
        var loaded = store.Load().Single();

        Assert.Equal(original, loaded);
        Assert.False(loaded.IsEnabled);
        Assert.Equal(@"C:\sons\gongo.wav", loaded.CustomSoundPath);
    }
}
