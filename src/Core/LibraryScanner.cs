using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagLib;
using Ultraudio.Models;

namespace Ultraudio.Core;

/// <summary>
/// Recursively scans folders and builds a <see cref="TrackModel"/> library with
/// metadata read via TagLib. Supports cancellation and progress reporting.
/// </summary>
public class LibraryScanner
{
    // Uses centralized lossless-only extensions — no lossy formats allowed
    private static readonly HashSet<string> _supportedExtensions = UltraudioConstants.LosslessExtensions;

    public event EventHandler<int>? ProgressChanged; // total files found so far
    public event EventHandler<TrackModel>? TrackScanned;

    /// <summary>
    /// Scans a list of root folders and returns all discovered tracks.
    /// </summary>
    public async Task<List<TrackModel>> ScanAsync(
        IEnumerable<string> rootFolders,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TrackModel>();
        int count = 0;

        await Task.Run(() =>
        {
            foreach (string root in rootFolders)
            {
                if (!Directory.Exists(root)) continue;

                var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => _supportedExtensions.Contains(Path.GetExtension(f)));

                foreach (string filePath in files)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var track = ScanFile(filePath);
                    if (track != null)
                    {
                        results.Add(track);
                        TrackScanned?.Invoke(this, track);
                    }

                    count++;
                    if (count % 25 == 0)
                        ProgressChanged?.Invoke(this, count);
                }
            }
        }, cancellationToken);

        ProgressChanged?.Invoke(this, results.Count);
        Log.Info("Scanner", $"Scanned {results.Count} tracks.");
        return results;
    }

    /// <summary>Scans a single file and returns its TrackModel.</summary>
    public static TrackModel? ScanFile(string filePath)
    {
        try
        {
            var track = new TrackModel
            {
                FilePath = filePath,
                Format = Path.GetExtension(filePath).ToUpper().TrimStart('.')
            };

            using var tagFile = TagLib.File.Create(filePath);

            // ── Tags ────────────────────────────────────────────────────────
            var tag = tagFile.Tag;
            track.Title = tag.Title ?? string.Empty;
            track.Artist = tag.FirstPerformer ?? tag.FirstAlbumArtist ?? string.Empty;
            track.Album = tag.Album ?? string.Empty;
            track.Genre = tag.FirstGenre ?? string.Empty;
            track.Year = (int)tag.Year;
            track.TrackNumber = tag.Track;

            // ReplayGain — read from XiphComment (FLAC/OGG Vorbis comment blocks)
            try
            {
                var xiphTag = tagFile.Tag as TagLib.Ogg.XiphComment;
                // For FLAC files, TagLibSharp exposes XiphComment in a Combined tag
                if (xiphTag == null && tagFile.Tag is TagLib.NonContainer.Tag combinedTag)
                {
                    // Walk tags in the combined tag to find XiphComment
                    foreach (var sub in combinedTag.Tags)
                        if (sub is TagLib.Ogg.XiphComment x) { xiphTag = x; break; }
                }
                if (xiphTag != null)
                {
                    var rgTrack = xiphTag.GetFirstField("REPLAYGAIN_TRACK_GAIN");
                    var rgAlbum = xiphTag.GetFirstField("REPLAYGAIN_ALBUM_GAIN");
                    if (rgTrack != null && double.TryParse(rgTrack.Replace(" dB", ""), out double rt))
                        track.ReplayGainTrack = rt;
                    if (rgAlbum != null && double.TryParse(rgAlbum.Replace(" dB", ""), out double ra))
                        track.ReplayGainAlbum = ra;
                }
            }
            catch { /* ReplayGain read is best-effort */ }

            // ── Technical info ──────────────────────────────────────────────
            var props = tagFile.Properties;
            track.SampleRate = props.AudioSampleRate;
            track.BitDepth = props.BitsPerSample;
            track.Bitrate = props.AudioBitrate;
            track.Channels = props.AudioChannels;
            track.Duration = props.Duration;

            return track;
        }
        catch (Exception ex)
        {
            Log.Warn("Scanner", $"Error scanning {Path.GetFileName(filePath)}: {ex.Message}");
            // Return minimal model so the file is still playable
            return new TrackModel
            {
                FilePath = filePath,
                Format = Path.GetExtension(filePath).ToUpper().TrimStart('.')
            };
        }
    }
}
