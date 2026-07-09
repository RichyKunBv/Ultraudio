using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ultraudio.Core;

/// <summary>
/// Centralized constants for the entire Ultraudio application.
/// Single source of truth for file extensions, colors, protocol prefixes,
/// and platform capability checks.
/// </summary>
public static class UltraudioConstants
{
    // ── Supported lossless audio extensions ────────────────────────────────
    public static readonly HashSet<string> LosslessExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".flac", ".wav", ".aiff", ".aif", ".dsf", ".dff" };

    // ── File picker filter patterns ───────────────────────────────────────
    public static readonly string[] FilePickerPatterns =
        { "*.flac", "*.wav", "*.aiff", "*.aif", "*.dsf", "*.dff" };

    // ── CD audio protocol prefix ──────────────────────────────────────────
    public const string CdProtocolPrefix = "cda://";

    // ── Theme colors ──────────────────────────────────────────────────────
    public const string AccentGreen    = "#00E576";
    public const string AccentTeal     = "#00C8C8";
    public const string PrimaryGreen   = "#1B6B3A";
    public const string TextPrimary    = "#CCCCCC";
    public const string TextSecondary  = "#888888";
    public const string TextMuted      = "#555555";
    public const string FavoriteActive = "#F5C542";
    public const string FavoriteMuted  = "#333333";

    // ── Default audio settings ────────────────────────────────────────────
    public const int DefaultSampleRate = 44100;
    public const int MinSampleRate     = 8000;
    public const int MaxSampleRate     = 384000;
    public const int DefaultHttpPort   = 7654;

    // ── Platform helpers ──────────────────────────────────────────────────

    /// <summary>
    /// CD Audio playback is only supported on Windows/Linux x64 (requires basscd native library).
    /// </summary>
    public static bool IsCdSupported =>
        (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
         RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) &&
        RuntimeInformation.OSArchitecture == Architecture.X64;
}
