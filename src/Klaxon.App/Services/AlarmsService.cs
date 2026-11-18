using Klaxon.Core.Model;
using Klaxon.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Klaxon.App.Services;

/// <summary>Owns the alarm list; every change persists at once and notifies.</summary>
public sealed class AlarmsService(IAlarmStore store, ILogger<AlarmsService> log)
{
    private readonly IAlarmStore _store = store;
    private readonly ILogger<AlarmsService> _log = log;

    private List<Alarm> _items = [];

    /// <summary>The current alarms.</summary>
    public IReadOnlyList<Alarm> Items => _items;

    /// <summary>Raised after any persisted change.</summary>
    public event EventHandler? Changed;

    /// <summary>Loads alarms from the store.</summary>
    public void Load()
    {
        _items = [.. _store.Load()];
        _log.LogInformation("{Quantidade} alarme(s) carregados.", _items.Count);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Finds an alarm by id.</summary>
    public Alarm? Find(Guid id) => _items.FirstOrDefault(a => a.Id == id);

    /// <summary>Adds a new alarm or replaces one with the same id.</summary>
    public void AddOrUpdate(Alarm alarm)
    {
        var indice = _items.FindIndex(a => a.Id == alarm.Id);

        if (indice >= 0)
        {
            _items[indice] = alarm;
            _log.LogInformation("Alarme {Titulo} atualizado.", alarm.Title);
        }
        else
        {
            _items.Add(alarm);
            _log.LogInformation("Alarme {Titulo} criado ({Agenda}).", alarm.Title, alarm.Schedule.Describe());
        }

        Persist();
    }

    /// <summary>Removes an alarm by id.</summary>
    public void Remove(Guid id)
    {
        var alarm = Find(id);
        if (alarm is null)
        {
            return;
        }

        _items.RemoveAll(a => a.Id == id);
        _log.LogInformation("Alarme {Titulo} removido.", alarm.Title);
        Persist();
    }

    /// <summary>Enables or disables an alarm.</summary>
    public void SetEnabled(Guid id, bool enabled)
    {
        var alarm = Find(id);
        if (alarm is null || alarm.IsEnabled == enabled)
        {
            return;
        }

        AddOrUpdate(alarm with { IsEnabled = enabled });
    }

    /// <summary>Saves to the store and notifies listeners.</summary>
    private void Persist()
    {
        _store.Save(_items);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
