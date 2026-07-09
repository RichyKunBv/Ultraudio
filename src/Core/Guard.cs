using System;
using System.IO;

namespace Ultraudio.Core;

/// <summary>
/// Defensive validation helpers to prevent common errors early and provide
/// clear diagnostics. Use at method boundaries where external input or
/// untrusted state enters the system.
/// </summary>
public static class Guard
{
    /// <summary>Throws if <paramref name="value"/> is null.</summary>
    public static void NotNull<T>(T? value, string paramName) where T : class
    {
        if (value == null)
            throw new ArgumentNullException(paramName);
    }

    /// <summary>Throws if the string is null or whitespace.</summary>
    public static void NotEmpty(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or empty.", paramName);
    }

    /// <summary>Throws if the file does not exist on disk.</summary>
    public static void FileExists(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("File path cannot be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Audio file not found: {path}", path);
    }

    /// <summary>Throws if <paramref name="index"/> is out of range [0, count).</summary>
    public static void ValidIndex(int index, int count, string paramName = "index")
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(paramName,
                $"Index {index} is out of range [0, {count}).");
    }

    /// <summary>Throws if <paramref name="value"/> is outside [min, max].</summary>
    public static void InRange(double value, double min, double max, string paramName)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(paramName,
                $"Value {value} is outside allowed range [{min}, {max}].");
    }

    /// <summary>Throws if <paramref name="value"/> is outside [min, max].</summary>
    public static void InRange(int value, int min, int max, string paramName)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(paramName,
                $"Value {value} is outside allowed range [{min}, {max}].");
    }
}
