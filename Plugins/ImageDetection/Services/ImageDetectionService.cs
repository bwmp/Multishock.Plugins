using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.CvEnum;
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
    private readonly IScreenCaptureService _captureService;
    private readonly CooldownManager _cooldownManager;
    private readonly DetectionTriggerManager _triggerManager;
    private readonly AlgorithmRegistry _algorithmRegistry;
    private readonly RecentDetectionsService _recentDetections;
    private readonly ValueChangeAnalyzerService? _valueAnalyzer;
    private readonly IDeviceActions? _deviceActions;
    private readonly IPluginHost? _pluginHost;

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
        IScreenCaptureService captureService,
        CooldownManager cooldownManager,
        DetectionTriggerManager triggerManager,
        AlgorithmRegistry algorithmRegistry,
        RecentDetectionsService recentDetections,
        IDeviceActions? deviceActions = null,
        IPluginHost? pluginHost = null,
        ValueChangeAnalyzerService? valueAnalyzer = null)
    {
        _configService = configService;
        _captureService = captureService;
        _cooldownManager = cooldownManager;
        _triggerManager = triggerManager;
        _algorithmRegistry = algorithmRegistry;
        _recentDetections = recentDetections;
        _valueAnalyzer = valueAnalyzer;
        _deviceActions = deviceActions;
        _pluginHost = pluginHost;

        SyncCaptureConfig();
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

            if (!_captureService.IsSupported)
            {
                ErrorOccurred?.Invoke(_captureService.UnsupportedReason ?? "Screen capture is not supported on this platform.");
                return;
            }

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

                // Meter targets are processed by the background loop (they need state tracking).
                // One-shot detection only applies to template targets.
                if (imageConfig.TargetType == DetectionTargetType.Meter)
                    continue;

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
            // Clone the template so the detection loop owns its own copy.
            // ReloadImages() can dispose cached Mats on a config change at any time;
            using var template = GetOrLoadImage(moduleId, imageConfig)?.Clone();
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

                    if (imageConfig.TargetType == DetectionTargetType.Meter)
                    {
                        await ProcessMeter(screenshot, moduleId, imageConfig, ct);
                    }
                    else
                    {
                        await ProcessImage(screenshot, moduleId, imageConfig, ct);
                    }
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

        _pluginHost?.ShowSuccessToast(
            $"Image Detected: {imageConfig.Name}",
            $"Confidence: {result.Confidence:P1}",
            10000);

        if (imageConfig.Action.Enabled && _deviceActions != null)
        {
            PerformAction(imageConfig.Action);
        }

        _cooldownManager.RecordTrigger(imagePath, imageConfig.Cooldown);
    }

    /// <summary>
    /// Processes a meter/healthbar target: extract fill %, feed analyzer, emit events.
    /// </summary>
    private async Task ProcessMeter(
        Mat screenshot,
        string moduleId,
        DetectionImage imageConfig,
        CancellationToken ct)
    {
        if (_valueAnalyzer == null) return;
        if (!imageConfig.Meter.Enabled) return;

        if (imageConfig.Meter.RequireFocusedWindow)
        {
            var isFocused = _captureService.IsRequiredWindowFocused(
                imageConfig.Meter.RequiredFocusWindowProcess,
                imageConfig.Meter.RequiredFocusWindowTitle);
            if (!isFocused) return;
        }

        // Meter targets require a custom region
        if (imageConfig.Region.Type != RegionType.Custom || imageConfig.Region.CustomRegion == null)
        {
            return;
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            var region = imageConfig.Region.CustomRegion;
            int x = Math.Max(0, Math.Min(region.X, screenshot.Width - 1));
            int y = Math.Max(0, Math.Min(region.Y, screenshot.Height - 1));
            int width = Math.Min(region.Width, screenshot.Width - x);
            int height = Math.Min(region.Height, screenshot.Height - y);

            if (width <= 2 || height <= 2) return;

            using var subMat = new Mat(screenshot, new System.Drawing.Rectangle(x, y, width, height));
            using var roi = subMat.Clone();

            // Compute fill percentage (exceptions propagate to outer catch for visibility)
            var percent = MeterFillAlgorithm.ComputeFillPercent(roi, imageConfig.Meter);
            if (percent < 0) return;

            Stats.TotalDetections++;

            var changeEvent = _valueAnalyzer.Process(moduleId, imageConfig, percent, DateTime.UtcNow);

            if (changeEvent == null) return; // No significant change so just discard

            Stats.SuccessfulDetections++;

            await _triggerManager.FireMeterChangedEvent(changeEvent);

            if (imageConfig.Action.Enabled && _deviceActions != null)
            {

                var absDelta = Math.Abs(changeEvent.DeltaPercent);
                var
                        // Damage % scaled against max: 30% HP loss with max 80 → 24
                        intensity = (object)imageConfig.Meter.IntensityMode switch
                        {
                            MeterIntensityMode.Scaled => (int)Math.Clamp(imageConfig.Action.Intensity * (absDelta / 100.0), 1, imageConfig.Action.Intensity), // Damage % scaled against max: 30% HP loss with max 80 → 24
                            MeterIntensityMode.Direct => (int)Math.Clamp(absDelta, 1, imageConfig.Action.Intensity), // Damage % used directly as intensity, capped at max
                            _ => (int)imageConfig.Action.Intensity,
                        };
                var actionConfig = new ActionConfig
                {
                    Enabled = true,
                    Type = imageConfig.Action.Type,
                    Intensity = intensity,
                    DurationSeconds = imageConfig.Action.DurationSeconds,
                    Mode = imageConfig.Action.Mode,
                    ShockerIds = imageConfig.Action.ShockerIds
                };

                PerformAction(actionConfig);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Meter processing failed: {ex.Message}");
        }
    }

    private static readonly Random _random = new();

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

            if (config.ShockerIds.Count > 0)
            {
                var parsedIds = config.ShockerIds
                    .Select(id => id.Split(':'))
                    .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
                    .Select(parts => (deviceId: int.Parse(parts[0]), shockerId: int.Parse(parts[1])))
                    .ToList();

                if (parsedIds.Count > 0)
                {
                    // In Random mode, pick a random subset from the selected shockers
                    if (config.Mode == ShockerMode.Random && parsedIds.Count > 1)
                    {
                        var min = Math.Clamp(config.RandomCountMin, 1, parsedIds.Count);
                        var max = Math.Clamp(config.RandomCountMax, min, parsedIds.Count);
                        var count = min == max ? min : _random.Next(min, max + 1);
                        parsedIds = parsedIds.OrderBy(_ => _random.Next()).Take(count).ToList();
                    }

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
                return; // No shocker IDs specified, so skip action to avoid unintended consequences
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
        SyncCaptureConfig();
        ReloadImages();
    }

    /// <summary>
    /// Pushes the user's capture settings (monitor index, source type, etc.)
    /// into the screen capture service so CaptureScreen() uses the right monitor.
    /// </summary>
    private void SyncCaptureConfig()
    {
        _captureService.Config = _configService.CaptureConfig;
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
