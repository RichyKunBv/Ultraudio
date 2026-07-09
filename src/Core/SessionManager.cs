using System;
using System.Collections.Generic;
using Ultraudio.Models;

namespace Ultraudio.Core;

/// <summary>
/// Handles saving and restoring the playback session state (queue, position, window geometry).
/// Extracted from MainWindow to separate persistence concerns from UI logic.
/// </summary>
public class SessionManager
{
    private readonly PreferencesManager _prefs;

    public SessionManager(PreferencesManager prefs)
    {
        _prefs = prefs;
    }

    // ── Session State ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if there is a saved session to restore.
    /// </summary>
    public bool HasSavedSession =>
        _prefs.Settings.SessionQueue != null && _prefs.Settings.SessionQueue.Count > 0;

    /// <summary>
    /// Gets the saved session queue.
    /// </summary>
    public List<TrackModel> SavedQueue => _prefs.Settings.SessionQueue ?? new();

    /// <summary>
    /// Gets the saved current track index.
    /// </summary>
    public int SavedCurrentIndex => _prefs.Settings.SessionCurrentIndex;

    /// <summary>
    /// Gets the saved playback position in seconds.
    /// </summary>
    public double SavedPosition => _prefs.Settings.SessionPosition;

    // ── Save ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the current playback session state for restoration on next launch.
    /// </summary>
    public void SaveSession(
        IReadOnlyList<TrackModel> playlist,
        int currentIndex,
        double positionSeconds,
        double volume,
        bool isMuted,
        bool shuffleEnabled,
        RepeatMode repeatMode,
        double windowWidth,
        double windowHeight,
        int windowLeft,
        int windowTop)
    {
        _prefs.Settings.Volume           = volume;
        _prefs.Settings.IsMuted          = isMuted;
        _prefs.Settings.WindowWidth      = windowWidth;
        _prefs.Settings.WindowHeight     = windowHeight;
        _prefs.Settings.WindowLeft       = windowLeft;
        _prefs.Settings.WindowTop        = windowTop;
        _prefs.Settings.IsShuffleEnabled = shuffleEnabled;
        _prefs.Settings.RepeatMode       = repeatMode.ToString();

        _prefs.Settings.SessionQueue        = new List<TrackModel>(playlist);
        _prefs.Settings.SessionCurrentIndex = currentIndex;
        _prefs.Settings.SessionPosition     = positionSeconds;

        _prefs.Save();
    }

    // ── Window Position ───────────────────────────────────────────────────

    /// <summary>
    /// Gets saved window position (left, top), or null if no saved position.
    /// </summary>
    public (int left, int top)? GetSavedWindowPosition()
    {
        if (_prefs.Settings.WindowLeft >= 0 && _prefs.Settings.WindowTop >= 0)
            return ((int)_prefs.Settings.WindowLeft, (int)_prefs.Settings.WindowTop);
        return null;
    }

    /// <summary>
    /// Gets saved window dimensions.
    /// </summary>
    public (double width, double height) GetSavedWindowSize() =>
        (_prefs.Settings.WindowWidth, _prefs.Settings.WindowHeight);
}
