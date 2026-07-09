using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using Ultraudio.Core;

namespace Ultraudio.Services;

/// <summary>
/// Extracts embedded cover art from audio files via TagLib and caches
/// the resulting Avalonia bitmaps using an O(1) LRU cache.
/// </summary>
public class CoverArtService
{
    private const int CacheCapacity = 50;

    // O(1) LRU: LinkedList for order + Dictionary mapping key → node
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Default placeholder (null means "show placeholder icon")
    public Bitmap? DefaultCover { get; set; } = null;

    /// <summary>
    /// Returns the cover art Bitmap for the given file, or null if none found.
    /// Results are cached with O(1) LRU eviction.
    /// </summary>
    public Bitmap? GetCover(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return DefaultCover;

        if (filePath.StartsWith(UltraudioConstants.CdProtocolPrefix, StringComparison.OrdinalIgnoreCase))
            return DefaultCover;

        // ── Cache hit — O(1) promote to front ─────────────────────────────
        if (_cache.TryGetValue(filePath, out var cached))
        {
            // Move to front using node reference (O(1) instead of O(n) search)
            if (_lruNodes.TryGetValue(filePath, out var node))
            {
                _lruOrder.Remove(node);
                _lruOrder.AddFirst(node);
            }
            return cached;
        }

        // ── Cache miss: extract from file ─────────────────────────────────
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
            Log.Warn("CoverArt", $"Failed to extract from {Path.GetFileName(filePath)}: {ex.Message}");
            bitmap = null;
        }

        // ── Insert into LRU — evict if at capacity ────────────────────────
        if (_cache.Count >= CacheCapacity)
        {
            // Evict least recently used (tail of list)
            var lruNode = _lruOrder.Last!;
            string lruKey = lruNode.Value;
            _lruOrder.RemoveLast();
            _lruNodes.Remove(lruKey);
            if (_cache.TryGetValue(lruKey, out var evicted))
            {
                evicted?.Dispose();
                _cache.Remove(lruKey);
            }
        }

        _cache[filePath] = bitmap;
        var newNode = _lruOrder.AddFirst(filePath);
        _lruNodes[filePath] = newNode;

        return bitmap ?? DefaultCover;
    }

    /// <summary>Clear the entire cover art cache and dispose all bitmaps.</summary>
    public void ClearCache()
    {
        foreach (var bmp in _cache.Values)
            bmp?.Dispose();
        _cache.Clear();
        _lruOrder.Clear();
        _lruNodes.Clear();
    }
}
