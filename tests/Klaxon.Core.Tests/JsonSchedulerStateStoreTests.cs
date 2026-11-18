using Klaxon.Core.Model;
using Klaxon.Core.Persistence;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

public class JsonSchedulerStateStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly string _file;

    public JsonSchedulerStateStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "KlaxonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "scheduler-state.json");
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
    public void Missing_file_loads_an_empty_state()
    {
        var store = new JsonSchedulerStateStore(_file);

        var state = store.Load();

        Assert.True(state.IsEmpty);
    }

    [Fact]
    public void Round_trip_preserves_snoozes_and_escalations()
    {
        var store = new JsonSchedulerStateStore(_file);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.FromHours(-3));
        var deadline = new DateTimeOffset(2026, 6, 10, 9, 45, 0, TimeSpan.FromHours(-3));

        var original = new SchedulerState
        {
            Snoozes = [new SnoozeState(id1, 2, when)],
            Escalations = [new EscalationSnapshot(id2, UrgencyLevel.High, deadline)],
        };

        store.Save(original);
        var loaded = store.Load();

        var snooze = Assert.Single(loaded.Snoozes);
        Assert.Equal(id1, snooze.AlarmId);
        Assert.Equal(2, snooze.Count);
        Assert.Equal(when, snooze.DueAt);

        var escalation = Assert.Single(loaded.Escalations);
        Assert.Equal(id2, escalation.AlarmId);
        Assert.Equal(UrgencyLevel.High, escalation.Level);
        Assert.Equal(deadline, escalation.IgnoreDeadline);
    }

    [Fact]
    public void Snooze_without_pending_time_survives_the_round_trip()
    {
        // A consumed snooze keeps its counter but has no pending wake time.
        var store = new JsonSchedulerStateStore(_file);
        var id = Guid.NewGuid();

        store.Save(new SchedulerState { Snoozes = [new SnoozeState(id, 1, null)] });
        var loaded = store.Load();

        var snooze = Assert.Single(loaded.Snoozes);
        Assert.Equal(1, snooze.Count);
        Assert.Null(snooze.DueAt);
    }

    [Fact]
    public void Saving_over_leaves_a_backup_of_the_previous_version()
    {
        var store = new JsonSchedulerStateStore(_file);

        store.Save(new SchedulerState { Snoozes = [new SnoozeState(Guid.NewGuid(), 1, null)] });
        store.Save(new SchedulerState());

        Assert.True(File.Exists(_file + ".bak"), "The atomic write must leave a .bak.");
        Assert.False(File.Exists(_file + ".tmp"), "The temp file must not be left behind.");
        Assert.True(store.Load().IsEmpty);
    }

    [Fact]
    public void Level_enum_is_written_as_a_name()
    {
        var store = new JsonSchedulerStateStore(_file);

        store.Save(new SchedulerState
        {
            Escalations = [new EscalationSnapshot(Guid.NewGuid(), UrgencyLevel.Critical, null)],
        });

        Assert.Contains("\"Critical\"", File.ReadAllText(_file));
    }

    [Fact]
    public void Corrupt_file_is_quarantined_instead_of_overwritten()
    {
        File.WriteAllText(_file, "{ isto não é json válido");

        var store = new JsonSchedulerStateStore(_file);
        var state = store.Load();

        Assert.True(state.IsEmpty);
        Assert.False(File.Exists(_file), "The unreadable file must be moved aside.");

        var quarantine = Directory.GetFiles(_folder, "scheduler-state.json.corrompido-*");
        Assert.Single(quarantine);
    }
}
