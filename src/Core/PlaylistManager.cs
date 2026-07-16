using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ultraudio.Models;

namespace Ultraudio.Core;

/// <summary>
/// Manages the playlist state: track list, filtered view, shuffle order,
/// current index, navigation, and favorites. Extracted from MainWindow
/// to keep UI and playlist logic separate.
/// </summary>
public class PlaylistManager
{
    // ── Collections ───────────────────────────────────────────────────────
    private readonly List<TrackModel> _tracks = new();
    private readonly ObservableCollection<PlaylistItemViewModel> _allItems = new();
    private readonly ObservableCollection<PlaylistItemViewModel> _filteredItems = new();

    // ── Navigation state ──────────────────────────────────────────────────
    private int _currentIndex = -1;
    private int[] _shuffleOrder = Array.Empty<int>();
    private int _shufflePos = 0;
    private bool _shuffleEnabled = false;
    private RepeatMode _repeatMode = RepeatMode.Off;
    private string _searchText = string.Empty;

    // ── Favorites persistence callback ────────────────────────────────────
    private readonly PreferencesManager _prefs;

    // ── Public read-only accessors ────────────────────────────────────────
    public IReadOnlyList<TrackModel> Tracks => _tracks;
    public ObservableCollection<PlaylistItemViewModel> AllItems => _allItems;
    public ObservableCollection<PlaylistItemViewModel> FilteredItems => _filteredItems;
    public int CurrentIndex => _currentIndex;
    public int Count => _tracks.Count;
    public int FilteredCount => _filteredItems.Count;
    public TrackModel? CurrentTrack => _currentIndex >= 0 && _currentIndex < _tracks.Count
        ? _tracks[_currentIndex]
        : null;

    public bool ShuffleEnabled
    {
        get => _shuffleEnabled;
        set
        {
            _shuffleEnabled = value;
            if (value) BuildShuffleOrder();
        }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set => _repeatMode = value;
    }

    // ── Constructor ───────────────────────────────────────────────────────

    public PlaylistManager(PreferencesManager prefs)
    {
        _prefs = prefs;
    }

    // ── Load / Append ─────────────────────────────────────────────────────

    /// <summary>
    /// Loads tracks into the playlist. Can append or replace.
    /// Returns the start index of newly added tracks.
    /// </summary>
    public int LoadTracks(List<TrackModel> tracks, bool append = false)
    {
        if (!append)
        {
            _tracks.Clear();
            _allItems.Clear();
        }

        int startIndex = _tracks.Count;
        _tracks.AddRange(tracks);

        foreach (var t in tracks)
        {
            t.IsFavorite = _prefs.Settings.Favorites.Contains(t.FilePath);
            _allItems.Add(new PlaylistItemViewModel(t));
        }

        ApplySearchFilter(_searchText);

        if (_shuffleEnabled) BuildShuffleOrder();

        return startIndex;
    }

    // ── Clear ─────────────────────────────────────────────────────────────

    public void Clear()
    {
        _tracks.Clear();
        _allItems.Clear();
        _filteredItems.Clear();
        _currentIndex = -1;
    }

    // ── Navigation ────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the current playing index and updates visual indicators.
    /// </summary>
    public void SetCurrentIndex(int index)
    {
        if (index < 0 || index >= _tracks.Count) return;

        // Clear old playing indicator
        if (_currentIndex >= 0 && _currentIndex < _allItems.Count)
            _allItems[_currentIndex].IsPlaying = false;

        _currentIndex = index;

        // Set new playing indicator
        if (index < _allItems.Count)
            _allItems[index].IsPlaying = true;
    }

    /// <summary>
    /// Returns the index of the next track to play, or -1 if none.
    /// </summary>
    public int GetNextIndex()
    {
        if (_tracks.Count == 0) return -1;

        if (_repeatMode == RepeatMode.One)
            return _currentIndex;

        if (_shuffleEnabled && _shuffleOrder.Length > 0)
        {
            if (_shufflePos + 1 < _shuffleOrder.Length)
            {
                _shufflePos++;
                return _shuffleOrder[_shufflePos];
            }
            
            if (_repeatMode == RepeatMode.All)
            {
                _shufflePos = 0;
                return _shuffleOrder[_shufflePos];
            }
            
            return -1;
        }

        if (_currentIndex + 1 < _tracks.Count)
            return _currentIndex + 1;

        if (_repeatMode == RepeatMode.All)
            return 0;

        return -1;
    }

