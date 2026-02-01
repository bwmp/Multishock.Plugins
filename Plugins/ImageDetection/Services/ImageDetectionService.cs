using System.Diagnostics;
using Emgu.CV;
using ImageDetection.Algorithms;
using ImageDetection.Models;
using MultiShock.PluginSdk;

namespace ImageDetection.Services;

/// <summary>
/// Main service for image detection. Manages the detection loop and coordinates
/// with other services.
/// </summary>
public class ImageDetectionService : IAsyncDisposable
{
    private readonly ImageConfigService _configService;
    private readonly ScreenCaptureService _captureService;
    private readonly CooldownManager _cooldownManager;
    private readonly DetectionTriggerManager _triggerManager;
    private readonly AlgorithmRegistry _algorithmRegistry;
    private readonly RecentDetectionsService _recentDetections;
    private readonly IDeviceActions? _deviceActions;

    private CancellationTokenSource? _detectionCts;
    private Task? _detectionTask;
    private readonly object _lock = new();

    private readonly Dictionary<string, Mat> _loadedImages = [];
    private Resolution _currentResolution;

    /// <summary>
    /// Whether background detection is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Event for detection status changes.
    /// </summary>
    public event Action<bool>? RunningStateChanged;

    /// <summary>
    /// Event for detection errors.
    /// </summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// Detection statistics.
    /// </summary>
    public DetectionStats Stats { get; } = new();

    public ImageDetectionService(
        ImageConfigService configService,
        ScreenCaptureService captureService,
        CooldownManager cooldownManager,
        DetectionTriggerManager triggerManager,
        AlgorithmRegistry algorithmRegistry,
        RecentDetectionsService recentDetections,
        IDeviceActions? deviceActions = null)
    {
        _configService = configService;
        _captureService = captureService;
        _cooldownManager = cooldownManager;
        _triggerManager = triggerManager;
        _algorithmRegistry = algorithmRegistry;
        _recentDetections = recentDetections;
        _deviceActions = deviceActions;

        _currentResolution = _captureService.GetCurrentMonitorResolution();

        _configService.ConfigurationChanged += OnConfigurationChanged;
    }

    /// <summary>
    /// Starts the background detection loop.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            _detectionCts = new CancellationTokenSource();
            _detectionTask = Task.Run(() => DetectionLoop(_detectionCts.Token));
            IsRunning = true;

