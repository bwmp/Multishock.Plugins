using ImageDetection.Algorithms;
using ImageDetection.Nodes;
using ImageDetection.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection;

/// <summary>
/// Image Detection Plugin - Detects images on screen and triggers actions.
/// </summary>
public class ImageDetectionPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    public const string PluginId = "com.multishock.imagedetection";

    private ImageDetectionService? _detectionService;
    private ImageConfigService? _configService;

    // ========== PLUGIN METADATA ==========

    public string Id => PluginId;
    public string Name => "Image Detection";
    public string Version => $"1.0.0+{BuildStamp.Stamp}";
    public string Description => "Detects images on screen and triggers actions when found.";

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        // Register algorithm registry with default algorithms
        services.AddSingleton<AlgorithmRegistry>(sp =>
        {
            var registry = new AlgorithmRegistry();
            registry.Register(new TemplateMatchingAlgorithm());
            registry.Register(new MaskedTemplateMatchingAlgorithm());
            return registry;
        });

        // Core services - use simple registration for Blazor DI compatibility
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<CooldownManager>();
        services.AddSingleton<DetectionTriggerManager>();
        services.AddSingleton<RecentDetectionsService>();

        // Config service depends on IPluginHost
        services.AddSingleton<ImageConfigService>(sp =>
        {
            var host = sp.GetRequiredService<IPluginHost>();
            return new ImageConfigService(host);
        });

        // Main detection service
        services.AddSingleton<ImageDetectionService>(sp =>
        {
            var configService = sp.GetRequiredService<ImageConfigService>();
            var captureService = sp.GetRequiredService<ScreenCaptureService>();
            var cooldownManager = sp.GetRequiredService<CooldownManager>();
            var triggerManager = sp.GetRequiredService<DetectionTriggerManager>();
            var algorithmRegistry = sp.GetRequiredService<AlgorithmRegistry>();
            var recentDetections = sp.GetRequiredService<RecentDetectionsService>();
            var deviceActions = sp.GetService<IDeviceActions>();

            return new ImageDetectionService(
                configService,
                captureService,
                cooldownManager,
                triggerManager,
                algorithmRegistry,
                recentDetections,
                deviceActions);
        });
    }

    public void Initialize(IServiceProvider sp)
    {
        // Initialize native libraries before any Emgu.CV usage
        NativeLibraryLoader.Initialize();

        _configService = sp.GetService<ImageConfigService>();
        _detectionService = sp.GetService<ImageDetectionService>();

        // Initialize trigger manager (registers for events)
        _ = sp.GetService<DetectionTriggerManager>();

        // Auto-start detection if configured
        if (_configService?.BackgroundDetectionEnabled == true)
        {
            _detectionService?.Start();
        }
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(Components.PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["backgroundDetectionEnabled"] = false,
        ["captureDelayMs"] = 100,
        ["defaultThreshold"] = 0.8,
        ["debugMode"] = false,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        if (_configService == null) return;

        if (settings.TryGetValue("backgroundDetectionEnabled", out var enabled) && enabled is bool isEnabled)
        {
            _configService.BackgroundDetectionEnabled = isEnabled;

            if (isEnabled && _detectionService?.IsRunning != true)
            {
                _detectionService?.Start();
            }
            else if (!isEnabled && _detectionService?.IsRunning == true)
            {
                _ = _detectionService.StopAsync();
            }
        }

        if (settings.TryGetValue("debugMode", out var debug) && debug is bool isDebug)
        {
            _configService.DebugMode = isDebug;
        }
    }

    // ========== STYLES (IPluginWithStyles) ==========

    public string GetStylesheet() => """
        /* Module cards */
        .imgdet-card {
            border: 1px solid var(--app-border);
            padding: 0.75rem;
            border-radius: 0.5rem;
            background: var(--app-surface);
        }
        
        .imgdet-card:hover {
            border-color: var(--app-primary);
        }
        
        /* Status indicators */
        .imgdet-status-running {
            color: #22c55e;
        }
        
        .imgdet-status-stopped {
            color: #ef4444;
        }
        
        /* Image thumbnails */
        .imgdet-thumbnail {
            max-width: 80px;
            max-height: 80px;
            border-radius: 0.25rem;
            object-fit: contain;
        }
        
        /* Grid section selector */
        .imgdet-grid-section {
            width: 2rem;
            height: 2rem;
            border: 1px solid var(--app-border);
            cursor: pointer;
            transition: background-color 0.2s;
        }
        
        .imgdet-grid-section.enabled {
            background-color: var(--app-primary);
        }
        
        .imgdet-grid-section:hover {
            opacity: 0.8;
        }
        
        /* Detection log */
        .imgdet-log-entry {
            font-family: monospace;
            font-size: 0.75rem;
            padding: 0.25rem 0.5rem;
            border-radius: 0.25rem;
        }
        
        .imgdet-log-success {
            background-color: rgba(34, 197, 94, 0.1);
            color: #22c55e;
        }
        
        .imgdet-log-info {
            background-color: rgba(59, 130, 246, 0.1);
            color: #3b82f6;
        }
        
        .imgdet-log-error {
            background-color: rgba(239, 68, 68, 0.1);
            color: #ef4444;
        }
        
        /* Range slider styling */
        input[type="range"] {
            -webkit-appearance: none;
            width: 100%;
            height: 6px;
            border-radius: 3px;
            background: #3f3f46;
        }
        
        input[type="range"]::-webkit-slider-thumb {
            -webkit-appearance: none;
            width: 16px;
            height: 16px;
            border-radius: 50%;
            background: #3b82f6;
            cursor: pointer;
            transition: background 0.15s;
        }
        
        input[type="range"]::-webkit-slider-thumb:hover {
            background: #60a5fa;
        }
        
        input[type="range"]::-moz-range-thumb {
            width: 16px;
            height: 16px;
            border-radius: 50%;
            background: #3b82f6;
            cursor: pointer;
            border: none;
        }
        
        /* Pulse animation for running indicator */
        @keyframes pulse {
            0%, 100% { opacity: 1; }
            50% { opacity: 0.5; }
        }
        
        .animate-pulse {
            animation: pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
        }
        
        /* Focus styles for inputs */
        select:focus, input:focus {
            outline: none;
            border-color: #3b82f6;
        }
        
        /* Recent Detections Component */
        .recent-detections {
            background: var(--app-surface);
            border: 1px solid var(--app-border);
            border-radius: 0.5rem;
            overflow: hidden;
        }
        
        .detections-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 0.75rem 1rem;
            border-bottom: 1px solid var(--app-border);
            background: var(--app-surface-alt, var(--app-surface));
        }
        
        .detections-header h4 {
            margin: 0;
            font-size: 0.875rem;
            font-weight: 600;
        }
        
        .header-actions {
            display: flex;
            align-items: center;
            gap: 0.5rem;
        }
        
        .detection-count {
            font-size: 0.75rem;
            color: var(--app-text-muted);
        }
        
        .btn-clear {
            background: none;
            border: none;
            color: var(--app-text-muted);
            cursor: pointer;
            padding: 0.25rem;
            border-radius: 0.25rem;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        
        .btn-clear:hover {
            color: #ef4444;
            background: rgba(239, 68, 68, 0.1);
        }
        
        .detections-list {
            max-height: 400px;
            overflow-y: auto;
        }
        
        .detection-item {
            display: flex;
            gap: 0.75rem;
            padding: 0.75rem 1rem;
            border-bottom: 1px solid var(--app-border);
            transition: background-color 0.15s;
        }
        
        .detection-item:last-child {
            border-bottom: none;
        }
        
        .detection-item:hover {
            background: var(--app-surface-hover, rgba(255,255,255,0.05));
        }
        
        .detection-item.triggered {
            border-left: 3px solid #22c55e;
        }
        
        .detection-item.cooldown {
            border-left: 3px solid #f59e0b;
            opacity: 0.7;
        }
        
        .detection-thumbnail {
            width: 60px;
            height: 60px;
            border-radius: 0.375rem;
            overflow: hidden;
            flex-shrink: 0;
            cursor: pointer;
            background: var(--app-surface-alt, #27272a);
            display: flex;
            align-items: center;
            justify-content: center;
        }
        
        .detection-thumbnail img {
            width: 100%;
            height: 100%;
            object-fit: cover;
        }
        
        .no-thumbnail {
            color: var(--app-text-muted);
        }
        
        .detection-info {
            flex: 1;
            min-width: 0;
        }
        
        .detection-name {
            font-weight: 500;
            font-size: 0.875rem;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }
        
        .detection-meta {
            display: flex;
            gap: 0.5rem;
            font-size: 0.75rem;
            color: var(--app-text-muted);
            margin-top: 0.25rem;
        }
        
        .detection-stats {
            display: flex;
            gap: 0.5rem;
            align-items: center;
            margin-top: 0.375rem;
        }
        
        .confidence {
            display: flex;
            align-items: center;
            gap: 0.25rem;
            font-size: 0.75rem;
            color: #22c55e;
        }
        
        .action-badge {
            font-size: 0.625rem;
            padding: 0.125rem 0.375rem;
            border-radius: 0.25rem;
            font-weight: 500;
            text-transform: uppercase;
        }
        
        .action-badge.shock {
            background: rgba(239, 68, 68, 0.2);
            color: #ef4444;
        }
        
        .action-badge.vibrate {
            background: rgba(59, 130, 246, 0.2);
            color: #3b82f6;
        }
        
        .action-badge.beep {
            background: rgba(245, 158, 11, 0.2);
            color: #f59e0b;
        }
        
        .cooldown-badge {
            font-size: 0.625rem;
            padding: 0.125rem 0.375rem;
            border-radius: 0.25rem;
            background: rgba(107, 114, 128, 0.2);
            color: #9ca3af;
        }
        
        .no-detections {
            padding: 2rem;
            text-align: center;
            color: var(--app-text-muted);
        }
        
        .no-detections svg {
            margin-bottom: 0.75rem;
            opacity: 0.5;
        }
        
        .no-detections p {
            margin: 0;
            font-weight: 500;
        }
        
        .no-detections .hint {
            font-size: 0.75rem;
            margin-top: 0.25rem;
            display: block;
        }
        
        .detections-stats {
            display: flex;
            justify-content: space-around;
            padding: 0.75rem 1rem;
            border-top: 1px solid var(--app-border);
            background: var(--app-surface-alt, var(--app-surface));
        }
        
        .stat {
            text-align: center;
        }
        
        .stat-value {
            display: block;
            font-size: 1.125rem;
            font-weight: 600;
            color: var(--app-text);
        }
        
        .stat-label {
            font-size: 0.625rem;
            text-transform: uppercase;
            color: var(--app-text-muted);
            letter-spacing: 0.05em;
        }
        
        /* Detection Modal */
        .detection-modal-overlay {
            position: fixed;
            inset: 0;
            background: rgba(0, 0, 0, 0.75);
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: 1000;
            padding: 1rem;
        }
        
        .detection-modal {
            background: var(--app-surface);
            border: 1px solid var(--app-border);
            border-radius: 0.5rem;
            max-width: 900px;
            width: 100%;
            max-height: 90vh;
            overflow: hidden;
            display: flex;
            flex-direction: column;
        }
        
        .modal-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 1rem;
            border-bottom: 1px solid var(--app-border);
        }
        
        .modal-header h4 {
            margin: 0;
        }
        
        .btn-close {
            background: none;
            border: none;
            color: var(--app-text-muted);
            cursor: pointer;
            padding: 0.25rem;
            border-radius: 0.25rem;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        
        .btn-close:hover {
            color: var(--app-text);
            background: rgba(255, 255, 255, 0.1);
        }
        
        .modal-content {
            overflow-y: auto;
            padding: 1rem;
        }
        
        .screenshot-container {
            position: relative;
            margin-bottom: 1rem;
            border-radius: 0.375rem;
            overflow: hidden;
            background: #000;
        }
        
        .screenshot-container img {
            width: 100%;
            height: auto;
            display: block;
        }
        
        .detection-marker {
            position: absolute;
            border: 2px solid #22c55e;
            background: rgba(34, 197, 94, 0.2);
            pointer-events: none;
        }
        
        .details-grid {
            display: grid;
            gap: 0.5rem;
        }
        
        .detail-row {
            display: flex;
            justify-content: space-between;
            padding: 0.5rem 0;
            border-bottom: 1px solid var(--app-border);
        }
        
        .detail-row:last-child {
            border-bottom: none;
        }
        
        .detail-row .label {
            color: var(--app-text-muted);
            font-size: 0.875rem;
        }
        
        .detail-row .value {
            font-weight: 500;
            font-size: 0.875rem;
        }
        
        .detail-row .value.action {
            color: #22c55e;
        }
        
        .detail-row .value.cooldown {
            color: #f59e0b;
        }
        
        /* Visual Section Selector Modal */
        .visual-selector-modal {
            position: fixed;
            inset: 0;
            z-index: 1000;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        }
        
        .modal-backdrop {
            position: absolute;
            inset: 0;
            background: rgba(0, 0, 0, 0.8);
            backdrop-filter: blur(4px);
        }
        
        .modal-content {
            position: relative;
            background: var(--app-surface);
            border: 1px solid var(--app-border);
            border-radius: 12px;
            max-width: 90vw;
            max-height: 90vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
            box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
        }
        
        .modal-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 16px 20px;
            border-bottom: 1px solid var(--app-border);
            background: var(--app-surface-alt, var(--app-surface));
        }
        
        .modal-header h4 {
            margin: 0;
            font-size: 1.125rem;
            font-weight: 600;
        }
        
        .resolution-info {
            display: block;
            font-size: 0.75rem;
            color: var(--app-text-muted);
            margin-top: 4px;
        }
        
        .header-info {
            display: flex;
            align-items: center;
            gap: 12px;
        }
        
        .status-badge {
            font-size: 0.75rem;
            padding: 4px 12px;
            border-radius: 20px;
            background: rgba(59, 130, 246, 0.2);
            color: #3b82f6;
            font-weight: 500;
        }
        
        .btn-close {
            background: none;
            border: none;
            font-size: 1.5rem;
            color: var(--app-text-muted);
            cursor: pointer;
            padding: 0;
            width: 32px;
            height: 32px;
            display: flex;
            align-items: center;
            justify-content: center;
            border-radius: 6px;
            transition: all 0.2s;
        }
        
        .btn-close:hover {
            background: rgba(255, 255, 255, 0.1);
            color: var(--app-text);
        }
        
        .modal-body {
            flex: 1;
            overflow: auto;
            padding: 20px;
            display: flex;
            flex-direction: column;
            gap: 16px;
        }
        
        .screenshot-info {
            display: flex;
            gap: 8px;
            justify-content: center;
        }
        
        .info-badge {
            font-size: 0.75rem;
            padding: 4px 10px;
            border-radius: 12px;
            background: rgba(100, 100, 100, 0.3);
            color: var(--app-text-muted);
        }
        
        .screenshot-container {
            position: relative;
            display: inline-block;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.3);
            align-self: center;
        }
        
        .screenshot-container img {
            display: block;
            max-width: 100%;
            max-height: 60vh;
            object-fit: contain;
        }
        
        .grid-overlay {
            position: absolute;
            inset: 0;
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            grid-template-rows: repeat(3, 1fr);
        }
        
        .grid-cell {
            position: relative;
            border: 2px solid rgba(255, 255, 255, 0.3);
            cursor: pointer;
            transition: all 0.2s;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
        }
        
        .grid-cell:hover {
            border-color: rgba(255, 255, 255, 0.8);
            transform: scale(0.98);
        }
        
        .grid-cell.enabled {
            background: rgba(34, 197, 94, 0.25);
            border-color: #22c55e;
        }
        
        .grid-cell.enabled:hover {
            background: rgba(34, 197, 94, 0.35);
        }
        
        .grid-cell.disabled {
            background: rgba(239, 68, 68, 0.25);
            border-color: #ef4444;
        }
        
        .grid-cell.disabled:hover {
            background: rgba(239, 68, 68, 0.35);
        }
        
        .cell-number {
            font-size: 1.5rem;
            font-weight: bold;
            color: white;
            text-shadow: 0 2px 4px rgba(0, 0, 0, 0.8);
        }
        
        .cell-indicator {
            position: absolute;
            top: 8px;
            right: 8px;
            width: 12px;
            height: 12px;
            border-radius: 50%;
        }
        
        .grid-cell.enabled .cell-indicator {
            background: #22c55e;
            box-shadow: 0 0 8px #22c55e;
        }
        
        .grid-cell.disabled .cell-indicator {
            background: #ef4444;
            box-shadow: 0 0 8px #ef4444;
        }
        
        .controls-bar {
            display: flex;
            gap: 8px;
            justify-content: center;
            flex-wrap: wrap;
        }
        
        .btn-control {
            padding: 8px 16px;
            border: none;
            border-radius: 6px;
            background: var(--app-surface-alt, #3f3f46);
            color: var(--app-text);
            font-size: 0.875rem;
            cursor: pointer;
            transition: all 0.2s;
        }
        
        .btn-control:hover {
            background: var(--app-surface-hover, #52525b);
        }
        
        .btn-control.btn-invert {
            background: rgba(139, 92, 246, 0.2);
            color: #8b5cf6;
        }
        
        .btn-control.btn-invert:hover {
            background: rgba(139, 92, 246, 0.3);
        }
        
        .btn-control.btn-refresh {
            background: rgba(59, 130, 246, 0.2);
            color: #3b82f6;
        }
        
        .btn-control.btn-refresh:hover {
            background: rgba(59, 130, 246, 0.3);
        }
        
        .modal-footer {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 16px 20px;
            border-top: 1px solid var(--app-border);
            background: var(--app-surface-alt, var(--app-surface));
            gap: 16px;
        }
        
        .help-text {
            font-size: 0.875rem;
            color: var(--app-text-muted);
            margin: 0;
        }
        
        .btn-done {
            padding: 8px 24px;
            border: none;
            border-radius: 6px;
            background: #22c55e;
            color: white;
            font-size: 0.875rem;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.2s;
        }
        
        .btn-done:hover {
            background: #16a34a;
        }
        
        /* Resolution Input Dialog */
        .resolution-dialog-overlay {
            position: fixed;
            inset: 0;
            background: rgba(0, 0, 0, 0.8);
            backdrop-filter: blur(4px);
            z-index: 1000;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        }
        
        .resolution-dialog {
            background: var(--app-surface);
            border: 1px solid var(--app-border);
            border-radius: 12px;
            max-width: 500px;
            width: 100%;
            box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
        }
        
        .dialog-header {
            padding: 20px;
            border-bottom: 1px solid var(--app-border);
        }
        
        .dialog-header h4 {
            margin: 0 0 4px 0;
            font-size: 1.25rem;
        }
        
        .dialog-subtitle {
            margin: 0;
            font-size: 0.875rem;
            color: var(--app-text-muted);
        }
        
        .dialog-body {
            padding: 20px;
            display: flex;
            flex-direction: column;
            gap: 20px;
        }
        
        .info-box {
            display: flex;
            gap: 12px;
            padding: 12px;
            background: rgba(59, 130, 246, 0.1);
            border: 1px solid rgba(59, 130, 246, 0.3);
            border-radius: 8px;
        }
        
        .info-box .info-icon {
            width: 20px;
            height: 20px;
            color: #3b82f6;
            flex-shrink: 0;
        }
        
        .info-box p {
            margin: 0;
            font-size: 0.875rem;
            color: var(--app-text);
        }
        
        .resolution-inputs {
            display: flex;
            align-items: flex-end;
            gap: 12px;
        }
        
        .input-group {
            flex: 1;
            display: flex;
            flex-direction: column;
            gap: 6px;
        }
        
        .input-group label {
            font-size: 0.75rem;
            color: var(--app-text-muted);
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }
        
        .input-group input {
            padding: 10px 12px;
            border: 1px solid var(--app-border);
            border-radius: 6px;
            background: var(--app-surface-alt, #27272a);
            color: var(--app-text);
            font-size: 1rem;
        }
        
        .input-group input:focus {
            outline: none;
            border-color: #3b82f6;
        }
        
        .separator {
            font-size: 1.25rem;
            color: var(--app-text-muted);
            padding-bottom: 10px;
        }
        
        .presets {
            display: flex;
            flex-direction: column;
            gap: 8px;
        }
        
        .presets-label {
            font-size: 0.75rem;
            color: var(--app-text-muted);
        }
        
        .preset-buttons {
            display: flex;
            flex-wrap: wrap;
            gap: 8px;
        }
        
        .preset-buttons button {
            padding: 6px 12px;
            border: 1px solid var(--app-border);
            border-radius: 6px;
            background: var(--app-surface-alt, #3f3f46);
            color: var(--app-text);
            font-size: 0.75rem;
            cursor: pointer;
            transition: all 0.2s;
        }
        
        .preset-buttons button:hover {
            background: var(--app-surface-hover, #52525b);
            border-color: #3b82f6;
        }
        
        .warning-box {
            display: flex;
            gap: 12px;
            padding: 12px;
            background: rgba(245, 158, 11, 0.1);
            border: 1px solid rgba(245, 158, 11, 0.3);
            border-radius: 8px;
        }
        
        .warning-box svg {
            width: 20px;
            height: 20px;
            color: #f59e0b;
            flex-shrink: 0;
        }
        
        .warning-box p {
            margin: 0;
            font-size: 0.875rem;
            color: var(--app-text);
        }
        
        .dialog-footer {
            display: flex;
            justify-content: flex-end;
            gap: 12px;
            padding: 16px 20px;
            border-top: 1px solid var(--app-border);
            background: var(--app-surface-alt, var(--app-surface));
        }
        
        .btn-secondary {
            padding: 10px 20px;
            border: 1px solid var(--app-border);
            border-radius: 6px;
            background: transparent;
            color: var(--app-text);
            font-size: 0.875rem;
            cursor: pointer;
            transition: all 0.2s;
        }
        
        .btn-secondary:hover {
            background: rgba(255, 255, 255, 0.1);
        }
        
        .btn-primary {
            padding: 10px 20px;
            border: none;
            border-radius: 6px;
            background: #3b82f6;
            color: white;
            font-size: 0.875rem;
            cursor: pointer;
            transition: all 0.2s;
        }
        
        .btn-primary:hover {
            background: #2563eb;
        }
        """;

    public string? GetStylesheetId() => "imagedetection-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Image Detection",
            Href = "/imagedetection",
            Icon = "scan-search",
            Order = 50
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        // Trigger nodes
        yield return new ImageDetectedTriggerNode();

        // Process nodes
        yield return new TakeScreenshotNode();
        yield return new DetectImageNode();
        yield return new StartDetectionNode();
        yield return new StopDetectionNode();
    }
}
