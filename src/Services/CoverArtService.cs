using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace Ultraudio.Services;

/// <summary>
/// Extracts embedded cover art from audio files via TagLib and caches
/// the resulting Avalonia bitmaps (LRU, max 20 entries).
/// </summary>
public class CoverArtService
{
    private const int CacheCapacity = 20;

    // Simple ordered dictionary acting as LRU cache
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Default placeholder (null means "show placeholder icon")
    public Bitmap? DefaultCover { get; set; } = null;

    /// <summary>
    /// Returns the cover art Bitmap for the given file, or null if none found.
    /// Results are cached.
    /// </summary>
    public Bitmap? GetCover(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return DefaultCover;

        // ── Cache hit ──────────────────────────────────────────────────────
        if (_cache.TryGetValue(filePath, out var cached))
        {
            // Move to front (most recently used)
            _lruOrder.Remove(filePath);
            _lruOrder.AddFirst(filePath);
            return cached;
        }

        // ── Cache miss: extract from file ──────────────────────────────────
        Bitmap? bitmap = null;
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var pictures = tagFile.Tag.Pictures;
            if (pictures != null && pictures.Length > 0)
            {
                byte[] imageData = pictures[0].Data.Data;
                using var ms = new MemoryStream(imageData);
                bitmap = new Bitmap(ms);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CoverArt] Failed to extract from {Path.GetFileName(filePath)}: {ex.Message}");
            bitmap = null;
        }

        // ── Insert into LRU ────────────────────────────────────────────────
        if (_cache.Count >= CacheCapacity)
        {
            // Evict least recently used
            var lruKey = _lruOrder.Last!.Value;
            _lruOrder.RemoveLast();
            if (_cache.TryGetValue(lruKey, out var evicted))
            {
                evicted?.Dispose();
                _cache.Remove(lruKey);
            }
        }

        _cache[filePath] = bitmap;
        _lruOrder.AddFirst(filePath);

        return bitmap ?? DefaultCover;
    }

    /// <summary>Clear the entire cover art cache.</summary>
    public void ClearCache()
    {
        foreach (var bmp in _cache.Values)
            bmp?.Dispose();
        _cache.Clear();
        _lruOrder.Clear();
    }
}
