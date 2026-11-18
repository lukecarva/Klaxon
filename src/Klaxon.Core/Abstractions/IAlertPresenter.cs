using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Abstractions;

/// <summary>Shows a triggered alarm to the user.</summary>
public interface IAlertPresenter
{
    /// <summary>Presents the alert for the given trigger.</summary>
    void Show(AlarmTriggeredEventArgs trigger);
}
