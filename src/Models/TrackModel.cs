using System;

namespace Ultraudio.Models;

/// <summary>
/// Represents a single audio track in the playlist.
/// Can be a physical file or a virtual track from a CUE sheet.
/// </summary>
public class TrackModel
{
    // ── File / Identity ────────────────────────────────────────────────────
    public string FilePath { get; set; } = string.Empty;

    /// <summary>True when this track is a virtual slice of a CUE sheet.</summary>
    public bool IsCueTrack { get; set; } = false;

    /// <summary>1-based track number inside the CUE sheet (0 if not a CUE track).</summary>
    public int CueTrackNumber { get; set; } = 0;

    /// <summary>Start position in seconds within the parent FLAC (0 if not a CUE track).</summary>
    public double CueStartSeconds { get; set; } = 0;

    /// <summary>End position in seconds (-1 means play to the end of file).</summary>
    public double CueEndSeconds { get; set; } = -1;

    // ── Metadata ───────────────────────────────────────────────────────────
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public int Year { get; set; } = 0;
    public uint TrackNumber { get; set; } = 0;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    // ── Technical Properties ───────────────────────────────────────────────
    public int SampleRate { get; set; } = 0;
    public int BitDepth { get; set; } = 0;
    public int Bitrate { get; set; } = 0;
    public int Channels { get; set; } = 0;
    public string Format { get; set; } = string.Empty;

    // ── ReplayGain ─────────────────────────────────────────────────────────
    public double ReplayGainTrack { get; set; } = double.NaN;
    public double ReplayGainAlbum { get; set; } = double.NaN;

    // ── Cover Art ──────────────────────────────────────────────────────────
    /// <summary>Raw bytes of the embedded cover art image (null if none).</summary>
    public byte[]? CoverArtBytes { get; set; } = null;

    // ── User Data ──────────────────────────────────────────────────────────
    public bool IsFavorite { get; set; } = false;

    // ── Derived / Display ──────────────────────────────────────────────────
    /// <summary>Display name for the playlist (Title → filename fallback).</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : System.IO.Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Short technical badge string, e.g. "FLAC • 24-bit • 96.0 kHz".</summary>
    public string TechBadge
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(Format)) parts.Add(Format);
            if (BitDepth > 0) parts.Add($"{BitDepth}-bit");
            if (SampleRate > 0) parts.Add($"{SampleRate / 1000.0:0.#} kHz");
            if (Bitrate > 0) parts.Add($"{Bitrate} kbps");
            return parts.Count > 0 ? string.Join(" • ", parts) : string.Empty;
        }
    }

    /// <summary>Duration formatted as mm:ss.</summary>
    public string DurationDisplay => Duration.ToString(@"mm\:ss");

    public override string ToString() => DisplayTitle;
}
