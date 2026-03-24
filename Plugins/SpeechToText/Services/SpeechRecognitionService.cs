using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace SpeechToText.Services;

public sealed class SpeechRecognitionService : IAsyncDisposable
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    private readonly SpeechToTextSettingsService _settings;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _sampleLock = new();
    private readonly List<float> _sampleBuffer = [];

    private CancellationTokenSource? _cts;
    private Task? _segmentationTask;
    private Task? _transcriptionTask;
    private Channel<TranscriptionChunk>? _chunks;

    private WaveInEvent? _waveIn;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    private bool _manualStopRequested;
    private bool _isDisposed;
    private int _restartRequested;
    private string? _activeConfigFingerprint;
    private double _currentInputLevel;
    private double _lastPublishedInputLevel;
    private DateTime _lastInputLevelPublishedUtc = DateTime.MinValue;

    public SpeechRecognitionService(SpeechToTextSettingsService settings)
    {
        _settings = settings;
    }

    public bool IsListening { get; private set; }

    public bool IsAvailable
    {
        get
        {
            try
            {
                return WaveInEvent.DeviceCount > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public event Action<bool>? ListeningStateChanged;

    public event Action<string>? StatusMessage;

    public event Action<string>? ErrorOccurred;

    public event Action<SpeechSentenceEvent>? SentenceRecognized;

    public event Action<double>? InputLevelChanged;

    public double CurrentInputLevel => _currentInputLevel;

    public IReadOnlyList<SpeechInputDevice> GetAvailableInputDevices()
    {
        var devices = new List<SpeechInputDevice>();

        try
        {
            var deviceCount = WaveInEvent.DeviceCount;
            for (var deviceNumber = 0; deviceNumber < deviceCount; deviceNumber++)
            {
                var capabilities = WaveInEvent.GetCapabilities(deviceNumber);
                var name = string.IsNullOrWhiteSpace(capabilities.ProductName)
                    ? $"Microphone {deviceNumber + 1}"
                    : capabilities.ProductName.Trim();

                devices.Add(new SpeechInputDevice(deviceNumber, name, capabilities.Channels));
            }
        }
        catch (Exception ex)
        {
            PublishError($"Unable to enumerate microphone devices: {ex.Message}");
        }

        return devices;
    }

    public async Task<bool> StartListeningAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);

        try
        {
            if (_isDisposed)
            {
                return false;
            }

            if (!IsAvailable)
            {
                PublishError("No recording device is available for Whisper speech recognition");
                return false;
            }

            var currentFingerprint = BuildConfigFingerprint();
            if (IsListening && string.Equals(_activeConfigFingerprint, currentFingerprint, StringComparison.Ordinal))
            {
                return true;
            }

            if (IsListening)
            {
                await CleanupListeningAsync();
            }

            _manualStopRequested = false;
            await InitializeWhisperAsync(cancellationToken);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _chunks = Channel.CreateBounded<TranscriptionChunk>(new BoundedChannelOptions(8)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

            _transcriptionTask = Task.Run(() => RunTranscriptionLoopAsync(_cts.Token), CancellationToken.None);
            _segmentationTask = Task.Run(() => RunSegmentationLoopAsync(_cts.Token), CancellationToken.None);

            var selectedInputDevice = ResolveInputDevice();

            _waveIn = CreateWaveIn(selectedInputDevice.DeviceNumber);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            try
            {
                _waveIn.StartRecording();
            }
            catch when (selectedInputDevice.DeviceNumber == -1)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();

                selectedInputDevice = ResolveFallbackInputDevice(devices: GetAvailableInputDevices());

                _waveIn = CreateWaveIn(selectedInputDevice.DeviceNumber);
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();

                PublishStatus($"System default microphone unavailable, using '{selectedInputDevice.Name}'");
            }

            _activeConfigFingerprint = currentFingerprint;
            SetListeningState(true);
            PublishInputLevel(0, force: true);
            PublishStatus($"Whisper listening started ({GetConfiguredLanguageLabel()}, model={_settings.WhisperModel}, mic={selectedInputDevice.Name}, minConfidence={_settings.MinimumConfidence:0.00}, silence={_settings.SilenceEnergyThreshold:0.0000})");
            return true;
        }
        catch (Exception ex)
        {
            PublishError($"Failed to start Whisper speech recognition: {ex.Message}");
            await CleanupListeningAsync();
            return false;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopListeningAsync()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            _manualStopRequested = true;
            await CleanupListeningAsync();
            PublishStatus("Whisper listening stopped");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task InitializeWhisperAsync(CancellationToken cancellationToken)
    {
        await DisposeWhisperAsync();

        var modelPath = await ResolveModelPathAsync(cancellationToken);

        _factory = WhisperFactory.FromPath(modelPath);

        var builder = _factory.CreateBuilder()
            .WithNoContext()
            .WithThreads(Math.Max(1, Environment.ProcessorCount / 2));

        var languageCode = NormalizeLanguageCode(_settings.Culture);
        if (string.Equals(languageCode, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder.WithLanguageDetection();
        }
        else
        {
            builder.WithLanguage(languageCode);
        }

        _processor = builder.Build();
        PublishStatus($"Loaded Whisper model from '{modelPath}'");
    }

    private async Task<string> ResolveModelPathAsync(CancellationToken cancellationToken)
    {
        var configuredPath = _settings.WhisperModelPath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullConfiguredPath = Path.GetFullPath(configuredPath);
            if (File.Exists(fullConfiguredPath))
            {
                return fullConfiguredPath;
            }

            throw new FileNotFoundException($"Configured Whisper model was not found: {fullConfiguredPath}");
        }

        if (!Enum.TryParse<GgmlType>(_settings.WhisperModel, true, out var modelType))
        {
            modelType = GgmlType.TinyEn;
        }

        var modelsDirectory = Path.Combine(_settings.PluginDataPath, "models");
        Directory.CreateDirectory(modelsDirectory);

        var modelPath = Path.Combine(modelsDirectory, $"ggml-{GetModelSlug(modelType)}.bin");
        if (File.Exists(modelPath))
        {
            return modelPath;
        }

        if (!_settings.DownloadModelIfMissing)
        {
            throw new FileNotFoundException($"Whisper model not found at '{modelPath}'. Enable auto-download or set a model path.");
        }

        PublishStatus($"Downloading Whisper model '{modelType}'...");
        var downloadStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(modelType, default, cancellationToken);

        var tempPath = $"{modelPath}.download";
        await using (downloadStream)
        await using (var fs = File.Create(tempPath))
        {
            await downloadStream.CopyToAsync(fs, cancellationToken);
        }

        if (File.Exists(modelPath))
        {
            File.Delete(modelPath);
        }

        File.Move(tempPath, modelPath);
        PublishStatus($"Whisper model downloaded to '{modelPath}'");
        return modelPath;
    }

    private async Task RunSegmentationLoopAsync(CancellationToken cancellationToken)
    {
        if (_chunks == null)
        {
            return;
        }

        var segmentSamples = (int)(SampleRate * (_settings.SegmentDurationMs / 1000.0));
        segmentSamples = Math.Max(segmentSamples, SampleRate);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(180, cancellationToken);

            var segment = TryTakeSegment(segmentSamples);
            if (segment == null)
            {
                continue;
            }

            var rmsEnergy = CalculateRmsEnergy(segment);
            if (rmsEnergy < _settings.SilenceEnergyThreshold)
            {
                continue;
            }

            await _chunks.Writer.WriteAsync(new TranscriptionChunk(segment), cancellationToken);
        }
    }

    private async Task RunTranscriptionLoopAsync(CancellationToken cancellationToken)
    {
        if (_chunks == null)
        {
            return;
        }

        var reader = _chunks.Reader;

        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var chunk))
            {
                await ProcessChunkAsync(chunk, cancellationToken);
            }
        }
    }

    private async Task ProcessChunkAsync(TranscriptionChunk chunk, CancellationToken cancellationToken)
    {
        if (_processor == null)
        {
            return;
        }

        try
        {
            await foreach (var segment in _processor.ProcessAsync(chunk.Samples, cancellationToken))
            {
                var text = segment.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var confidence = ResolveConfidence(segment);
                if (confidence < _settings.MinimumConfidence)
                {
                    continue;
                }

                var language = string.IsNullOrWhiteSpace(segment.Language)
                    ? GetConfiguredLanguageLabel()
                    : segment.Language;

                SentenceRecognized?.Invoke(new SpeechSentenceEvent(
                    text,
                    confidence,
                    DateTimeOffset.UtcNow,
                    language,
                    $"provider=whisper;model={_settings.WhisperModel};segmentMs={_settings.SegmentDurationMs};samples={chunk.Samples.Length}"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            PublishError($"Whisper transcription failed: {ex.Message}");

            if (!_manualStopRequested &&
                _settings.AutoRestartOnFailure &&
                IsListening)
            {
                _ = RestartAfterDelayAsync();
            }
        }
    }

    private float[]? TryTakeSegment(int targetSamples)
    {
        lock (_sampleLock)
        {
            if (_sampleBuffer.Count < targetSamples)
            {
                return null;
            }

            var segment = _sampleBuffer.Take(targetSamples).ToArray();
            _sampleBuffer.RemoveRange(0, targetSamples);
            return segment;
        }
    }

    private void OnDataAvailable(object? _, WaveInEventArgs e)
    {
        var samples = e.BytesRecorded / 2;
        if (samples <= 0)
        {
            return;
        }

        double sumSquares = 0;

        lock (_sampleLock)
        {
            for (var i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                var normalized = sample / 32768f;
                _sampleBuffer.Add(normalized);
                sumSquares += normalized * normalized;
            }

            var maxSamplesToKeep = SampleRate * 30;
            if (_sampleBuffer.Count > maxSamplesToKeep)
            {
                _sampleBuffer.RemoveRange(0, _sampleBuffer.Count - maxSamplesToKeep);
            }
        }

        var rms = Math.Sqrt(sumSquares / samples);
        var scaled = Math.Clamp(rms * 8.0, 0.0, 1.0);
        var smoothed = (_currentInputLevel * 0.72) + (scaled * 0.28);
        _currentInputLevel = smoothed;
        PublishInputLevel(smoothed);
    }

    private void OnRecordingStopped(object? _, StoppedEventArgs e)
    {
        if (e.Exception == null)
        {
            return;
        }

        PublishError($"Audio capture stopped unexpectedly: {e.Exception.Message}");

        if (_manualStopRequested || !_settings.AutoRestartOnFailure || !IsListening)
        {
            return;
        }

        _ = RestartAfterDelayAsync();
    }

    private async Task RestartAfterDelayAsync()
    {
        if (Interlocked.Exchange(ref _restartRequested, 1) == 1)
        {
            return;
        }

        try
        {
            PublishStatus($"Attempting Whisper restart in {_settings.RestartDelayMs}ms");
            await Task.Delay(_settings.RestartDelayMs);
            await StartListeningAsync();
        }
        catch (Exception ex)
        {
            PublishError($"Whisper restart failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _restartRequested, 0);
        }
    }

    private async Task CleanupListeningAsync()
    {
        var cts = _cts;
        _cts = null;

        if (cts != null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (_waveIn != null)
        {
            try
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.StopRecording();
            }
            catch
            {
            }

            _waveIn.Dispose();
            _waveIn = null;
        }

        if (_chunks != null)
        {
            _chunks.Writer.TryComplete();
        }

        var segmentationTask = _segmentationTask;
        _segmentationTask = null;
        await AwaitTask(segmentationTask);

        var transcriptionTask = _transcriptionTask;
        _transcriptionTask = null;
        await AwaitTask(transcriptionTask);

        lock (_sampleLock)
        {
            _sampleBuffer.Clear();
        }

        _chunks = null;
        _activeConfigFingerprint = null;
        _currentInputLevel = 0;
        PublishInputLevel(0, force: true);

        await DisposeWhisperAsync();
        SetListeningState(false);
    }

    private async Task DisposeWhisperAsync()
    {
        if (_processor != null)
        {
            await _processor.DisposeAsync();
            _processor = null;
        }

        if (_factory != null)
        {
            _factory.Dispose();
            _factory = null;
        }
    }

    private static async Task AwaitTask(Task? task)
    {
        if (task == null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static double CalculateRmsEnergy(float[] samples)
    {
        if (samples.Length == 0)
        {
            return 0.0;
        }

        double sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return Math.Sqrt(sum / samples.Length);
    }

    private void PublishInputLevel(double level, bool force = false)
    {
        level = Math.Clamp(level, 0, 1);

        var now = DateTime.UtcNow;
        if (!force)
        {
            var delta = Math.Abs(level - _lastPublishedInputLevel);
            var elapsed = now - _lastInputLevelPublishedUtc;
            if (delta < 0.01 && elapsed.TotalMilliseconds < 120)
            {
                return;
            }
        }

        _lastPublishedInputLevel = level;
        _lastInputLevelPublishedUtc = now;
        InputLevelChanged?.Invoke(level);
    }

    private static double ResolveConfidence(SegmentData segment)
    {
        var value = segment.Probability;
        if (double.IsFinite(value) && value > 0)
        {
            return Math.Clamp(value, 0, 1);
        }

        value = segment.MaxProbability;
        if (double.IsFinite(value) && value > 0)
        {
            return Math.Clamp(value, 0, 1);
        }

        return 0.75;
    }

    private string BuildConfigFingerprint()
    {
        var buffer = new StringBuilder();
        buffer.Append(_settings.Culture).Append('|');
        buffer.Append(_settings.WhisperModel).Append('|');
        buffer.Append(_settings.WhisperModelPath).Append('|');
        buffer.Append(_settings.DownloadModelIfMissing).Append('|');
        buffer.Append(_settings.SegmentDurationMs).Append('|');
        buffer.Append(_settings.SilenceEnergyThreshold).Append('|');
        buffer.Append(_settings.UseDefaultMicrophone).Append('|');
        buffer.Append(_settings.MicrophoneDeviceNumber).Append('|');
        buffer.Append(_settings.MinimumConfidence);
        return buffer.ToString();
    }

    private SpeechInputDevice ResolveInputDevice()
    {
        var devices = GetAvailableInputDevices();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No microphone input devices found");
        }

        if (_settings.UseDefaultMicrophone)
        {
            return new SpeechInputDevice(-1, "System Default", 1);
        }

        var match = devices.FirstOrDefault(x => x.DeviceNumber == _settings.MicrophoneDeviceNumber);
        return match ?? devices[0];
    }

    private static SpeechInputDevice ResolveFallbackInputDevice(IReadOnlyList<SpeechInputDevice> devices)
    {
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No microphone input devices found");
        }

        return devices[0];
    }

    private static WaveInEvent CreateWaveIn(int deviceNumber)
    {
        return new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            BufferMilliseconds = 125,
            NumberOfBuffers = 3,
        };
    }

    private string GetConfiguredLanguageLabel()
    {
        var normalized = NormalizeLanguageCode(_settings.Culture);
        return string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : normalized;
    }

    private static string NormalizeLanguageCode(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return "en";
        }

        var value = culture.Trim();
        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        var dashIndex = value.IndexOf('-');
        if (dashIndex > 0)
        {
            value = value[..dashIndex];
        }

        return value.ToLowerInvariant();
    }

    private static string GetModelSlug(GgmlType modelType)
    {
        return modelType switch
        {
            GgmlType.Tiny => "tiny",
            GgmlType.TinyEn => "tiny.en",
            GgmlType.Base => "base",
            GgmlType.BaseEn => "base.en",
            GgmlType.Small => "small",
            GgmlType.SmallEn => "small.en",
            GgmlType.Medium => "medium",
            GgmlType.MediumEn => "medium.en",
            GgmlType.LargeV1 => "large-v1",
            GgmlType.LargeV2 => "large-v2",
            GgmlType.LargeV3 => "large-v3",
            GgmlType.LargeV3Turbo => "large-v3-turbo",
            _ => "tiny.en",
        };
    }

    private void SetListeningState(bool isListening)
    {
        if (IsListening == isListening)
        {
            return;
        }

        IsListening = isListening;
        ListeningStateChanged?.Invoke(isListening);
    }

    private void PublishStatus(string message)
    {
        SpeechToTextPlugin.Logger?.LogInformation("{Message}", message);
        StatusMessage?.Invoke(message);
    }

    private void PublishError(string message)
    {
        SpeechToTextPlugin.Logger?.LogWarning("{Message}", message);
        ErrorOccurred?.Invoke(message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await StopListeningAsync();
        _lifecycleLock.Dispose();
    }

    private sealed record TranscriptionChunk(float[] Samples);
}

public sealed record SpeechInputDevice(int DeviceNumber, string Name, int Channels);

public sealed record SpeechSentenceEvent(
    string Text,
    double Confidence,
    DateTimeOffset TimestampUtc,
    string Culture,
    string Context);
