using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ultraudio.Models;

/// <summary>
/// Serialized settings model. All fields have sane defaults.
/// </summary>
public class AppSettings
{
    // ── Audio ───────────────────────────────────────────────────────────────
    public int LastDeviceIndex { get; set; } = -1;
    public string LastDeviceName { get; set; } = string.Empty;
    public double Volume { get; set; } = 1.0;
    public bool IsMuted { get; set; } = false;
    public bool ExclusiveMode { get; set; } = false;
    public bool RamMode { get; set; } = false;

    // ── Playback ────────────────────────────────────────────────────────────
    public string RepeatMode { get; set; } = "Off";   // "Off" | "One" | "All"
    public bool IsShuffleEnabled { get; set; } = false;
    public bool GaplessEnabled { get; set; } = true;

    // ── UI / Navigation ─────────────────────────────────────────────────────
    public string LastFolderPath { get; set; } = string.Empty;
    public double WindowWidth { get; set; } = 820;
    public double WindowHeight { get; set; } = 580;
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;

    // ── Features ────────────────────────────────────────────────────────────
    public bool HttpApiEnabled { get; set; } = false;
    public int HttpApiPort { get; set; } = 7654;
    public bool SpectrumEnabled { get; set; } = true;
    public bool CdEnabled { get; set; } = true;

    // ── Library ─────────────────────────────────────────────────────────────
    public List<string> LibraryFolders { get; set; } = new();

    // ── Favorites ───────────────────────────────────────────────────────────
    /// <summary>Set of file paths marked as favorite.</summary>
    public HashSet<string> Favorites { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    // ── Version ─────────────────────────────────────────────────────────────
    [JsonPropertyName("settingsVersion")]
    public int SettingsVersion { get; set; } = 1;
}
