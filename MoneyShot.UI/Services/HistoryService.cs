using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Media.Imaging;
using MoneyShot.Models;
using MoneyShot.UI.Interop;
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
        _historyDirectory = Path.Combine(MoneyShot.Services.AppDataPaths.GetConfigRoot(), "MoneyShot", "history");
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

            // Save(string) is the only overload for "just encode PNG, default settings" — its
            // replacement (a BitmapEncoderOptions overload) isn't otherwise needed since history
            // is always plain PNG regardless of the user's chosen save format.
#pragma warning disable CS0618
            image.Save(Path.Combine(_historyDirectory, entry.ImageFileName));
            using (var thumbnail = CreateThumbnail(image))
            {
                thumbnail.Save(Path.Combine(_historyDirectory, entry.ThumbnailFileName));
            }
#pragma warning restore CS0618
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

    /// <summary>
    /// Downsamples via raw pixel nearest-neighbor sampling rather than Bitmap.CreateScaledBitmap
    /// — confirmed by actually running this (see LINUX_PORT.md Phase 1 verification notes) that
    /// CreateScaledBitmap throws "Invalid source bitmap type" on a WriteableBitmap source (which
    /// is what every capture produces, via CapturedImage.ToAvaloniaBitmap). Also deliberately
    /// always returns a distinct Bitmap object, even when no scaling is needed: the caller wraps
    /// the result in a `using`, and the previous version's "already small enough, just return
    /// source" fast path would alias the caller's own `image` argument — the `using` block would
    /// then dispose the caller's bitmap out from under it (e.g. right before EditorWindow renders
    /// it), for any capture at or under 400px wide.
    /// </summary>
    private static Bitmap CreateThumbnail(Bitmap source)
    {
        var captured = source.ToCapturedImage();
        if (captured.Width <= ThumbnailMaxWidth) return captured.ToAvaloniaBitmap();

        var scale = (double)ThumbnailMaxWidth / captured.Width;
        var targetWidth = ThumbnailMaxWidth;
        var targetHeight = Math.Max(1, (int)Math.Round(captured.Height * scale));
        return DownsampleNearestNeighbor(captured, targetWidth, targetHeight).ToAvaloniaBitmap();
    }

    private static MoneyShot.Abstractions.CapturedImage DownsampleNearestNeighbor(MoneyShot.Abstractions.CapturedImage source, int targetWidth, int targetHeight)
    {
        const int bytesPerPixel = 4;
        var destStride = targetWidth * bytesPerPixel;
        var dest = new byte[destStride * targetHeight];

        for (var ty = 0; ty < targetHeight; ty++)
        {
            var srcY = Math.Min(source.Height - 1, (int)((long)ty * source.Height / targetHeight));
            var destRowOffset = ty * destStride;
            var srcRowOffset = srcY * source.Stride;
            for (var tx = 0; tx < targetWidth; tx++)
            {
                var srcX = Math.Min(source.Width - 1, (int)((long)tx * source.Width / targetWidth));
                var srcOffset = srcRowOffset + srcX * bytesPerPixel;
                var destOffset = destRowOffset + tx * bytesPerPixel;
                Buffer.BlockCopy(source.PixelDataBgra32, srcOffset, dest, destOffset, bytesPerPixel);
            }
        }

        return new MoneyShot.Abstractions.CapturedImage(targetWidth, targetHeight, destStride, dest);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
