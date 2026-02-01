using System.Reflection;
using System.Runtime.InteropServices;

namespace ImageDetection;

/// <summary>
/// Handles extraction and loading of embedded native libraries.
/// </summary>
public static class NativeLibraryLoader
{
    private static bool _initialized;
    private static readonly object _lock = new();
    private static string? _extractedPath;

    /// <summary>
    /// Ensures native libraries are extracted and loaded.
    /// Call this before using any Emgu.CV functionality.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            try
            {
                ExtractAndLoadNativeLibraries();
                _initialized = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize native libraries: {ex.Message}", ex);
            }
        }
    }

    private static void ExtractAndLoadNativeLibraries()
    {
        var assembly = typeof(NativeLibraryLoader).Assembly;
        
        // Create extraction directory in temp with version info
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        var extractDir = Path.Combine(Path.GetTempPath(), "ImageDetection", version, "native");
        Directory.CreateDirectory(extractDir);
        _extractedPath = extractDir;

        // Define all native libraries to extract
        var nativeLibs = new[]
        {
            ("ImageDetection.cvextern.dll", "cvextern.dll"),
            ("ImageDetection.libusb-1.0.dll", "libusb-1.0.dll"),
            ("ImageDetection.opencv_videoio_ffmpeg4120_64.dll", "opencv_videoio_ffmpeg4120_64.dll")
        };

        // Extract all native libraries
        foreach (var (resourceName, fileName) in nativeLibs)
        {
            var filePath = Path.Combine(extractDir, fileName);
            
            // Check if already extracted
            if (!File.Exists(filePath))
            {
                ExtractResource(assembly, resourceName, filePath);
            }
        }

        // Add to DLL search path BEFORE loading any libraries
        SetDllDirectory(extractDir);
        
        // Also add to PATH environment variable for the process
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!currentPath.Contains(extractDir))
        {
            Environment.SetEnvironmentVariable("PATH", extractDir + ";" + currentPath);
        }

        // Pre-load cvextern.dll (it will find its dependencies in the same directory)
        var cvexternPath = Path.Combine(extractDir, "cvextern.dll");
        var handle = LoadLibrary(cvexternPath);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to load cvextern.dll from {cvexternPath}. Error code: {error}");
        }
    }

    private static void ExtractResource(Assembly assembly, string resourceName, string outputPath)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // List available resources for debugging
            var names = assembly.GetManifestResourceNames();
            var availableResources = string.Join(", ", names);
            throw new InvalidOperationException(
                $"Resource '{resourceName}' not found. Available resources: {availableResources}");
        }

        using var output = File.Create(outputPath);
        stream.CopyTo(output);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}
