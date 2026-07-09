using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Ultraudio.Core;

// ─────────────────────────────────────────────────────────────────────────────
// Data model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single entry in the play history, tracking play count, last played time,
/// and total accumulated listen time.
/// </summary>
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
/// Tracks playback history per file. Data is persisted as JSON with debounced
/// writes to avoid excessive disk I/O on every track change.
/// </summary>
public class PlayHistory
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _historyPath;
    private Dictionary<string, PlayHistoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // ── Debounced save ────────────────────────────────────────────────────
    private bool _isDirty = false;
    private Timer? _saveTimer;
    private const int SaveDelayMs = 5000; // 5 seconds debounce

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

    // ─── Public API ──────────────────────────────────────────────────────

    /// <summary>Record that a track started playing (debounced save).</summary>
    public void RecordPlay(string filePath, string displayTitle)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        if (!_entries.TryGetValue(filePath, out var entry))
        {
            entry = new PlayHistoryEntry { FilePath = filePath, DisplayTitle = displayTitle };
            _entries[filePath] = entry;
        }

        entry.PlayCount++;
        entry.LastPlayed = DateTime.UtcNow;
        entry.DisplayTitle = displayTitle; // Keep title fresh
        ScheduleSave();
    }

    /// <summary>Accumulate listened seconds for the current session (debounced save).</summary>
    public void AddListenTime(string filePath, double seconds)
    {
        if (_entries.TryGetValue(filePath, out var entry))
        {
            entry.TotalListenSeconds += seconds;
            ScheduleSave();
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

    // ─── Debounced persistence ───────────────────────────────────────────

    private void ScheduleSave()
    {
        _isDirty = true;
        // Reset the timer — only fires after SaveDelayMs of inactivity
        _saveTimer?.Dispose();
        _saveTimer = new Timer(_ => FlushIfDirty(), null, SaveDelayMs, Timeout.Infinite);
    }

    private void FlushIfDirty()
    {
        if (_isDirty)
        {
            SaveToDisk();
            _isDirty = false;
        }
    }

    /// <summary>
    /// Force an immediate save to disk. Call this on app shutdown to ensure
    /// no data is lost from the debounce buffer.
    /// </summary>
    public void Save()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        SaveToDisk();
        _isDirty = false;
    }

    // ─── Persistence ─────────────────────────────────────────────────────

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
            Log.Error("History", "Load failed", ex);
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var list = new List<PlayHistoryEntry>(_entries.Values);
            string json = JsonSerializer.Serialize(list, _jsonOptions);
            File.WriteAllText(_historyPath, json);
        }
        catch (Exception ex)
        {
            Log.Error("History", "Save failed", ex);
        }
    }
}
