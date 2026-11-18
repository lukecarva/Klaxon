using Klaxon.App.Services;
using Klaxon.Core.Model;
using Klaxon.Core.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klaxon.App.Tests;

public class AlarmsServiceTests
{
    private static AlarmsService NewService(InMemoryAlarmStore store) =>
        new(store, NullLogger<AlarmsService>.Instance);

    private static Alarm SampleAlarm(string title = "Reunião") =>
        Alarm.New(title, new DailySchedule(new TimeOnly(7, 0)));

    [Fact]
    public void Load_reads_from_the_store_and_raises_changed()
    {
        var store = new InMemoryAlarmStore();
        store.Save([SampleAlarm()]);

        var service = NewService(store);
        var changed = 0;
        service.Changed += (_, _) => changed++;

        service.Load();

        Assert.Single(service.Items);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void AddOrUpdate_adds_persists_and_notifies()
    {
        var store = new InMemoryAlarmStore();
        var service = NewService(store);
        var changed = 0;
        service.Changed += (_, _) => changed++;

        service.AddOrUpdate(SampleAlarm());

        Assert.Single(service.Items);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void AddOrUpdate_replaces_the_alarm_with_the_same_id()
    {
        var store = new InMemoryAlarmStore();
        var service = NewService(store);
        var alarm = SampleAlarm();

        service.AddOrUpdate(alarm);
        service.AddOrUpdate(alarm with { Title = "Novo título" });

        Assert.Single(service.Items);
        Assert.Equal("Novo título", service.Items[0].Title);
    }

    [Fact]
    public void Remove_deletes_by_id()
    {
        var store = new InMemoryAlarmStore();
        var service = NewService(store);
        var alarm = SampleAlarm();
        service.AddOrUpdate(alarm);

        service.Remove(alarm.Id);

        Assert.Empty(service.Items);
    }

    [Fact]
    public void Remove_unknown_id_does_nothing()
    {
        var store = new InMemoryAlarmStore();
        var service = NewService(store);
        service.AddOrUpdate(SampleAlarm());
        var before = store.SaveCount;

        service.Remove(Guid.NewGuid());

        Assert.Single(service.Items);
        Assert.Equal(before, store.SaveCount);
    }

    [Fact]
    public void SetEnabled_toggles_and_persists()
    {
        var store = new InMemoryAlarmStore();
        var service = NewService(store);
        var alarm = SampleAlarm();
        service.AddOrUpdate(alarm);

        service.SetEnabled(alarm.Id, false);

        Assert.False(service.Items.Single().IsEnabled);
    }

    [Fact]
    public void SetEnabled_no_op_when_state_already_matches()
    {
        var store = new InMemoryAlarmStore();
        var service = NewService(store);
        var alarm = SampleAlarm();
        service.AddOrUpdate(alarm);
        var saves = store.SaveCount;

        service.SetEnabled(alarm.Id, true); // already enabled

        Assert.Equal(saves, store.SaveCount);
    }
}
