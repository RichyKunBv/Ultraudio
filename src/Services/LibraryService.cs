using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ultraudio.Core;
using Ultraudio.Models;

namespace Ultraudio.Services;

/// <summary>
/// Automatic music library service. Coordinates background folder scanning,
/// SQLite persistence, and file system monitoring.
/// </summary>
public class LibraryService : IDisposable
{
    private readonly LibraryDatabaseService _db;
    private readonly PreferencesManager _prefs;
    private readonly List<FileSystemWatcher> _watchers = new();
    private System.Timers.Timer? _debounceTimer;
    private readonly object _syncLock = new();
    private bool _isScanning = false;
    private bool _scanRequested;

    public event Action? ScanStarted;
    public event Action<int, int>? ProgressChanged; // current, total
    public event Action<int>? ScanCompleted; // total tracks in DB

    public bool IsScanning => _isScanning;
    public LibraryDatabaseService Database => _db;

    public LibraryService(PreferencesManager prefs, LibraryDatabaseService? db = null)
    {
        _prefs = prefs;
        _db = db ?? new LibraryDatabaseService();

        // Setup debounce timer for filesystem watcher events (wait 3 seconds after last file change)
        _debounceTimer = new System.Timers.Timer(3000)
        {
            AutoReset = false
        };
        _debounceTimer.Elapsed += async (_, _) =>
        {
            Log.Info("LibraryService", "FileSystem change detected. Auto-synchronizing library...");
            await ScanLibraryAsync();
        };

        UpdateWatchers();
    }

    /// <summary>Returns all library folders configured in AppSettings.</summary>
    public IReadOnlyList<string> GetLibraryFolders() => _prefs.Settings.LibraryFolders;

    /// <summary>Adds a folder to the library and triggers a scan.</summary>
    public async Task AddFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        string normalized = Path.GetFullPath(folderPath);

        lock (_syncLock)
        {
            if (!_prefs.Settings.LibraryFolders.Any(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                _prefs.Settings.LibraryFolders.Add(normalized);
                _prefs.Save();
            }
        }

        UpdateWatchers();
        await ScanLibraryAsync();
    }

    /// <summary>Removes a folder from the library and cleans up tracks from DB.</summary>
    public async Task RemoveFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        string normalized = Path.GetFullPath(folderPath);

        lock (_syncLock)
        {
            _prefs.Settings.LibraryFolders.RemoveAll(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase));
            _prefs.Save();
        }

        UpdateWatchers();
        await _db.RemoveTracksUnderFolderAsync(normalized);
        int remaining = await _db.GetTrackCountAsync();
        ScanCompleted?.Invoke(remaining);
    }

    /// <summary>Configures FileSystemWatchers for all configured folders to make the library automatic.</summary>
    public void UpdateWatchers()
    {
        lock (_syncLock)
        {
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();

            foreach (var folder in _prefs.Settings.LibraryFolders)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
                    };

                    watcher.Created += OnFileSystemEvent;
                    watcher.Deleted += OnFileSystemEvent;
                    watcher.Renamed += OnFileSystemEvent;
                    watcher.Changed += OnFileSystemEvent;
                    watcher.EnableRaisingEvents = true;

                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    Log.Warn("LibraryService", $"Failed to watch folder '{folder}': {ex.Message}");
                }
            }
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        // Check if event is for a supported audio format
        string ext = Path.GetExtension(e.FullPath);
        if (UltraudioConstants.LosslessExtensions.Contains(ext) || Directory.Exists(e.FullPath))
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        }
    }

    /// <summary>
    /// Scans all configured folders, updates SQLite DB incrementally, and removes dead tracks.
    /// </summary>
    public async Task ScanLibraryAsync(bool forceRescan = false, CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            if (_isScanning)
            {
                _scanRequested = true;
                return;
            }

            _isScanning = true;
        }

        ScanStarted?.Invoke();
        Log.Info("LibraryService", "Starting automatic library scan...");

        try
        {
            var folders = _prefs.Settings.LibraryFolders.Where(Directory.Exists).ToList();
            if (folders.Count == 0)
            {
                _isScanning = false;
                int count = await _db.GetTrackCountAsync();
                ScanCompleted?.Invoke(count);
                return;
            }

            var cachedModTimes = forceRescan ? new Dictionary<string, long>() : await _db.GetTrackModificationTimesAsync();
            var supported = UltraudioConstants.LosslessExtensions;
            var discoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await Task.Run(async () =>
            {
                // 1. Gather all files across all folders
                var filesToProcess = new List<string>();
                foreach (var folder in folders)
                {
                    try
                    {
                        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                            .Where(f => supported.Contains(Path.GetExtension(f)) && !Path.GetFileName(f).StartsWith("._"));
                        
                        foreach (var f in files)
                        {
                            discoveredFiles.Add(f);
                            filesToProcess.Add(f);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("LibraryService", $"Error reading directory {folder}: {ex.Message}");
                    }
                }

                int total = filesToProcess.Count;
                int current = 0;
                var batch = new List<(TrackModel track, long lastMod)>();

                // 2. Process files: only inspect TagLib metadata if new or modified
                foreach (var filePath in filesToProcess)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var lastWrite = File.GetLastWriteTimeUtc(filePath).Ticks;

                        if (cachedModTimes.TryGetValue(filePath, out long cachedWrite) && cachedWrite == lastWrite)
                        {
                            // Unchanged, already in DB
                            current++;
                            if (current % 50 == 0)
                                ProgressChanged?.Invoke(current, total);
                            continue;
                        }

                        // Read metadata
                        var track = LibraryScanner.ScanFile(filePath);
                        if (track != null)
                        {
                            batch.Add((track, lastWrite));
                        }

                        if (batch.Count >= 50)
                        {
                            await _db.UpsertTracksAsync(batch);
                            batch.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("LibraryService", $"Error processing file {filePath}: {ex.Message}");
                    }

                    current++;
                    if (current % 20 == 0 || current == total)
                    {
                        ProgressChanged?.Invoke(current, total);
                    }
                }

                if (batch.Count > 0)
                {
                    await _db.UpsertTracksAsync(batch);
                    batch.Clear();
                }

                // 3. Clean up deleted tracks
                await _db.RemoveMissingTracksAsync(discoveredFiles);

            }, cancellationToken);

            int finalCount = await _db.GetTrackCountAsync();
            Log.Info("LibraryService", $"Library scan completed. Total tracks in SQLite: {finalCount}");
            ScanCompleted?.Invoke(finalCount);
        }
        catch (Exception ex)
        {
            Log.Error("LibraryService", "Error during library scan", ex);
        }
        finally
        {
            bool scanAgain;
            lock (_syncLock)
            {
                scanAgain = _scanRequested;
                _scanRequested = false;
                _isScanning = false;
            }

            if (scanAgain && !cancellationToken.IsCancellationRequested)
                _ = ScanLibraryAsync(forceRescan: false, cancellationToken);
        }
    }

    public void Dispose()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        lock (_syncLock)
        {
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
        }
    }
}
