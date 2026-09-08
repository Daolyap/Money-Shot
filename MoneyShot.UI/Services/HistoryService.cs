using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Media.Imaging;
using MoneyShot.Models;
using Logger = MoneyShot.Services.Logger;

namespace MoneyShot.UI.Services;

/// <summary>
/// Avalonia twin of MoneyShot/Services/HistoryService.cs (WPF) — see LINUX_PORT.md Phase 1. Same
/// file layout and JSON metadata shape (both builds share the same %AppData%\MoneyShot\history
/// directory, so history captured by either build shows up in both), with BitmapSource/
/// PngBitmapEncoder swapped for Avalonia's Bitmap. History is always PNG regardless of the user's
/// chosen save format — matches the WPF version, which also hardcoded PngBitmapEncoder here.
/// </summary>
public sealed class HistoryService
{
    private const int ThumbnailMaxWidth = 400;
    private const int DefaultRetentionCount = 50;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _historyDirectory;

    public HistoryService()
    {
        _historyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoneyShot",
            "history");
        try
        {
            Directory.CreateDirectory(_historyDirectory);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not create history directory", ex);
        }
    }

    public string HistoryDirectory => _historyDirectory;

    public HistoryEntry? Save(Bitmap image, string source, int retentionCount = DefaultRetentionCount)
    {
        try
        {
            var id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            var entry = new HistoryEntry
            {
                Id = id,
                CapturedAt = DateTime.Now,
                Width = image.PixelSize.Width,
                Height = image.PixelSize.Height,
                Source = source,
                ImageFileName = $"{id}.png",
                ThumbnailFileName = $"{id}-thumb.png",
            };

            image.Save(Path.Combine(_historyDirectory, entry.ImageFileName));
            using (var thumbnail = CreateThumbnail(image))
            {
                thumbnail.Save(Path.Combine(_historyDirectory, entry.ThumbnailFileName));
            }
            File.WriteAllText(
                Path.Combine(_historyDirectory, $"{id}.json"),
                JsonSerializer.Serialize(entry, JsonOptions));

            EnforceRetention(retentionCount);
            return entry;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save history entry", ex);
            return null;
        }
    }

    public IReadOnlyList<HistoryEntry> List()
    {
        try
        {
            return Directory.EnumerateFiles(_historyDirectory, "*.json")
                .Select(LoadEntry)
                .Where(e => e != null)
                .Select(e => e!)
                .OrderByDescending(e => e.CapturedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to list history", ex);
            return Array.Empty<HistoryEntry>();
        }
    }

    public Bitmap? LoadImage(HistoryEntry entry) =>
        LoadPng(Path.Combine(_historyDirectory, entry.ImageFileName));

    public Bitmap? LoadThumbnail(HistoryEntry entry) =>
        LoadPng(Path.Combine(_historyDirectory, entry.ThumbnailFileName));

    public void Delete(HistoryEntry entry)
    {
        TryDelete(Path.Combine(_historyDirectory, entry.ImageFileName));
        TryDelete(Path.Combine(_historyDirectory, entry.ThumbnailFileName));
        TryDelete(Path.Combine(_historyDirectory, $"{entry.Id}.json"));
    }

    private void EnforceRetention(int retentionCount)
    {
        if (retentionCount <= 0) return;
        var entries = List();
        if (entries.Count <= retentionCount) return;
        foreach (var stale in entries.Skip(retentionCount))
        {
            Delete(stale);
        }
    }

    private static HistoryEntry? LoadEntry(string jsonPath)
    {
        try
        {
            return JsonSerializer.Deserialize<HistoryEntry>(File.ReadAllText(jsonPath));
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadPng(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            // Read fully into memory before decoding so the file handle doesn't stay open —
            // matches the WPF version's BitmapCacheOption.OnLoad intent (lets Delete succeed later).
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load history image at {path}", ex);
            return null;
        }
    }

    private static Bitmap CreateThumbnail(Bitmap source)
    {
        if (source.PixelSize.Width <= ThumbnailMaxWidth) return source;
        var scale = (double)ThumbnailMaxWidth / source.PixelSize.Width;
        var targetSize = new Avalonia.PixelSize(
            ThumbnailMaxWidth,
            Math.Max(1, (int)Math.Round(source.PixelSize.Height * scale)));
        return source.CreateScaledBitmap(targetSize, BitmapInterpolationMode.HighQuality);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
