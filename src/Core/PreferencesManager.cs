using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ultraudio.Models;

namespace Ultraudio.Core;

/// <summary>
/// Manages loading and saving of <see cref="AppSettings"/> to disk.
/// Settings are stored as JSON in the platform-appropriate app-data directory.
/// </summary>
public class PreferencesManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public AppSettings Settings { get; private set; } = new();

    public PreferencesManager()
    {
        // Cross-platform settings directory
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        string settingsDir = Path.Combine(appData, "Ultraudio");
        Directory.CreateDirectory(settingsDir);
        _settingsPath = Path.Combine(settingsDir, "settings.json");
    }

    /// <summary>Loads settings from disk, or returns defaults if the file doesn't exist.</summary>
    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (loaded != null)
                    Settings = loaded;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Preferences] Failed to load settings: {ex.Message}. Using defaults.");
            Settings = new AppSettings();
        }
    }

    /// <summary>Saves the current settings to disk.</summary>
    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Settings, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Preferences] Failed to save settings: {ex.Message}");
        }
    }

    /// <summary>Convenience: save immediately after modifying a setting.</summary>
    public void Set(Action<AppSettings> mutate)
    {
        mutate(Settings);
        Save();
    }
}
