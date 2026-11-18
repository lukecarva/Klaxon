using System.IO;
using System.Text;
using System.Text.Json;

namespace Klaxon.App.Services;

/// <summary>App preferences, stored apart from the alarms.</summary>
public sealed class AppSettings
{
    /// <summary>Language code ("en" or "pt-BR"). Null = decide from Windows.</summary>
    public string? Language { get; set; }
}

/// <summary>Reads and writes <c>settings.json</c>.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Loads preferences, returning defaults if missing or unreadable.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Fall back to defaults on a corrupt file.
        }

        return new AppSettings();
    }

    /// <summary>Saves preferences atomically.</summary>
    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureCreated();

        var json = JsonSerializer.Serialize(settings, Options);
        var temp = AppPaths.SettingsFile + ".tmp";

        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(AppPaths.SettingsFile))
        {
            File.Replace(temp, AppPaths.SettingsFile, null);
        }
        else
        {
            File.Move(temp, AppPaths.SettingsFile);
        }
    }
}
