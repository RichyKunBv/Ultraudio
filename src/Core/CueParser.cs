using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Ultraudio.Models;

namespace Ultraudio.Core;

/// <summary>
/// Parses standard .cue sheet files and returns a list of virtual <see cref="TrackModel"/>
/// objects, each pointing to the same physical audio file but with distinct
/// CueStartSeconds / CueEndSeconds bounds.
/// </summary>
public static class CueParser
{
    private static readonly Regex _fileRegex   = new(@"FILE\s+""?(.+?)""?\s+\w+", RegexOptions.IgnoreCase);
    private static readonly Regex _trackRegex  = new(@"TRACK\s+(\d+)\s+AUDIO", RegexOptions.IgnoreCase);
    private static readonly Regex _titleRegex  = new(@"TITLE\s+""?(.+?)""?\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex _artistRegex = new(@"PERFORMER\s+""?(.+?)""?\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex _albumRegex  = new(@"TITLE\s+""?(.+?)""?\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex _indexRegex  = new(@"INDEX\s+01\s+(\d{2}):(\d{2}):(\d{2})", RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse a .cue file and return virtual tracks.
    /// </summary>
    /// <param name="cuePath">Absolute path to the .cue file.</param>
    /// <returns>
    /// List of <see cref="TrackModel"/> objects.
    /// Returns an empty list on failure.
    /// </returns>
    public static List<TrackModel> Parse(string cuePath)
    {
        var tracks = new List<TrackModel>();

        if (!File.Exists(cuePath))
        {
            Console.WriteLine($"[CUE] File not found: {cuePath}");
            return tracks;
        }

        string cueDir = Path.GetDirectoryName(cuePath) ?? string.Empty;
        string[] lines = File.ReadAllLines(cuePath);

        string currentAudioFile = string.Empty;
        string albumTitle = string.Empty;
        string albumArtist = string.Empty;

        TrackModel? currentTrack = null;
        bool inTrackBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            // ── FILE directive ─────────────────────────────────────────────
            var fileMatch = _fileRegex.Match(line);
            if (fileMatch.Success)
            {
                string rawName = fileMatch.Groups[1].Value.Trim();
                currentAudioFile = Path.Combine(cueDir, rawName);
                if (!File.Exists(currentAudioFile))
                {
                    // Try without path component
                    currentAudioFile = Path.Combine(cueDir, Path.GetFileName(rawName));
                }
                inTrackBlock = false;
                continue;
            }

            // ── TRACK directive ────────────────────────────────────────────
            var trackMatch = _trackRegex.Match(line);
            if (trackMatch.Success)
            {
                if (currentTrack != null)
                    tracks.Add(currentTrack);

                currentTrack = new TrackModel
                {
                    FilePath = currentAudioFile,
                    IsCueTrack = true,
                    CueTrackNumber = int.Parse(trackMatch.Groups[1].Value),
                    Album = albumTitle,
                    Artist = albumArtist,
                    Format = Path.GetExtension(currentAudioFile).ToUpper().TrimStart('.')
                };
                inTrackBlock = true;
                continue;
            }

            // ── Header-level TITLE (album) ─────────────────────────────────
            if (!inTrackBlock && line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            {
                var m = _albumRegex.Match(line);
                if (m.Success) albumTitle = m.Groups[1].Value;
                continue;
            }

            // ── Header-level PERFORMER (album artist) ──────────────────────
            if (!inTrackBlock && line.StartsWith("PERFORMER", StringComparison.OrdinalIgnoreCase))
            {
                var m = _artistRegex.Match(line);
                if (m.Success) albumArtist = m.Groups[1].Value;
                continue;
            }

            // ── Inside a TRACK block ───────────────────────────────────────
            if (currentTrack != null && inTrackBlock)
            {
                // TITLE
                if (line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
                {
                    var m = _titleRegex.Match(line);
                    if (m.Success) currentTrack.Title = m.Groups[1].Value;
                    continue;
                }

                // PERFORMER
                if (line.StartsWith("PERFORMER", StringComparison.OrdinalIgnoreCase))
                {
                    var m = _artistRegex.Match(line);
                    if (m.Success) currentTrack.Artist = m.Groups[1].Value;
                    continue;
                }

                // INDEX 01 → start position
                var indexMatch = _indexRegex.Match(line);
                if (indexMatch.Success)
                {
                    int mm = int.Parse(indexMatch.Groups[1].Value);
                    int ss = int.Parse(indexMatch.Groups[2].Value);
                    int ff = int.Parse(indexMatch.Groups[3].Value); // frames (1/75 sec)
                    currentTrack.CueStartSeconds = mm * 60 + ss + ff / 75.0;
                    continue;
                }
            }
        }

        // Add the last track
        if (currentTrack != null)
            tracks.Add(currentTrack);

        // ── Set end times ──────────────────────────────────────────────────
        // Each track ends where the next one begins; last track plays to file end.
        for (int i = 0; i < tracks.Count - 1; i++)
            tracks[i].CueEndSeconds = tracks[i + 1].CueStartSeconds;

        tracks[^1].CueEndSeconds = -1; // Last track: play to file end

        // ── Propagate album-level metadata ────────────────────────────────
        foreach (var t in tracks)
        {
            if (string.IsNullOrWhiteSpace(t.Artist) && !string.IsNullOrWhiteSpace(albumArtist))
                t.Artist = albumArtist;
            if (string.IsNullOrWhiteSpace(t.Album) && !string.IsNullOrWhiteSpace(albumTitle))
                t.Album = albumTitle;
            if (string.IsNullOrWhiteSpace(t.Title))
                t.Title = $"Track {t.CueTrackNumber:D2}";
        }

        Console.WriteLine($"[CUE] Parsed {tracks.Count} tracks from: {Path.GetFileName(cuePath)}");
        return tracks;
    }

    // ── Helper: MM:SS:FF → seconds ────────────────────────────────────────────
    public static double TimecodeToSeconds(int mm, int ss, int ff)
        => mm * 60 + ss + ff / 75.0;
}
