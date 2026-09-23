#if !WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

public sealed class DesktopScreenCaptureService(ILogger? logger = null) : IScreenCaptureService
{
    private readonly ILogger? _logger = logger;
    private CaptureConfig _config = new();

    public bool IsSupported => OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
        ? File.Exists("/usr/sbin/screencapture")
        : OperatingSystem.IsLinux() && FindLinuxCaptureCommand() is not null;

    public string? UnsupportedReason => IsSupported
        ? null
        : OperatingSystem.IsLinux()
            ? "Install grim, gnome-screenshot, or scrot to enable Linux screen capture."
            : "Screen capture is unavailable on this platform.";

    public string? DpiWarning => null;

    public CaptureConfig Config
    {
        get => _config;
        set => _config = value ?? new CaptureConfig();
    }

    public Mat CaptureScreen() => Config.SourceType switch
    {
        CaptureSourceType.Window => CaptureWindow(Config.WindowTitle, Config.WindowId),
        _ => CaptureMonitor(Config.MonitorIndex)
    };

    public Mat CaptureMonitor(int monitorIndex = 1)
    {
        EnsureSupported();
        var outputPath = CreateTemporaryPngPath();
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            {
                var arguments = new List<string> { "-x" };
                if (Config.IncludeCursor) arguments.Add("-C");
                if (monitorIndex > 0) arguments.AddRange(["-D", monitorIndex.ToString()]);
                arguments.Add(outputPath);
                RunCapture("/usr/sbin/screencapture", arguments);
            }
            else
            {
                CaptureLinuxScreen(outputPath);
            }

            return LoadCapture(outputPath);
        }
        finally
        {
            DeleteTemporaryFile(outputPath);
        }
    }

    public Mat CaptureWindow(string? windowTitle, string? windowId = null)
    {
        EnsureSupported();
        var outputPath = CreateTemporaryPngPath();
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            {
                if (string.IsNullOrWhiteSpace(windowId))
                {
                    throw new NotSupportedException("macOS window capture requires a window identifier. Select a window from the refreshed list.");
                }

                RunCapture("/usr/sbin/screencapture", ["-x", "-l", windowId, outputPath]);
            }
            else
            {
                var import = FindExecutable("import");
                if (import is null || string.IsNullOrWhiteSpace(windowId))
                {
                    throw new NotSupportedException("Linux window capture requires ImageMagick's import command and an X11 window identifier.");
                }

                RunCapture(import, ["-window", windowId, outputPath]);
            }

            return LoadCapture(outputPath);
        }
        finally
        {
            DeleteTemporaryFile(outputPath);
        }
    }

    public Mat ApplyRegionFilter(Mat screenshot, RegionConfig regionConfig)
    {
        if (regionConfig.Type == RegionType.FullScreen)
        {
            return CloneView(screenshot);
        }

        if (regionConfig.Type == RegionType.Custom && regionConfig.CustomRegion is not null)
        {
            var region = regionConfig.GetCustomRegionFor(screenshot.Width, screenshot.Height)!;
            var x = Math.Clamp(region.X, 0, Math.Max(0, screenshot.Width - 1));
            var y = Math.Clamp(region.Y, 0, Math.Max(0, screenshot.Height - 1));
            var width = Math.Min(region.Width, screenshot.Width - x);
            var height = Math.Min(region.Height, screenshot.Height - y);
            return width > 0 && height > 0
                ? new Mat(screenshot, new System.Drawing.Rectangle(x, y, width, height))
                : CloneView(screenshot);
        }

        if (regionConfig.Type != RegionType.Grid || regionConfig.GridSections is null ||
            regionConfig.GridSections.AllSectionsEnabled())
        {
            return CloneView(screenshot);
        }

        var result = new Mat(screenshot.Size, screenshot.Depth, screenshot.NumberOfChannels);
        using var mask = new Mat(screenshot.Size, DepthType.Cv8U, 1);
        mask.SetTo(new Emgu.CV.Structure.MCvScalar(0));
        var sectionWidth = screenshot.Width / 3;
        var sectionHeight = screenshot.Height / 3;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if (!regionConfig.GridSections[row, column]) continue;
                var x = column * sectionWidth;
                var y = row * sectionHeight;
                var width = column == 2 ? screenshot.Width - x : sectionWidth;
                var height = row == 2 ? screenshot.Height - y : sectionHeight;
                using var section = new Mat(mask, new System.Drawing.Rectangle(x, y, width, height));
                section.SetTo(new Emgu.CV.Structure.MCvScalar(255));
            }
        }

        CvInvoke.BitwiseAnd(screenshot, screenshot, result, mask);
        return result;
    }

    public List<MonitorInfo> GetMonitors()
    {
        try
        {
            using var capture = CaptureMonitor(1);
            return
            [
                new MonitorInfo
                {
                    Index = 1,
                    Name = "Primary display",
                    IsPrimary = true,
                    Resolution = new Resolution(capture.Width, capture.Height),
                    Position = new Models.Point(0, 0)
                }
            ];
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not query the primary display");
            return [];
        }
    }

    public Resolution GetCurrentMonitorResolution()
    {
        using var capture = CaptureMonitor(Config.MonitorIndex);
        return new Resolution(capture.Width, capture.Height);
    }

    public List<WindowInfo> GetWindows()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            return MacWindowList.GetVisibleWindows();
        }

        if (OperatingSystem.IsLinux())
        {
            return GetLinuxWindows();
        }

        var foreground = GetForegroundWindowInfo();
        return foreground is null ? [] : [foreground];
    }

    public WindowInfo? GetForegroundWindowInfo()
    {
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            {
                return MacWindowList.GetVisibleWindows().FirstOrDefault();
            }

            var xdotool = FindExecutable("xdotool");
            if (xdotool is null) return null;
            var idResult = Run(xdotool, ["getactivewindow"]);
            var id = idResult.Output.Trim();
            if (idResult.ExitCode != 0 || string.IsNullOrEmpty(id)) return null;
            var title = Run(xdotool, ["getwindowname", id]).Output.Trim();
            var pidText = Run(xdotool, ["getwindowpid", id]).Output.Trim();
            var processName = int.TryParse(pidText, out var pid) ? ReadLinuxProcessName(pid) : string.Empty;
            return new WindowInfo { Id = id, ProcessName = processName, Title = title, IsVisible = true };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not query the foreground window");
            return null;
        }
    }

    public bool IsRequiredWindowFocused(string? requiredProcessName, string? requiredTitle)
    {
        if (string.IsNullOrWhiteSpace(requiredProcessName) && string.IsNullOrWhiteSpace(requiredTitle))
        {
            return true;
        }

        var foreground = GetForegroundWindowInfo();
        return foreground is not null &&
            (string.IsNullOrWhiteSpace(requiredProcessName) || foreground.ProcessName.Contains(requiredProcessName, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(requiredTitle) || foreground.Title.Contains(requiredTitle, StringComparison.OrdinalIgnoreCase));
    }

    private static List<WindowInfo> GetLinuxWindows()
    {
        var wmctrl = FindExecutable("wmctrl");
        if (wmctrl is null) return [];
        var result = Run(wmctrl, ["-lp"]);
        if (result.ExitCode != 0) return [];

        var windows = new List<WindowInfo>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split((char[]?)null, 5, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5) continue;
            _ = int.TryParse(fields[2], out var pid);
            windows.Add(new WindowInfo
            {
                Id = fields[0],
                ProcessName = pid > 0 ? ReadLinuxProcessName(pid) : fields[3],
                Title = fields[4],
                IsVisible = true
            });
        }
        return windows;
    }

    private static string ReadLinuxProcessName(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static void CaptureLinuxScreen(string outputPath)
    {
        var commands = new[] { "grim", "gnome-screenshot", "scrot" }
            .Select(FindExecutable)
            .Where(command => command is not null)
            .Cast<string>()
            .ToList();
        if (commands.Count == 0)
        {
            throw new PlatformNotSupportedException("No Linux screen-capture command is installed.");
        }

        var errors = new List<string>();
        foreach (var command in commands)
        {
            var arguments = Path.GetFileName(command).Equals("gnome-screenshot", StringComparison.Ordinal)
                ? new[] { "-f", outputPath }
                : new[] { outputPath };
            var result = Run(command, arguments);
            if (result.ExitCode == 0 && File.Exists(outputPath))
            {
                return;
            }

            errors.Add($"{Path.GetFileName(command)}: {result.Error.Trim()}");
        }

        throw new InvalidOperationException($"Screen capture failed. {string.Join("; ", errors)}");
    }

    private static string? FindLinuxCaptureCommand() =>
        FindExecutable("grim") ?? FindExecutable("gnome-screenshot") ?? FindExecutable("scrot");

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        return string.IsNullOrWhiteSpace(path)
            ? null
            : path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, name))
                .FirstOrDefault(File.Exists);
    }

    private static void RunCapture(string fileName, IEnumerable<string> arguments)
    {
        var result = Run(fileName, arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Screen capture failed: {result.Error.Trim()}");
        }
    }

    private static ProcessResult Run(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within 15 seconds.");
        }
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static Mat LoadCapture(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("The screen-capture command did not create an image.");
        var image = CvInvoke.Imread(path, ImreadModes.Unchanged);
        if (image.IsEmpty)
        {
            image.Dispose();
            throw new InvalidOperationException("The captured image could not be decoded.");
        }
        return image;
    }

    private static Mat CloneView(Mat source) =>
        new(source, new System.Drawing.Rectangle(0, 0, source.Width, source.Height));

    private void EnsureSupported()
    {
        if (!IsSupported) throw new PlatformNotSupportedException(UnsupportedReason);
    }

    private static string CreateTemporaryPngPath() =>
        Path.Combine(Path.GetTempPath(), $"multishock-capture-{Guid.NewGuid():N}.png");

    private static void DeleteTemporaryFile(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private static class MacWindowList
    {
        private const uint OnScreenOnly = 1;
        private const uint ExcludeDesktopElements = 16;
        private const uint Utf8Encoding = 0x08000100;
        private const int SInt32Type = 3;
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        public static List<WindowInfo> GetVisibleWindows()
        {
            var result = new List<WindowInfo>();
            var array = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
            if (array == IntPtr.Zero)
            {
                return result;
            }

            var numberKey = CreateString("kCGWindowNumber");
            var ownerKey = CreateString("kCGWindowOwnerName");
            var nameKey = CreateString("kCGWindowName");
            var layerKey = CreateString("kCGWindowLayer");
            try
            {
                var count = CFArrayGetCount(array).ToInt64();
                for (long index = 0; index < count; index++)
                {
                    var dictionary = CFArrayGetValueAtIndex(array, new IntPtr(index));
                    if (dictionary == IntPtr.Zero || ReadNumber(dictionary, layerKey) != 0)
                    {
                        continue;
                    }

                    var id = ReadNumber(dictionary, numberKey);
                    var owner = ReadString(dictionary, ownerKey);
                    var title = ReadString(dictionary, nameKey);
                    if (id <= 0 || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    result.Add(new WindowInfo
                    {
                        Id = id.ToString(),
                        ProcessName = owner,
                        Title = title,
                        IsVisible = true
                    });
                }
            }
            finally
            {
                CFRelease(numberKey);
                CFRelease(ownerKey);
                CFRelease(nameKey);
                CFRelease(layerKey);
                CFRelease(array);
            }

            return result;
        }

        private static IntPtr CreateString(string value) =>
            CFStringCreateWithCString(IntPtr.Zero, value, Utf8Encoding);

        private static int ReadNumber(IntPtr dictionary, IntPtr key)
        {
            var value = CFDictionaryGetValue(dictionary, key);
            var number = 0;
            return value != IntPtr.Zero && CFNumberGetValue(value, SInt32Type, ref number) ? number : 0;
        }

        private static string ReadString(IntPtr dictionary, IntPtr key)
        {
            var value = CFDictionaryGetValue(dictionary, key);
            if (value == IntPtr.Zero)
            {
                return string.Empty;
            }

            var length = CFStringGetLength(value).ToInt64();
            var capacity = CFStringGetMaximumSizeForEncoding(new IntPtr(length), Utf8Encoding).ToInt64() + 1;
            if (capacity <= 1 || capacity > int.MaxValue)
            {
                return string.Empty;
            }

            var buffer = new byte[(int)capacity];
            return CFStringGetCString(value, buffer, new IntPtr(buffer.Length), Utf8Encoding)
                ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var end and >= 0 ? end : buffer.Length)
                : string.Empty;
        }

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFArrayGetCount(IntPtr array);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, IntPtr index);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringGetLength(IntPtr value);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringGetMaximumSizeForEncoding(IntPtr length, uint encoding);

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, IntPtr bufferSize, uint encoding);

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFNumberGetValue(IntPtr number, int type, ref int value);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr value);
    }
}
#endif
