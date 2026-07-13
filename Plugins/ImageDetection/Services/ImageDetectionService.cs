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
    private readonly OcrChangeAnalyzerService? _ocrAnalyzer;
    private readonly OcrTextRecognizerService? _ocrRecognizer;
    private readonly IDeviceActions? _deviceActions;
    private readonly IPluginHost? _pluginHost;

    private CancellationTokenSource? _detectionCts;
    private Task? _detectionTask;
    private readonly object _lock = new();

    private readonly Dictionary<string, LoadedTemplate> _loadedImages = [];
    private readonly ReaderWriterLockSlim _templateLock = new();
    private long _frameNumber;
    private Resolution _currentResolution;

    // Consecutive-match streaks per target (only touched from the loop thread).
    private readonly Dictionary<string, int> _consecutiveMatches = [];
    private bool _legacyRegionsStamped;

    // Live per-target status for the UI.
    private readonly Dictionary<string, TargetHealth> _health = [];
    private readonly object _healthLock = new();

    // Rate limiter for detection toasts (only touched from the loop thread).
    private readonly Dictionary<string, DateTime> _lastToastAt = [];
    private static readonly TimeSpan ToastMinInterval = TimeSpan.FromSeconds(30);

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
        ValueChangeAnalyzerService? valueAnalyzer = null,
        OcrChangeAnalyzerService? ocrAnalyzer = null,
        OcrTextRecognizerService? ocrRecognizer = null)
    {
        _configService = configService;
        _captureService = captureService;
        _cooldownManager = cooldownManager;
        _triggerManager = triggerManager;
        _algorithmRegistry = algorithmRegistry;
        _recentDetections = recentDetections;
        _valueAnalyzer = valueAnalyzer;
        _ocrAnalyzer = ocrAnalyzer;
        _ocrRecognizer = ocrRecognizer;
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
            using var colorScreenshot = ToBgr(screenshot);
            using var grayScreenshot = ToGray(colorScreenshot);
            var enabledImages = _configService.GetAllEnabledImages().ToList();

            foreach (var (moduleId, imageConfig) in enabledImages)
            {
                if (ct.IsCancellationRequested) break;

                // Stateful targets (meter/OCR) are processed by the background loop.
                // One-shot detection only applies to template targets.
                if (imageConfig.TargetType != DetectionTargetType.Template)
                    continue;

                var result = await DetectImagePreparedAsync(screenshot, colorScreenshot, grayScreenshot, moduleId, imageConfig, ct);
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
    /// Runs a single target's pipeline once against a fresh capture and returns
    /// a rich result for the UI: confidence / meter % / OCR text plus an
    /// annotated preview image. Never fires triggers, actions, or cooldowns.
    /// </summary>
    public async Task<TargetTestResult> TestTargetAsync(string moduleId, string targetId, CancellationToken ct = default)
    {
        var image = _configService.GetImage(moduleId, targetId);
        if (image == null)
        {
            return TargetTestResult.Fail("Target not found");
        }

        if (!_captureService.IsSupported)
        {
            return TargetTestResult.Fail(_captureService.UnsupportedReason ?? "Screen capture is not supported.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var screenshot = _captureService.CaptureScreen();

            var result = image.TargetType switch
            {
                DetectionTargetType.Meter => TestMeter(screenshot, image),
                DetectionTargetType.Ocr => await TestOcr(screenshot, image, ct),
                _ => await TestTemplate(screenshot, moduleId, image, ct)
            };

            result.Duration = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            return TargetTestResult.Fail($"Capture failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the live status the detection loop has collected for a target,
    /// including the current cooldown countdown. Null before the first pass.
    /// </summary>
    public TargetHealth? GetTargetHealth(string moduleId, DetectionImage image)
    {
        var key = $"{moduleId}/{image.Id}";

        TargetHealth? health;
        lock (_healthLock)
        {
            health = _health.TryGetValue(key, out var value) ? value.Clone() : null;
        }

        if (health != null)
        {
            health.CooldownRemainingSeconds =
                _cooldownManager.GetCooldownInfo(key, image.Cooldown).RemainingSeconds;
        }

        return health;
    }

    /// <summary>
    /// Returns configuration problems that would make this target silently do
    /// nothing at runtime (missing region, no shockers, missing file, ...).
    /// </summary>
    public List<string> GetTargetIssues(DetectionImage image)
    {
        var templateFileExists = image.TargetType != DetectionTargetType.Template
            || (!string.IsNullOrEmpty(image.FilePath) && File.Exists(image.FilePath));

        return TargetValidator.GetIssues(image, templateFileExists, _ocrRecognizer?.IsAvailable ?? false);
    }

    private async Task<TargetTestResult> TestTemplate(Mat screenshot, string moduleId, DetectionImage image, CancellationToken ct)
    {
        var result = await DetectImageAsync(screenshot, moduleId, image, ct);

        if (result.Error != null)
        {
            return TargetTestResult.Fail(result.Error);
        }

        using var annotated = ToBgr(screenshot);

        if (result.MatchLocation is { } location && result.MatchSize is { } size)
        {
            // Custom-region matches are reported region-relative; map back to
            // full-frame coordinates for the overlay.
            int offsetX = 0, offsetY = 0;
            if (image.Region.Type == RegionType.Custom && image.Region.CustomRegion != null)
            {
                var region = image.Region.GetCustomRegionFor(screenshot.Width, screenshot.Height)!;
                offsetX = Math.Max(0, region.X);
                offsetY = Math.Max(0, region.Y);
            }

            var color = result.Found
                ? new Emgu.CV.Structure.MCvScalar(64, 220, 64)
                : new Emgu.CV.Structure.MCvScalar(64, 64, 220);
            CvInvoke.Rectangle(annotated,
                new System.Drawing.Rectangle(location.X + offsetX, location.Y + offsetY, size.Width, size.Height),
                color, 3);
        }

        return new TargetTestResult
        {
            Success = true,
            Found = result.Found,
            Confidence = result.Confidence,
            Threshold = image.Threshold,
            Message = result.Found
                ? $"Match found: confidence {result.Confidence:P1} ≥ threshold {image.Threshold:P0}"
                : $"No match: best confidence {result.Confidence:P1} is below threshold {image.Threshold:P0}",
            PreviewDataUri = EncodePngDataUri(annotated)
        };
    }

    private TargetTestResult TestMeter(Mat screenshot, DetectionImage image)
    {
        using var roi = CropCustomRegion(screenshot, image);
        if (roi == null)
        {
            return TargetTestResult.Fail("Configure a custom screen region for this meter first.");
        }

        var percent = MeterFillAlgorithm.ComputeFillPercent(roi, image.Meter);
        if (percent < 0)
        {
            return new TargetTestResult
            {
                Success = true,
                Message = "Could not read a fill level from the region. Check the region crop and color settings.",
                PreviewDataUri = EncodePngDataUri(roi)
            };
        }

        return new TargetTestResult
        {
            Success = true,
            Found = true,
            MeterPercent = percent,
            Message = $"Meter reads {percent:F1}% full",
            PreviewDataUri = EncodePngDataUri(roi)
        };
    }

    private async Task<TargetTestResult> TestOcr(Mat screenshot, DetectionImage image, CancellationToken ct)
    {
        if (_ocrRecognizer is not { IsAvailable: true })
        {
            return TargetTestResult.Fail(_ocrRecognizer?.UnavailableReason ?? "OCR is not available.");
        }

        using var roi = CropCustomRegion(screenshot, image);
        if (roi == null)
        {
            return TargetTestResult.Fail("Configure a custom screen region for this OCR target first.");
        }

        var rawText = await _ocrRecognizer.ReadTextAsync(roi, image.Ocr, ct);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new TargetTestResult
            {
                Success = true,
                Message = "No text was read from the region. Try adjusting the region, scale, or thresholding.",
                PreviewDataUri = EncodePngDataUri(roi)
            };
        }

        string? parsed = null;
        if (_ocrAnalyzer != null)
        {
            var reading = _ocrAnalyzer.ParseReading(image.Ocr, rawText);
            if (reading.MatchedKeyword != null) parsed = $"keyword \"{reading.MatchedKeyword}\"";
            if (reading.NumberValue.HasValue)
                parsed = parsed == null ? $"number {reading.NumberValue.Value}" : $"{parsed}, number {reading.NumberValue.Value}";
        }

        return new TargetTestResult
        {
            Success = true,
            Found = true,
            OcrText = rawText,
            OcrParsed = parsed,
            Message = parsed != null ? $"Read \"{rawText}\" ({parsed})" : $"Read \"{rawText}\" (no keyword/number matched)",
            PreviewDataUri = EncodePngDataUri(roi)
        };
    }

    /// <summary>
    /// Crops the target's custom region out of the screenshot, or null when no
    /// usable region is configured.
    /// </summary>
    private static Mat? CropCustomRegion(Mat screenshot, DetectionImage image)
    {
        if (image.Region.Type != RegionType.Custom || image.Region.CustomRegion == null) return null;

        var region = image.Region.GetCustomRegionFor(screenshot.Width, screenshot.Height)!;
        int x = Math.Max(0, Math.Min(region.X, screenshot.Width - 1));
        int y = Math.Max(0, Math.Min(region.Y, screenshot.Height - 1));
        int width = Math.Min(region.Width, screenshot.Width - x);
        int height = Math.Min(region.Height, screenshot.Height - y);

        if (width <= 2 || height <= 2) return null;

        using var view = new Mat(screenshot, new System.Drawing.Rectangle(x, y, width, height));
        return view.Clone();
    }

    /// <summary>
    /// Encodes a Mat as a PNG data URI, downscaling to a preview-friendly width.
    /// </summary>
    private static string EncodePngDataUri(Mat image, int maxWidth = 960)
    {
        Mat toEncode = image;
        Mat? resized = null;

        if (image.Width > maxWidth)
        {
            var scale = (double)maxWidth / image.Width;
            resized = new Mat();
            CvInvoke.Resize(image, resized, new System.Drawing.Size(0, 0), scale, scale, Inter.Area);
            toEncode = resized;
        }

        try
        {
            using var buffer = new Emgu.CV.Util.VectorOfByte();
            CvInvoke.Imencode(".png", toEncode, buffer);
            return $"data:image/png;base64,{Convert.ToBase64String(buffer.ToArray())}";
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private void UpdateHealth(string moduleId, string targetId, Action<TargetHealth> update)
    {
        var key = $"{moduleId}/{targetId}";
        lock (_healthLock)
        {
            if (!_health.TryGetValue(key, out var health))
            {
                health = new TargetHealth();
                _health[key] = health;
            }

            health.LastCheckedUtc = DateTime.UtcNow;
            update(health);
        }
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
        using var colorScreenshot = ToBgr(screenshot);
        using var grayScreenshot = ToGray(colorScreenshot);
        return DetectImagePreparedAsync(screenshot, colorScreenshot, grayScreenshot, moduleId, imageConfig, ct);
    }

    private Task<DetectionResult> DetectImagePreparedAsync(
        Mat sourceScreenshot, Mat colorScreenshot, Mat grayScreenshot,
        string moduleId, DetectionImage imageConfig, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        _templateLock.EnterReadLock();
        try
        {
            var cached = GetOrLoadImage(moduleId, imageConfig);
            if (cached == null)
            {
                return Task.FromResult(DetectionResult.Failed($"Could not load image: {imageConfig.FilePath}", imageConfig));
            }

            bool masked = imageConfig.AlgorithmId == "template-matching-masked";
            var screenshot = masked ? sourceScreenshot : imageConfig.UseColorMatching ? colorScreenshot : grayScreenshot;
            var template = masked ? cached.Source : imageConfig.UseColorMatching ? cached.Color : cached.Gray;

            var algorithm = _algorithmRegistry.Get(imageConfig.AlgorithmId)
                ?? _algorithmRegistry.GetDefault();

            if (algorithm == null)
            {
                return Task.FromResult(DetectionResult.Failed("No detection algorithm available", imageConfig));
            }

            DetectionResult result;

            if (imageConfig.Region.Type == RegionType.Grid
                && imageConfig.Region.GridSections is { } sections
                && !sections.AllSectionsEnabled())
            {
                // Match inside each enabled section block instead of blacking out
                // disabled sections: avoids false matches in the blacked-out area
                // and skips its matching cost.
                result = DetectInGridSections(algorithm, screenshot, template, imageConfig, sections, ct);
            }
            else
            {
                using var filteredScreenshot = _captureService.ApplyRegionFilter(screenshot, imageConfig.Region);
                result = DetectWithScales(algorithm, filteredScreenshot, template, imageConfig, ct);
            }

            result.Image = imageConfig;
            result.DetectionTime = stopwatch.Elapsed;

            Stats.IncrementTotal();

            if (result.Found)
            {
                Stats.IncrementSuccessful();
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(DetectionResult.Failed(ex.Message, imageConfig));
        }
        finally { _templateLock.ExitReadLock(); }
    }

    /// <summary>
    /// Runs detection once per enabled grid-section block and returns the first
    /// hit (or the best-confidence miss). Match locations are reported in
    /// full-screenshot coordinates, matching the old blackout behavior.
    /// </summary>
    private static DetectionResult DetectInGridSections(
        IDetectionAlgorithm algorithm,
        Mat screenshot,
        Mat template,
        DetectionImage imageConfig,
        GridSections sections,
        CancellationToken ct)
    {
        var rects = sections.GetEnabledRectangles(screenshot.Width, screenshot.Height);
        if (rects.Count == 0)
        {
            return DetectionResult.Failed("No grid sections are enabled", imageConfig);
        }

        DetectionResult? best = null;

        foreach (var rect in rects)
        {
            ct.ThrowIfCancellationRequested();

            if (template.Width > rect.Width || template.Height > rect.Height)
            {
                continue; // Template cannot fit inside this block of sections
            }

            using var roi = new Mat(screenshot, rect);
            var result = DetectWithScales(algorithm, roi, template, imageConfig, ct);

            if (result.MatchLocation is { } loc)
            {
                result.MatchLocation = new Models.Point(loc.X + rect.X, loc.Y + rect.Y);
            }

            if (result.Found)
            {
                return result;
            }

            if (result.Error == null && (best == null || result.Confidence > best.Confidence))
            {
                best = result;
            }
        }

        return best ?? DetectionResult.Failed(
            $"Template ({template.Width}x{template.Height}) does not fit in any enabled grid section block",
            imageConfig);
    }

    /// <summary>
    /// Runs detection, optionally sweeping the template across the configured
    /// scale range (nearest to native scale first) so matches survive in-game
    /// UI scaling that monitor-resolution auto-resize cannot account for.
    /// </summary>
    private static DetectionResult DetectWithScales(
        IDetectionAlgorithm algorithm,
        Mat screenshot,
        Mat template,
        DetectionImage imageConfig,
        CancellationToken ct)
    {
        var config = imageConfig.MultiScale;
        if (imageConfig.TargetType != DetectionTargetType.Template || !config.Enabled)
        {
            return algorithm.Detect(screenshot, template, imageConfig.Threshold, ct);
        }

        DetectionResult? best = null;

        foreach (var scale in BuildScaleLadder(config))
        {
            ct.ThrowIfCancellationRequested();

            DetectionResult result;

            if (Math.Abs(scale - 1.0) < 0.001)
            {
                result = algorithm.Detect(screenshot, template, imageConfig.Threshold, ct);
            }
            else
            {
                int w = (int)Math.Round(template.Width * scale);
                int h = (int)Math.Round(template.Height * scale);
                if (w < 2 || h < 2 || w > screenshot.Width || h > screenshot.Height)
                {
                    continue;
                }

                using var scaledTemplate = new Mat();
                CvInvoke.Resize(template, scaledTemplate, new System.Drawing.Size(w, h),
                    0, 0, scale < 1.0 ? Inter.Area : Inter.Cubic);
                result = algorithm.Detect(screenshot, scaledTemplate, imageConfig.Threshold, ct);
            }

            if (result.Found)
            {
                return result;
            }

            if (result.Error == null && (best == null || result.Confidence > best.Confidence))
            {
                best = result;
            }
        }

        return best ?? DetectionResult.Failed("Template does not fit in the search area at any scale", imageConfig);
    }

    private static IEnumerable<double> BuildScaleLadder(MultiScaleConfig config)
    {
        var steps = Math.Clamp(config.Steps, 2, 15);
        var min = Math.Clamp(Math.Min(config.MinScale, config.MaxScale), 0.2, 1.0);
        var max = Math.Clamp(Math.Max(config.MinScale, config.MaxScale), 1.0, 4.0);

        var scales = new List<double> { 1.0 };
        for (int i = 0; i < steps; i++)
        {
            var scale = min + (max - min) * i / (steps - 1);
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                scales.Add(scale);
            }
        }

        // Native scale first, then nearest scales outward, so exact matches
        // exit early and pay nothing extra.
        return scales.OrderBy(s => Math.Abs(s - 1.0));
    }

    /// <summary>
    /// Reloads all cached images.
    /// </summary>
    public void ReloadImages()
    {
        _templateLock.EnterWriteLock();
        try
        {
            ClearLoadedImages();
            _currentResolution = _captureService.GetCurrentMonitorResolution();
        }
        finally { _templateLock.ExitWriteLock(); }
    }

    private async Task DetectionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var loopStart = Stopwatch.StartNew();

                var frame = Interlocked.Increment(ref _frameNumber);
                var enabledImages = _configService.GetAllEnabledImages()
                    .Where(item => IsDueAndFocused(item.Image, frame)).ToList();

                // Avoid even taking a screenshot when every configured target is
                // cadence-skipped or waiting for its required foreground window.
                if (enabledImages.Count == 0)
                {
                    await DelayUntilNextLoop(ct);
                    continue;
                }

                using var screenshot = _captureService.CaptureScreen();
                using var colorScreenshot = ToBgr(screenshot);
                using var grayScreenshot = ToGray(colorScreenshot);
                Stats.LastCaptureTime = DateTime.UtcNow;

                if (!_legacyRegionsStamped)
                {
                    _legacyRegionsStamped = true;
                    _configService.StampLegacyRegionReferences(
                        new Resolution(screenshot.Width, screenshot.Height));
                }

                foreach (var (moduleId, imageConfig) in enabledImages)
                {
                    if (ct.IsCancellationRequested) break;

                    if (imageConfig.TargetType == DetectionTargetType.Meter)
                    {
                        await ProcessMeter(screenshot, moduleId, imageConfig, ct);
                    }
                    else if (imageConfig.TargetType == DetectionTargetType.Ocr)
                    {
                        await ProcessOcr(screenshot, moduleId, imageConfig, ct);
                    }
                    else
                    {
                        await ProcessImage(screenshot, colorScreenshot, grayScreenshot, moduleId, imageConfig, ct);
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
        Mat colorScreenshot,
        Mat grayScreenshot,
        string moduleId,
        DetectionImage imageConfig,
        CancellationToken ct)
    {
        var imagePath = $"{moduleId}/{imageConfig.Id}";

        var result = await DetectImagePreparedAsync(
            screenshot, colorScreenshot, grayScreenshot, moduleId, imageConfig, ct);

        UpdateHealth(moduleId, imageConfig.Id, health =>
        {
            health.LastError = result.Error;
            health.LastConfidence = result.Error == null ? result.Confidence : health.LastConfidence;
            if (result.Found) health.LastDetectedUtc = DateTime.UtcNow;
        });

        if (result.Error != null)
        {
            return;
        }

        if (!result.Found)
        {
            _consecutiveMatches.Remove(imagePath);
            return;
        }

        // Debounce: require N consecutive matching frames before triggering so a
        // confidence value wobbling around the threshold can't fire on noise.
        var required = Math.Max(1, imageConfig.RequiredConsecutiveMatches);
        var streak = Math.Min(required, _consecutiveMatches.GetValueOrDefault(imagePath) + 1);
        _consecutiveMatches[imagePath] = streak;

        if (streak < required)
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

        // Per-target toast opt-out + rate limit: a frequently matching target
        // must not flood the toast area.
        if (imageConfig.ShowDetectionToasts
            && DateTime.UtcNow - _lastToastAt.GetValueOrDefault(imagePath) >= ToastMinInterval)
        {
            _lastToastAt[imagePath] = DateTime.UtcNow;
            _pluginHost?.ShowSuccessToast(
                $"Image Detected: {imageConfig.Name}",
                $"Confidence: {result.Confidence:P1}",
                4000);
        }

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

            var region = imageConfig.Region.GetCustomRegionFor(screenshot.Width, screenshot.Height)!;
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

            UpdateHealth(moduleId, imageConfig.Id, health =>
            {
                health.LastMeterPercent = percent;
                health.LastError = null;
            });

            Stats.IncrementTotal();

            var changeEvent = _valueAnalyzer.Process(moduleId, imageConfig, percent, DateTime.UtcNow);

            if (changeEvent == null) return; // No significant change so just discard

            Stats.IncrementSuccessful();

            UpdateHealth(moduleId, imageConfig.Id, health => health.LastDetectedUtc = DateTime.UtcNow);

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
            UpdateHealth(moduleId, imageConfig.Id, health => health.LastError = ex.Message);
            ErrorOccurred?.Invoke($"Meter processing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes an OCR target: extract text/number from region and emit OCR events.
    /// </summary>
    private async Task ProcessOcr(
        Mat screenshot,
        string moduleId,
        DetectionImage imageConfig,
        CancellationToken ct)
    {
        if (_ocrAnalyzer == null || _ocrRecognizer == null) return;
        if (!imageConfig.Ocr.Enabled) return;

        if (imageConfig.Ocr.RequireFocusedWindow)
        {
            var isFocused = _captureService.IsRequiredWindowFocused(
                imageConfig.Ocr.RequiredFocusWindowProcess,
                imageConfig.Ocr.RequiredFocusWindowTitle);
            if (!isFocused) return;
        }

        if (imageConfig.Region.Type != RegionType.Custom || imageConfig.Region.CustomRegion == null)
        {
            return;
        }

        if (!_ocrRecognizer.IsAvailable)
        {
            return;
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            var region = imageConfig.Region.GetCustomRegionFor(screenshot.Width, screenshot.Height)!;
            int x = Math.Max(0, Math.Min(region.X, screenshot.Width - 1));
            int y = Math.Max(0, Math.Min(region.Y, screenshot.Height - 1));
            int width = Math.Min(region.Width, screenshot.Width - x);
            int height = Math.Min(region.Height, screenshot.Height - y);

            if (width <= 2 || height <= 2) return;

            using var subMat = new Mat(screenshot, new System.Drawing.Rectangle(x, y, width, height));
            using var roi = subMat.Clone();

            var rawText = await _ocrRecognizer.ReadTextAsync(roi, imageConfig.Ocr, ct);

            UpdateHealth(moduleId, imageConfig.Id, health =>
            {
                health.LastOcrText = rawText;
                health.LastError = null;
            });

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return;
            }

            Stats.IncrementTotal();

            var reading = _ocrAnalyzer.ParseReading(imageConfig.Ocr, rawText);
            var detectionEvent = _ocrAnalyzer.Process(moduleId, imageConfig, reading, DateTime.UtcNow);
            if (detectionEvent == null)
            {
                return;
            }

            Stats.IncrementSuccessful();

            UpdateHealth(moduleId, imageConfig.Id, health => health.LastDetectedUtc = DateTime.UtcNow);

            await _triggerManager.FireOcrDetectedEvent(detectionEvent);

            if (imageConfig.Action.Enabled && _deviceActions != null)
            {
                var actionConfig = BuildOcrActionConfig(imageConfig, detectionEvent);
                PerformAction(actionConfig);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            UpdateHealth(moduleId, imageConfig.Id, health => health.LastError = ex.Message);
            ErrorOccurred?.Invoke($"OCR processing failed: {ex.Message}");
        }
    }

    private static ActionConfig BuildOcrActionConfig(DetectionImage imageConfig, OcrDetectionEvent detectionEvent)
    {
        var intensity = imageConfig.Action.Intensity;

        if (imageConfig.Ocr.ScaleActionWithNumberDelta && detectionEvent.DeltaNumber.HasValue)
        {
            var maxDelta = Math.Max(0.1, imageConfig.Ocr.MaxDeltaForMaxIntensity);
            var ratio = Math.Clamp(Math.Abs(detectionEvent.DeltaNumber.Value) / maxDelta, 0.0, 1.0);
            intensity = (int)Math.Clamp(Math.Round(imageConfig.Action.Intensity * ratio), 1, imageConfig.Action.Intensity);
        }

        return new ActionConfig
        {
            Enabled = true,
            Type = imageConfig.Action.Type,
            Intensity = intensity,
            DurationSeconds = imageConfig.Action.DurationSeconds,
            Mode = imageConfig.Action.Mode,
            RandomCountMin = imageConfig.Action.RandomCountMin,
            RandomCountMax = imageConfig.Action.RandomCountMax,
            ShockerIds = imageConfig.Action.ShockerIds
        };
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

    private bool IsDueAndFocused(DetectionImage image, long frame)
    {
        var cadence = Math.Max(1, image.ProcessEveryNthFrame);
        if ((frame - 1) % cadence != 0) return false;

        var (required, process, title) = image.TargetType switch
        {
            DetectionTargetType.Meter => (image.Meter.RequireFocusedWindow,
                image.Meter.RequiredFocusWindowProcess, image.Meter.RequiredFocusWindowTitle),
            DetectionTargetType.Ocr => (image.Ocr.RequireFocusedWindow,
                image.Ocr.RequiredFocusWindowProcess, image.Ocr.RequiredFocusWindowTitle),
            _ => (image.RequireFocusedWindow,
                image.RequiredFocusWindowProcess, image.RequiredFocusWindowTitle)
        };

        return !required || _captureService.IsRequiredWindowFocused(process, title);
    }

    private async Task DelayUntilNextLoop(CancellationToken ct)
    {
        var delayMs = _configService.CaptureConfig.CaptureDelayMs;
        if (delayMs > 0) await Task.Delay(delayMs, ct);
    }

    private static Mat ToBgr(Mat image)
    {
        if (image.NumberOfChannels == 3) return image.Clone();
        var result = new Mat();
        CvInvoke.CvtColor(image, result, image.NumberOfChannels == 4
            ? ColorConversion.Bgra2Bgr : ColorConversion.Gray2Bgr);
        return result;
    }

    private static Mat ToGray(Mat bgr)
    {
        if (bgr.NumberOfChannels == 1) return bgr.Clone();
        var result = new Mat();
        CvInvoke.CvtColor(bgr, result, ColorConversion.Bgr2Gray);
        return result;
    }

    private LoadedTemplate? GetOrLoadImage(string moduleId, DetectionImage imageConfig)
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
                var color = ToBgr(image);
                var gray = ToGray(color);
                var loaded = new LoadedTemplate(image, color, gray);
                _loadedImages[key] = loaded;
                return loaded;
            }

            return null;
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
        _templateLock.EnterWriteLock();
        try { ClearLoadedImages(); }
        finally { _templateLock.ExitWriteLock(); }
        _templateLock.Dispose();
        _configService.ConfigurationChanged -= OnConfigurationChanged;
    }

    private sealed class LoadedTemplate(Mat source, Mat color, Mat gray) : IDisposable
    {
        public Mat Source { get; } = source;
        public Mat Color { get; } = color;
        public Mat Gray { get; } = gray;
        public void Dispose()
        {
            Source.Dispose();
            Color.Dispose();
            Gray.Dispose();
        }
    }
}

/// <summary>
/// Statistics about detection performance.
/// </summary>
public class DetectionStats
{
    private long _totalDetections;
    private long _successfulDetections;
    private long _lastCaptureTicks;
    private long _lastLoopDurationTicks;

    public long TotalDetections => Interlocked.Read(ref _totalDetections);
    public long SuccessfulDetections => Interlocked.Read(ref _successfulDetections);
    public DateTime LastCaptureTime
    {
        get => new(Interlocked.Read(ref _lastCaptureTicks), DateTimeKind.Utc);
        set => Interlocked.Exchange(ref _lastCaptureTicks, value.Ticks);
    }
    public TimeSpan LastLoopDuration
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _lastLoopDurationTicks));
        set => Interlocked.Exchange(ref _lastLoopDurationTicks, value.Ticks);
    }

    public void IncrementTotal() => Interlocked.Increment(ref _totalDetections);
    public void IncrementSuccessful() => Interlocked.Increment(ref _successfulDetections);

    public double SuccessRate => TotalDetections > 0
        ? (double)SuccessfulDetections / TotalDetections
        : 0;

    public void Reset()
    {
        Interlocked.Exchange(ref _totalDetections, 0);
        Interlocked.Exchange(ref _successfulDetections, 0);
        LastCaptureTime = default;
        LastLoopDuration = default;
    }
}
