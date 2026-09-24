using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Ultraudio.Core;

/// <summary>
/// Centralized logging with severity levels, file persistence, ring buffer for UI display,
/// and conditional DEBUG-only output.
/// </summary>
public static class Log
{
    private static readonly object _lock = new();
    private static readonly Queue<string> _recentLogs = new();
    private const int MaxMemoryLogs = 1000;
    private const long MaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly string _logDir;
    private static readonly string _logFilePath;

    public static event Action<string>? MessageLogged;

    static Log()
    {
        try
        {
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);

            _logDir = Path.Combine(appData, "Ultraudio", "logs");
            Directory.CreateDirectory(_logDir);
            _logFilePath = Path.Combine(_logDir, "ultraudio.log");

            // Check if rotation needed
            RotateLogIfNeeded();

            LoadRecentLogsFromFile();
            
            // Initial session header
            string header = $"=== Ultraudio {AppInfo.VersionDisplay} Sesión Iniciada: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({Environment.OSVersion.Platform} {Environment.OSVersion.VersionString}) ===";
            WriteToFile(header);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] [LogInit] No se pudo inicializar archivo de logs: {ex.Message}");
            _logDir = string.Empty;
            _logFilePath = string.Empty;
        }
    }

    public static string LogFilePath => _logFilePath;
    public static string LogDirectory => _logDir;

    /// <summary>Informational messages about normal operations.</summary>
    public static void Info(string tag, string message) => Write("INFO", tag, message);

    /// <summary>Warning about something unexpected but recoverable.</summary>
    public static void Warn(string tag, string message) => Write("WARN", tag, message);

    /// <summary>Error with optional exception details.</summary>
    public static void Error(string tag, string message, Exception? ex = null)
    {
        string fullMsg = ex != null ? $"{message} → {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        Write("ERROR", tag, fullMsg);
    }

    /// <summary>Debug-only output — completely stripped from Release builds.</summary>
    [Conditional("DEBUG")]
    public static void Debug(string tag, string message) => Write("DEBUG", tag, message);

    private static void Write(string level, string tag, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"[{timestamp}] [{level}] [{tag}] {message}";

        // Write to standard output
        Console.WriteLine(line);

        lock (_lock)
        {
            // Add to in-memory buffer
            if (_recentLogs.Count >= MaxMemoryLogs)
            {
                _recentLogs.Dequeue();
            }
            _recentLogs.Enqueue(line);

            // Write to file
            WriteToFile(line);
        }

        try
        {
            MessageLogged?.Invoke(line);
        }
        catch { /* best-effort notification */ }
    }

    private static void WriteToFile(string line)
    {
        if (string.IsNullOrEmpty(_logFilePath)) return;

        try
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* ignore file write errors to prevent recursive exceptions */ }
    }

    private static void RotateLogIfNeeded()
    {
        if (string.IsNullOrEmpty(_logFilePath) || !File.Exists(_logFilePath)) return;

        try
        {
            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length > MaxLogFileSizeBytes)
            {
                string oldLog = Path.Combine(_logDir, "ultraudio.old.log");
                if (File.Exists(oldLog))
                {
                    File.Delete(oldLog);
                }
                File.Move(_logFilePath, oldLog);
            }
        }
        catch { /* rotation best-effort */ }
    }

    /// <summary>Returns in-memory snapshot of recent logs.</summary>
    public static string[] GetRecentLogs()
    {
        lock (_lock)
        {
            return _recentLogs.ToArray();
        }
    }

    private static void LoadRecentLogsFromFile()
    {
        if (string.IsNullOrEmpty(_logFilePath) || !File.Exists(_logFilePath)) return;

        try
        {
            foreach (string line in File.ReadLines(_logFilePath))
            {
                if (_recentLogs.Count >= MaxMemoryLogs)
                    _recentLogs.Dequeue();

                _recentLogs.Enqueue(line);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] [LogInit] No se pudieron cargar logs anteriores: {ex.Message}");
        }
    }

    /// <summary>Clears both memory and log file.</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _recentLogs.Clear();
            if (!string.IsNullOrEmpty(_logFilePath) && File.Exists(_logFilePath))
            {
                try
                {
                    File.WriteAllText(_logFilePath, string.Empty);
                }
                catch { }
            }
        }
    }
}