            _triggerManager.NotifyDetectionStarted();
            RunningStateChanged?.Invoke(true);
        }
    }

    /// <summary>
    /// Stops the background detection loop.
    /// </summary>
    public async Task StopAsync()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
        }

        _detectionCts?.Cancel();

        if (_detectionTask != null)
        {
            try
            {
                await _detectionTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // Detection loop did not stop in time
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _detectionCts?.Dispose();
        _detectionCts = null;
        _detectionTask = null;

        _triggerManager.NotifyDetectionStopped();
        RunningStateChanged?.Invoke(false);
    }

    /// <summary>
    /// Toggles detection on/off.
    /// </summary>
    public async Task ToggleAsync()
    {
        if (IsRunning)
            await StopAsync();
        else
            Start();
    }

    /// <summary>
    /// Performs a single detection pass (for flow nodes).
    /// </summary>
    public async Task<List<DetectionResult>> DetectOnceAsync(CancellationToken ct = default)
    {
        var results = new List<DetectionResult>();

        try
        {
            using var screenshot = _captureService.CaptureScreen();
            var enabledImages = _configService.GetAllEnabledImages().ToList();

            foreach (var (moduleId, imageConfig) in enabledImages)
            {
                if (ct.IsCancellationRequested) break;

                var result = await DetectImageAsync(screenshot, moduleId, imageConfig, ct);
                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }

        return results;
    }

    /// <summary>
    /// Detects a specific image in a screenshot.
    /// </summary>
    public Task<DetectionResult> DetectImageAsync(
        Mat screenshot,
        string moduleId,
        DetectionImage imageConfig,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var template = GetOrLoadImage(moduleId, imageConfig);
            if (template == null)
            {
                return Task.FromResult(DetectionResult.Failed($"Could not load image: {imageConfig.FilePath}", imageConfig));
            }

            using var filteredScreenshot = _captureService.ApplyRegionFilter(screenshot, imageConfig.Region);

            var algorithm = _algorithmRegistry.Get(imageConfig.AlgorithmId)
                ?? _algorithmRegistry.GetDefault();

            if (algorithm == null)
            {
                return Task.FromResult(DetectionResult.Failed("No detection algorithm available", imageConfig));
            }

            var result = algorithm.Detect(filteredScreenshot, template, imageConfig.Threshold, ct);
            result.Image = imageConfig;
            result.DetectionTime = stopwatch.Elapsed;

            Stats.TotalDetections++;

            if (result.Found)
            {
                Stats.SuccessfulDetections++;
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(DetectionResult.Failed(ex.Message, imageConfig));
        }
    }

    /// <summary>
    /// Reloads all cached images.
    /// </summary>
    public void ReloadImages()
    {
        lock (_lock)
        {
            ClearLoadedImages();
            _currentResolution = _captureService.GetCurrentMonitorResolution();
        }
    }

    private async Task DetectionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var loopStart = Stopwatch.StartNew();

                using var screenshot = _captureService.CaptureScreen();
                Stats.LastCaptureTime = DateTime.UtcNow;

                var enabledImages = _configService.GetAllEnabledImages().ToList();

                foreach (var (moduleId, imageConfig) in enabledImages)
                {
                    if (ct.IsCancellationRequested) break;

                    await ProcessImage(screenshot, moduleId, imageConfig, ct);
                }

                loopStart.Stop();
                Stats.LastLoopDuration = loopStart.Elapsed;

                var delayMs = _configService.CaptureConfig.CaptureDelayMs;
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);

                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessImage(
        Mat screenshot,
        string moduleId,
        DetectionImage imageConfig,
        CancellationToken ct)
    {
        var imagePath = $"{moduleId}/{imageConfig.Id}";

        var result = await DetectImageAsync(screenshot, moduleId, imageConfig, ct);

        if (result.Error != null)
        {
            return;
        }

        if (!result.Found)
        {
            return;
        }

        _cooldownManager.RecordDetection(imagePath, imageConfig.Cooldown);

        var wasInCooldown = !_cooldownManager.CanTrigger(imagePath, imageConfig.Cooldown);

        var module = _configService.GetModule(moduleId);
        var actionTriggered = !wasInCooldown && imageConfig.Action.Enabled;
        var actionType = actionTriggered ? imageConfig.Action.Type.ToString() : null;

        await _recentDetections.RecordDetectionAsync(
            moduleId,
            module?.Name ?? moduleId,
            imageConfig,
            result,
            screenshot,
            actionTriggered,
            actionType,
            wasInCooldown);

        if (wasInCooldown)
        {
            return;
        }

        await _triggerManager.FireDetectionEvent(moduleId, imageConfig, result);

        if (imageConfig.Action.Enabled && _deviceActions != null)
        {
            PerformAction(imageConfig.Action);
        }

        _cooldownManager.RecordTrigger(imagePath, imageConfig.Cooldown);
    }

    private void PerformAction(ActionConfig config)
    {
        try
        {
            var commandType = config.Type switch
            {
                ActionType.Shock => CommandType.Shock,
                ActionType.Vibrate => CommandType.Vibrate,
                ActionType.Beep => CommandType.Beep,
                _ => CommandType.Vibrate
            };

            if (config.Mode == ShockerMode.Specific && config.ShockerIds.Count > 0)
            {
                var parsedIds = config.ShockerIds
                    .Select(id => id.Split(':'))
                    .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
                    .Select(parts => (deviceId: int.Parse(parts[0]), shockerId: int.Parse(parts[1])))
                    .ToList();

                if (parsedIds.Count > 0)
                {
                    var deviceIds = parsedIds.Select(p => p.deviceId).Distinct();
                    var shockerIds = parsedIds.Select(p => p.shockerId);

                    _deviceActions?.PerformAction(
                        intensity: config.Intensity,
                        durationSeconds: config.DurationSeconds,
                        command: commandType,
                        deviceIds: deviceIds,
                        shockerIds: shockerIds
                    );
                }
            }
            else
            {
                _deviceActions?.PerformAction(
                    intensity: config.Intensity,
                    durationSeconds: config.DurationSeconds,
                    command: commandType
                );
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Action failed: {ex.Message}");
        }
    }

    private Mat? GetOrLoadImage(string moduleId, DetectionImage imageConfig)
    {
        var key = $"{moduleId}/{imageConfig.Id}";

        lock (_lock)
        {
            if (_loadedImages.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var image = _configService.LoadImage(imageConfig, _currentResolution);
            if (image != null)
            {
                _loadedImages[key] = image;
            }

            return image;
        }
    }

    private void ClearLoadedImages()
    {
        foreach (var image in _loadedImages.Values)
        {
            image.Dispose();
        }
        _loadedImages.Clear();
    }

    private void OnConfigurationChanged()
    {
        ReloadImages();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        ClearLoadedImages();
        _configService.ConfigurationChanged -= OnConfigurationChanged;
    }
}

/// <summary>
/// Statistics about detection performance.
/// </summary>
public class DetectionStats
{
    public long TotalDetections { get; set; }
    public long SuccessfulDetections { get; set; }
    public DateTime LastCaptureTime { get; set; }
    public TimeSpan LastLoopDuration { get; set; }

    public double SuccessRate => TotalDetections > 0
        ? (double)SuccessfulDetections / TotalDetections
        : 0;

    public void Reset()
    {
        TotalDetections = 0;
        SuccessfulDetections = 0;
        LastCaptureTime = default;
        LastLoopDuration = default;
    }
}
