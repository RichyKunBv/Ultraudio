using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ultraudio.Core;

// ─────────────────────────────────────────────────────────────────────────────
// Data model
// ─────────────────────────────────────────────────────────────────────────────

public class PlayHistoryEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public int PlayCount { get; set; } = 0;
    public DateTime LastPlayed { get; set; } = DateTime.MinValue;
    public double TotalListenSeconds { get; set; } = 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// Manager
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tracks playback history per file. Data is persisted as JSON.
/// </summary>
public class PlayHistory
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _historyPath;
    private Dictionary<string, PlayHistoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public PlayHistory()
    {
        string configDir = GetConfigDir();
        Directory.CreateDirectory(configDir);
        _historyPath = Path.Combine(configDir, "history.json");
        Load();
    }

    private static string GetConfigDir()
    {
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(appData, "Ultraudio");
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Record that a track started playing.</summary>
    public void RecordPlay(string filePath, string displayTitle)
    {
        if (!_entries.TryGetValue(filePath, out var entry))
        {
            entry = new PlayHistoryEntry { FilePath = filePath, DisplayTitle = displayTitle };
            _entries[filePath] = entry;
        }

        entry.PlayCount++;
        entry.LastPlayed = DateTime.UtcNow;
        entry.DisplayTitle = displayTitle; // Keep title fresh
        Save();
    }

    /// <summary>Accumulate listened seconds for the current session.</summary>
    public void AddListenTime(string filePath, double seconds)
    {
        if (_entries.TryGetValue(filePath, out var entry))
        {
            entry.TotalListenSeconds += seconds;
            // Debounced save — don't hit disk every second
        }
    }

    /// <summary>Get the most recently played tracks (newest first).</summary>
    public List<PlayHistoryEntry> GetRecent(int max = 50)
    {
        var list = new List<PlayHistoryEntry>(_entries.Values);
        list.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));
        return list.Count > max ? list.GetRange(0, max) : list;
    }

    /// <summary>Get the most played tracks (highest play count first).</summary>
    public List<PlayHistoryEntry> GetMostPlayed(int max = 20)
    {
        var list = new List<PlayHistoryEntry>(_entries.Values);
        list.Sort((a, b) => b.PlayCount.CompareTo(a.PlayCount));
        return list.Count > max ? list.GetRange(0, max) : list;
    }

    public int GetPlayCount(string filePath)
        => _entries.TryGetValue(filePath, out var e) ? e.PlayCount : 0;

    public void Clear() { _entries.Clear(); Save(); }

    // ─── Persistence ──────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (File.Exists(_historyPath))
            {
                string json = File.ReadAllText(_historyPath);
                var list = JsonSerializer.Deserialize<List<PlayHistoryEntry>>(json, _jsonOptions);
                if (list != null)
                {
                    _entries = new Dictionary<string, PlayHistoryEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in list)
                        _entries[e.FilePath] = e;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[History] Load failed: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            var list = new List<PlayHistoryEntry>(_entries.Values);
            string json = JsonSerializer.Serialize(list, _jsonOptions);
            File.WriteAllText(_historyPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[History] Save failed: {ex.Message}");
        }
    }
}
