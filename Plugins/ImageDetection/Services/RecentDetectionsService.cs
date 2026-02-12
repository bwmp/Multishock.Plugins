using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;
using MultiShock.PluginSdk;

namespace ImageDetection.Services;

/// <summary>
/// Service for managing and tracking recent detection events.
/// </summary>
public class RecentDetectionsService : IDisposable
{
    private readonly List<DetectionEvent> _events = [];
    private readonly object _lock = new();
    private readonly string _screenshotsDir;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    /// <summary>
    /// Maximum number of recent detection events to display (unlimited = 0).
    /// </summary>
    public int MaxDisplayEvents { get; set; } = 50;

    /// <summary>
    /// Maximum number of screenshot files to keep on disk.
    /// </summary>
    public int MaxScreenshots { get; set; } = 25;

    /// <summary>
    /// Whether to save screenshots with detections.
    /// </summary>
    public bool SaveScreenshots { get; set; } = true;

    /// <summary>
    /// Event raised when a new detection is recorded.
    /// </summary>
    public event Action<DetectionEvent>? DetectionRecorded;

    /// <summary>
    /// Event raised when detections are cleared.
    /// </summary>
    public event Action? DetectionsCleared;

    public RecentDetectionsService(IPluginHost pluginHost)
    {
        _screenshotsDir = Path.Combine(
            pluginHost.GetPluginDataPath(ImageDetectionPlugin.PluginId), "Screenshots");

        Directory.CreateDirectory(_screenshotsDir);

        ClearScreenshotsFolder();
    }

