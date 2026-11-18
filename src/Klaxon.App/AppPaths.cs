using System.IO;

namespace Klaxon.App;

/// <summary>Filesystem paths the app reads and writes, under %APPDATA%\Klaxon.</summary>
public static class AppPaths
{
    /// <summary>Root data folder.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Klaxon");

    /// <summary>Folder for log files.</summary>
    public static string LogsFolder => Path.Combine(Root, "logs");

    /// <summary>Rolling log file name pattern.</summary>
    public static string LogFilePattern => Path.Combine(LogsFolder, "log-.txt");

    /// <summary>Path of the alarms file.</summary>
    public static string AlarmsFile => Path.Combine(Root, "alarms.json");

    /// <summary>Path of the settings file.</summary>
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>Path of the scheduler runtime-state file (snoozes and escalations).</summary>
    public static string SchedulerStateFile => Path.Combine(Root, "scheduler-state.json");

    /// <summary>Creates the data and log folders if missing.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsFolder);
    }
}