    /// <summary>
    /// Returns the index of the next track for gapless preload (peek, no side effects).
    /// </summary>
    public int PeekNextIndex()
    {
        if (_tracks.Count == 0) return -1;

        if (_shuffleEnabled && _shuffleOrder.Length > 0)
        {
            if (_shufflePos + 1 < _shuffleOrder.Length)
                return _shuffleOrder[_shufflePos + 1];
                
            if (_repeatMode == RepeatMode.All)
                return _shuffleOrder[0];
                
            return -1;
        }

        if (_currentIndex + 1 < _tracks.Count)
            return _currentIndex + 1;

        if (_repeatMode == RepeatMode.All)
            return 0;

        return -1;
    }

    /// <summary>
    /// Returns the index of the previous track, or -1 if at the beginning.
    /// </summary>
    public int GetPreviousIndex()
    {
        if (_shuffleEnabled)
        {
            if (_shufflePos > 0)
            {
                _shufflePos--;
                return _shuffleOrder[_shufflePos];
            }
            return -1; // Caller should reset position to 0
        }

        if (_currentIndex > 0)
            return _currentIndex - 1;

        return -1;  // Caller should reset position to 0
    }

    // ── Reorder ───────────────────────────────────────────────────────────

    public bool MoveUp(PlaylistItemViewModel vm)
    {
        int idx = _allItems.IndexOf(vm);
        if (idx <= 0) return false;

        var track = _tracks[idx];
        _tracks.RemoveAt(idx);
        _tracks.Insert(idx - 1, track);

        _allItems.RemoveAt(idx);
        _allItems.Insert(idx - 1, vm);

        if (_currentIndex == idx) _currentIndex = idx - 1;
        else if (_currentIndex == idx - 1) _currentIndex = idx;

        if (_shuffleEnabled) BuildShuffleOrder();
        return true;
    }

    public bool MoveDown(PlaylistItemViewModel vm)
    {
        int idx = _allItems.IndexOf(vm);
        if (idx < 0 || idx >= _allItems.Count - 1) return false;

        var track = _tracks[idx];
        _tracks.RemoveAt(idx);
        _tracks.Insert(idx + 1, track);

        _allItems.RemoveAt(idx);
        _allItems.Insert(idx + 1, vm);

        if (_currentIndex == idx) _currentIndex = idx + 1;
        else if (_currentIndex == idx + 1) _currentIndex = idx;

        if (_shuffleEnabled) BuildShuffleOrder();
        return true;
    }

    // ── Search / Filter ───────────────────────────────────────────────────

    public void ApplySearchFilter(string query)
    {
        _searchText = query;
        _filteredItems.Clear();
        foreach (var item in _allItems)
        {
            if (string.IsNullOrWhiteSpace(query) ||
                item.Track.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Track.Album.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _filteredItems.Add(item);
            }
        }
    }

    // ── Favorites ─────────────────────────────────────────────────────────

    public void ToggleFavorite(int idx)
    {
        if (idx < 0 || idx >= _tracks.Count) return;

        var track = _tracks[idx];
        track.IsFavorite = !track.IsFavorite;

        if (track.IsFavorite)
            _prefs.Settings.Favorites.Add(track.FilePath);
        else
            _prefs.Settings.Favorites.Remove(track.FilePath);

        _prefs.Save();

        // Update VMs
        foreach (var vm in _allItems.Where(v => v.Track == track))
            vm.RefreshFav();
    }

    // ── Shuffle ───────────────────────────────────────────────────────────

    private void BuildShuffleOrder()
    {
        var order = Enumerable.Range(0, _tracks.Count).ToList();
        var rng = new Random();
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        // Put current index first if playing
        if (_currentIndex >= 0)
        {
            order.Remove(_currentIndex);
            order.Insert(0, _currentIndex);
        }
        _shuffleOrder = order.ToArray();
        _shufflePos = 0;
    }

    // ── Playlist status display ───────────────────────────────────────────

    public string PlaylistCountText =>
        _tracks.Count > 0 ? $"{_filteredItems.Count} tracks" : string.Empty;

    /// <summary>
    /// Gets the PlaylistItemViewModel for the current track, if any.
    /// </summary>
    public PlaylistItemViewModel? GetCurrentViewModel()
    {
        if (_currentIndex >= 0 && _currentIndex < _allItems.Count)
            return _allItems[_currentIndex];
        return null;
    }

    /// <summary>
    /// Finds the playlist index for a given track.
    /// </summary>
    public int IndexOf(TrackModel track) => _tracks.IndexOf(track);
}