    /// <summary>
    /// Clears all screenshots from the folder.
    /// </summary>
    private void ClearScreenshotsFolder()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_screenshotsDir))
            {
                try
                {
                    File.Delete(file);
                }
                catch { /* Ignore deletion errors */ }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to clear screenshots folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Records a new detection event.
    /// </summary>
    public async Task<DetectionEvent> RecordDetectionAsync(
        string moduleId,
        string moduleName,
        DetectionImage imageConfig,
        DetectionResult result,
        Mat? screenshot = null,
        bool actionTriggered = false,
        string? actionType = null,
        bool wasInCooldown = false)
    {
        var evt = new DetectionEvent
        {
            ModuleId = moduleId,
            ModuleName = moduleName,
            ImageId = imageConfig.Id,
            ImageName = imageConfig.Name ?? imageConfig.Id,
            Confidence = result.Confidence,
            LocationX = result.MatchLocation?.X ?? 0,
            LocationY = result.MatchLocation?.Y ?? 0,
            ActionTriggered = actionTriggered,
            ActionType = actionType,
            DetectionTime = result.DetectionTime,
            WasInCooldown = wasInCooldown
        };

        if (SaveScreenshots && screenshot != null && result.Found)
        {
            try
            {
                await _saveSemaphore.WaitAsync();
                try
                {
                    (evt.ScreenshotPath, evt.ThumbnailPath) = await SaveDetectionImagesAsync(
                        screenshot, result, evt.Id);
                }
                finally
                {
                    _saveSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail the detection
                Console.WriteLine($"Failed to save screenshot: {ex.Message}");
            }
        }

        lock (_lock)
        {
            _events.Insert(0, evt);
            
            // Note: Events list is unlimited (in-memory only, cleared on restart)
            // Only screenshots are limited
        }

        DetectionRecorded?.Invoke(evt);
        return evt;
    }

    /// <summary>
    /// Gets all recent detection events (unlimited in-memory log).
    /// </summary>
    public List<DetectionEvent> GetRecentDetections(int? count = null)
    {
        lock (_lock)
        {
            var events = _events;
            if (count.HasValue)
                events = [.. events.Take(count.Value)];
            else if (MaxDisplayEvents > 0)
                events = [.. events.Take(MaxDisplayEvents)];
            
            return [.. events];
        }
    }

    /// <summary>
    /// Gets recent detections for a specific module.
    /// </summary>
    public List<DetectionEvent> GetModuleDetections(string moduleId, int? count = null)
    {
        lock (_lock)
        {
            var query = _events.Where(e => e.ModuleId == moduleId);
            if (count.HasValue)
                query = query.Take(count.Value);
            return [.. query];
        }
    }

    /// <summary>
    /// Clears all detection history.
    /// </summary>
    public void ClearDetections()
    {
        lock (_lock)
        {
            foreach (var evt in _events)
            {
                DeleteScreenshotFiles(evt);
            }
            _events.Clear();
        }
        DetectionsCleared?.Invoke();
    }

    /// <summary>
    /// Gets the count of recent detections.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>
    /// Gets statistics about recent detections.
    /// </summary>
    public DetectionHistoryStats GetStats(TimeSpan? window = null)
    {
        lock (_lock)
        {
            var windowStart = DateTime.Now - (window ?? TimeSpan.FromHours(24));
            var recent = _events.Where(e => e.Timestamp > windowStart).ToList();

            return new DetectionHistoryStats
            {
                TotalDetections = recent.Count,
                ActionsTriggered = recent.Count(e => e.ActionTriggered),
                UniqueImagesDetected = recent.Select(e => e.ImageId).Distinct().Count(),
                AverageConfidence = recent.Any() ? recent.Average(e => e.Confidence) : 0,
                LastDetectionTime = recent.FirstOrDefault()?.Timestamp,
                MostDetectedImage = recent.GroupBy(e => e.ImageName)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key
            };
        }
    }

    private async Task<(string? screenshot, string? thumbnail)> SaveDetectionImagesAsync(
        Mat screenshot, DetectionResult result, string eventId)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var screenshotPath = Path.Combine(_screenshotsDir, $"detection_{eventId}_{timestamp}.png");

        if (result.MatchLocation.HasValue && result.MatchSize.HasValue)
        {
            var loc = result.MatchLocation.Value;
            var size = result.MatchSize.Value;
            var rect = new System.Drawing.Rectangle(loc.X, loc.Y, size.Width, size.Height);

            CvInvoke.Rectangle(screenshot, rect, new Emgu.CV.Structure.MCvScalar(0, 0, 255, 255), 3);
        }

        await Task.Run(() =>
        {
            screenshot.Save(screenshotPath);
        });

        string? thumbnailPath = null;

        if (result.MatchLocation.HasValue && result.MatchSize.HasValue)
        {
            var loc = result.MatchLocation.Value;
            var size = result.MatchSize.Value;

            var padding = 20;
            var x = Math.Max(0, loc.X - padding);
            var y = Math.Max(0, loc.Y - padding);
            var width = Math.Min(screenshot.Width - x, size.Width + padding * 2);
            var height = Math.Min(screenshot.Height - y, size.Height + padding * 2);

            using var roi = new Mat(screenshot, new System.Drawing.Rectangle(x, y, width, height));
            thumbnailPath = Path.Combine(_screenshotsDir, $"thumb_{eventId}_{timestamp}.png");

            using var resized = new Mat();
            var scale = Math.Min(200.0 / width, 200.0 / height);
            var newWidth = (int)(width * scale);
            var newHeight = (int)(height * scale);
            
            CvInvoke.Resize(roi, resized, new System.Drawing.Size(newWidth, newHeight), 
                interpolation: Inter.Linear);
            
            await Task.Run(() =>
            {
                resized.Save(thumbnailPath);
            });
        }

        EnforceScreenshotLimit();

        return (screenshotPath, thumbnailPath);
    }

    /// <summary>
    /// Removes oldest screenshots when we exceed the maximum count.
    /// </summary>
    private void EnforceScreenshotLimit()
    {
        try
        {
            var allFiles = Directory.EnumerateFiles(_screenshotsDir)
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.CreationTime)
                .ToList();

            var filesToDelete = allFiles.Take(Math.Max(0, allFiles.Count - MaxScreenshots));
            
            foreach (var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                }
                catch { /* Ignore deletion errors */ }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to enforce screenshot limit: {ex.Message}");
        }
    }

    private void DeleteScreenshotFiles(DetectionEvent evt)
    {
        try
        {
            if (!string.IsNullOrEmpty(evt.ScreenshotPath) && File.Exists(evt.ScreenshotPath))
            {
                File.Delete(evt.ScreenshotPath);
            }
            if (!string.IsNullOrEmpty(evt.ThumbnailPath) && File.Exists(evt.ThumbnailPath))
            {
                File.Delete(evt.ThumbnailPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete screenshot: {ex.Message}");
        }
    }


    public void Dispose()
    {
        _saveSemaphore.Dispose();
    }
}

/// <summary>
/// Statistics about detection history.
/// </summary>
public class DetectionHistoryStats
{
    public int TotalDetections { get; set; }
    public int ActionsTriggered { get; set; }
    public int UniqueImagesDetected { get; set; }
    public double AverageConfidence { get; set; }
    public DateTime? LastDetectionTime { get; set; }
    public string? MostDetectedImage { get; set; }
}
