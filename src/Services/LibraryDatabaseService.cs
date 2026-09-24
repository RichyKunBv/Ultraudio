using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Ultraudio.Core;
using Ultraudio.Models;

namespace Ultraudio.Services;

/// <summary>
/// High-performance SQLite database service for the Ultraudio music library.
/// Caches track metadata and audio specifications for instant querying and persistence.
/// </summary>
public class LibraryDatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public LibraryDatabaseService(string? customDbPath = null)
    {
        if (!string.IsNullOrEmpty(customDbPath))
        {
            _dbPath = customDbPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);
            string appDir = Path.Combine(appData, "Ultraudio");
            Directory.CreateDirectory(appDir);
            _dbPath = Path.Combine(appDir, "library.db");
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeDatabase();
    }

    public string DatabasePath => _dbPath;

    private void InitializeDatabase()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // Enable WAL mode for better concurrency and write speed
            using (var walCmd = conn.CreateCommand())
            {
                walCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                walCmd.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS tracks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_path TEXT UNIQUE NOT NULL,
                    title TEXT,
                    artist TEXT,
                    album TEXT,
                    genre TEXT,
                    year INTEGER,
                    track_number INTEGER,
                    duration_seconds REAL,
                    sample_rate INTEGER,
                    bit_depth INTEGER,
                    bitrate INTEGER,
                    channels INTEGER,
                    replay_gain_track REAL,
                    replay_gain_album REAL,
                    format TEXT,
                    last_modified INTEGER,
                    date_added INTEGER
                );

                CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);
                CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album);
                CREATE INDEX IF NOT EXISTS idx_tracks_title ON tracks(title);
                CREATE INDEX IF NOT EXISTS idx_tracks_path ON tracks(file_path);
            ";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Error("LibraryDB", "Failed to initialize SQLite database", ex);
        }
    }

    public async Task<int> GetTrackCountAsync()
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM tracks;";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            Log.Error("LibraryDB", "Error getting track count", ex);
            return 0;
        }
    }

    public async Task<Dictionary<string, long>> GetTrackModificationTimesAsync()
    {
        var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT file_path, last_modified FROM tracks;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string path = reader.GetString(0);
                long modified = reader.GetInt64(1);
                dict[path] = modified;
            }
        }
        catch (Exception ex)
        {
            Log.Error("LibraryDB", "Error getting modification times", ex);
        }
        return dict;
    }

    public async Task<List<TrackModel>> GetAllTracksAsync(string? searchQuery = null)
    {
        var results = new List<TrackModel>();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                cmd.CommandText = @"
                    SELECT file_path, title, artist, album, genre, year, track_number,
                           duration_seconds, sample_rate, bit_depth, bitrate, channels,
                           replay_gain_track, replay_gain_album, format
                    FROM tracks
                    ORDER BY 
                        CASE WHEN artist IS NULL OR artist = '' THEN 1 ELSE 0 END, artist COLLATE NOCASE ASC,
                        CASE WHEN album IS NULL OR album = '' THEN 1 ELSE 0 END, album COLLATE NOCASE ASC,
                        track_number ASC, title COLLATE NOCASE ASC;
                ";
            }
            else
            {
                cmd.CommandText = @"
                    SELECT file_path, title, artist, album, genre, year, track_number,
                           duration_seconds, sample_rate, bit_depth, bitrate, channels,
                           replay_gain_track, replay_gain_album, format
                    FROM tracks
                    WHERE title LIKE @q OR artist LIKE @q OR album LIKE @q OR genre LIKE @q OR file_path LIKE @q
                    ORDER BY artist COLLATE NOCASE ASC, album COLLATE NOCASE ASC, track_number ASC;
                ";
                cmd.Parameters.AddWithValue("@q", $"%{searchQuery.Trim()}%");
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var track = new TrackModel
                {
                    FilePath = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Artist = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Album = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Genre = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Year = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    TrackNumber = reader.IsDBNull(6) ? 0 : (uint)reader.GetInt32(6),
                    Duration = reader.IsDBNull(7) ? TimeSpan.Zero : TimeSpan.FromSeconds(reader.GetDouble(7)),
                    SampleRate = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    BitDepth = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    Bitrate = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    Channels = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    ReplayGainTrack = reader.IsDBNull(12) ? double.NaN : reader.GetDouble(12),
                    ReplayGainAlbum = reader.IsDBNull(13) ? double.NaN : reader.GetDouble(13),
                    Format = reader.IsDBNull(14) ? string.Empty : reader.GetString(14)
                };
                results.Add(track);
            }
        }
        catch (Exception ex)
        {
            Log.Error("LibraryDB", "Error querying tracks", ex);
        }

        return results;
    }

    public async Task UpsertTracksAsync(IEnumerable<(TrackModel track, long lastModified)> items)
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var transaction = (SqliteTransaction)await conn.BeginTransactionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO tracks (
                    file_path, title, artist, album, genre, year, track_number,
                    duration_seconds, sample_rate, bit_depth, bitrate, channels,
                    replay_gain_track, replay_gain_album, format, last_modified, date_added
                ) VALUES (
                    @file_path, @title, @artist, @album, @genre, @year, @track_number,
                    @duration_seconds, @sample_rate, @bit_depth, @bitrate, @channels,
                    @replay_gain_track, @replay_gain_album, @format, @last_modified, @date_added
                )
                ON CONFLICT(file_path) DO UPDATE SET
                    title = excluded.title,
                    artist = excluded.artist,
                    album = excluded.album,
                    genre = excluded.genre,
                    year = excluded.year,
                    track_number = excluded.track_number,
                    duration_seconds = excluded.duration_seconds,
                    sample_rate = excluded.sample_rate,
                    bit_depth = excluded.bit_depth,
                    bitrate = excluded.bitrate,
                    channels = excluded.channels,
                    replay_gain_track = excluded.replay_gain_track,
                    replay_gain_album = excluded.replay_gain_album,
                    format = excluded.format,
                    last_modified = excluded.last_modified;
            ";

            var pFilePath = cmd.Parameters.Add("@file_path", SqliteType.Text);
            var pTitle = cmd.Parameters.Add("@title", SqliteType.Text);
            var pArtist = cmd.Parameters.Add("@artist", SqliteType.Text);
            var pAlbum = cmd.Parameters.Add("@album", SqliteType.Text);
            var pGenre = cmd.Parameters.Add("@genre", SqliteType.Text);
            var pYear = cmd.Parameters.Add("@year", SqliteType.Integer);
            var pTrackNumber = cmd.Parameters.Add("@track_number", SqliteType.Integer);
            var pDuration = cmd.Parameters.Add("@duration_seconds", SqliteType.Real);
            var pSampleRate = cmd.Parameters.Add("@sample_rate", SqliteType.Integer);
            var pBitDepth = cmd.Parameters.Add("@bit_depth", SqliteType.Integer);
            var pBitrate = cmd.Parameters.Add("@bitrate", SqliteType.Integer);
            var pChannels = cmd.Parameters.Add("@channels", SqliteType.Integer);
            var pRgTrack = cmd.Parameters.Add("@replay_gain_track", SqliteType.Real);
            var pRgAlbum = cmd.Parameters.Add("@replay_gain_album", SqliteType.Real);
            var pFormat = cmd.Parameters.Add("@format", SqliteType.Text);
            var pLastMod = cmd.Parameters.Add("@last_modified", SqliteType.Integer);
            var pDateAdded = cmd.Parameters.Add("@date_added", SqliteType.Integer);

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var (track, lastModified) in items)
            {
                pFilePath.Value = track.FilePath;
                pTitle.Value = (object?)track.Title ?? DBNull.Value;
                pArtist.Value = (object?)track.Artist ?? DBNull.Value;
                pAlbum.Value = (object?)track.Album ?? DBNull.Value;
                pGenre.Value = (object?)track.Genre ?? DBNull.Value;
                pYear.Value = track.Year;
                pTrackNumber.Value = (int)track.TrackNumber;
                pDuration.Value = track.Duration.TotalSeconds;
                pSampleRate.Value = track.SampleRate;
                pBitDepth.Value = track.BitDepth;
                pBitrate.Value = track.Bitrate;
                pChannels.Value = track.Channels;
                pRgTrack.Value = double.IsNaN(track.ReplayGainTrack) ? DBNull.Value : track.ReplayGainTrack;
                pRgAlbum.Value = double.IsNaN(track.ReplayGainAlbum) ? DBNull.Value : track.ReplayGainAlbum;
                pFormat.Value = (object?)track.Format ?? DBNull.Value;
                pLastMod.Value = lastModified;
                pDateAdded.Value = now;

                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            Log.Error("LibraryDB", "Failed to batch upsert tracks", ex);
        }
    }

    public async Task RemoveTracksUnderFolderAsync(string folderPath)
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            string normalizedFolder = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            cmd.CommandText = "DELETE FROM tracks WHERE file_path = @folder OR file_path LIKE @folderPrefix;";
            cmd.Parameters.AddWithValue("@folder", normalizedFolder);
            cmd.Parameters.AddWithValue("@folderPrefix", normalizedFolder + Path.DirectorySeparatorChar + "%");
            int deleted = await cmd.ExecuteNonQueryAsync();
            Log.Info("LibraryDB", $"Removed {deleted} tracks under removed folder: {folderPath}");
        }
        catch (Exception ex)
        {
            Log.Error("LibraryDB", $"Error removing tracks for folder {folderPath}", ex);
        }
    }

    public async Task RemoveMissingTracksAsync(HashSet<string> existingValidPaths)
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var pathsToDelete = new List<string>();
            await using (var readCmd = conn.CreateCommand())
            {
                readCmd.CommandText = "SELECT file_path FROM tracks;";
                await using var reader = await readCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string path = reader.GetString(0);
                    if (!existingValidPaths.Contains(path) && !File.Exists(path))
                    {
                        pathsToDelete.Add(path);
                    }
                }
            }

            if (pathsToDelete.Count > 0)
            {
                await using var trans = (SqliteTransaction)await conn.BeginTransactionAsync();
                await using var delCmd = conn.CreateCommand();
                delCmd.Transaction = trans;
                delCmd.CommandText = "DELETE FROM tracks WHERE file_path = @p;";
                var p = delCmd.Parameters.Add("@p", SqliteType.Text);

                foreach (var path in pathsToDelete)
                {
                    p.Value = path;
                    await delCmd.ExecuteNonQueryAsync();
                }

                await trans.CommitAsync();
                Log.Info("LibraryDB", $"Cleaned up {pathsToDelete.Count} missing tracks.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("LibraryDB", "Error cleaning up missing tracks", ex);
        }
    }

    public async Task ClearAllAsync()
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM tracks; VACUUM;";
            await cmd.ExecuteNonQueryAsync();
            Log.Info("LibraryDB", "Database cleared and vacuumed.");
        }
        catch (Exception ex)
        {
            Log.Error("LibraryDB", "Error clearing database", ex);
        }
    }
}
