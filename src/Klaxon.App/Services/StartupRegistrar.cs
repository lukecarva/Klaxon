using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Klaxon.App.Services;

/// <summary>Registers/unregisters "start with Windows" under HKCU (no admin).</summary>
public sealed class StartupRegistrar(ILogger<StartupRegistrar> log)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Klaxon";

    private readonly ILogger<StartupRegistrar> _log = log;

    /// <summary>Whether start-with-Windows is currently set.</summary>
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>Turns start-with-Windows on or off.</summary>
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null)
        {
            _log.LogError("The user's Run key is unavailable.");
            return;
        }

        if (enabled)
        {
            var path = ExecutablePath();
            if (path is null)
            {
                _log.LogError("Could not determine the executable path.");
                return;
            }

            // --minimized: start straight to the tray.
            key.SetValue(ValueName, $"\"{path}\" --minimized");
            _log.LogInformation("Startup enabled.");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            _log.LogInformation("Startup disabled.");
        }
    }

    /// <summary>Path of the running executable.</summary>
    private static string? ExecutablePath() =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
}
