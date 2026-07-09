using System;
using System.Diagnostics;

namespace Ultraudio.Core;

/// <summary>
/// Centralized logging with severity levels, tag filtering, and
/// conditional DEBUG-only output. Replaces scattered Console.WriteLine calls.
/// 
/// Usage:
///   Log.Info("AudioEngine", "Device initialized at 96kHz");
///   Log.Error("CoverArt", "Failed to extract", ex);
///   Log.Debug("Playlist", "Shuffle order recalculated");  // DEBUG builds only
/// </summary>
public static class Log
{
    /// <summary>Informational messages about normal operations.</summary>
    public static void Info(string tag, string message)
    {
        Write("INFO", tag, message);
    }

    /// <summary>Warning about something unexpected but recoverable.</summary>
    public static void Warn(string tag, string message)
    {
        Write("WARN", tag, message);
    }

    /// <summary>Error with optional exception details.</summary>
    public static void Error(string tag, string message, Exception? ex = null)
    {
        Write("ERROR", tag, ex != null ? $"{message} → {ex.Message}" : message);
    }

    /// <summary>
    /// Debug-only output — completely stripped from Release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Debug(string tag, string message)
    {
        Write("DEBUG", tag, message);
    }

    private static void Write(string level, string tag, string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"[{timestamp}] [{level}] [{tag}] {message}");
    }
}
