using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;

namespace ImageDetection.Services;

public partial class ImageConfigService
{
    /// <summary>
    /// Loads an image from disk and scales it to the target resolution.
    /// </summary>
    public Mat? LoadImage(DetectionImage imageConfig, Resolution targetResolution)
    {
        try
        {
            if (!File.Exists(imageConfig.FilePath))
            {
                return null;
            }

            var image = CvInvoke.Imread(imageConfig.FilePath, ImreadModes.Unchanged);

            if (image.IsEmpty)
            {
                return null;
            }

            if (imageConfig.AutoResize)
            {
                var (scaleX, scaleY) = imageConfig.CaptureResolution.GetScaleRatios(targetResolution);

                if (Math.Abs(scaleX - 1.0) > 0.001 || Math.Abs(scaleY - 1.0) > 0.001)
                {
                    var resized = new Mat();
                    CvInvoke.Resize(image, resized, new System.Drawing.Size(0, 0), scaleX, scaleY, Inter.Area);
                    image.Dispose();
                    image = resized;
                }
            }

            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the thumbnail path for an image (creates if needed).
    /// </summary>
    public string? GetThumbnailPath(DetectionImage image, int maxSize = 100)
    {
        try
        {
            var thumbDir = Path.Combine(_dataPath, "thumbnails");
            Directory.CreateDirectory(thumbDir);

            var thumbPath = Path.Combine(thumbDir, $"{Path.GetFileNameWithoutExtension(image.FilePath)}_thumb.png");

            if (File.Exists(thumbPath))
            {
                var originalTime = File.GetLastWriteTime(image.FilePath);
                var thumbTime = File.GetLastWriteTime(thumbPath);

                if (thumbTime >= originalTime)
                {
                    return thumbPath;
                }
            }

            using var original = CvInvoke.Imread(image.FilePath, ImreadModes.Unchanged);
            if (original.IsEmpty) return null;

            double scale = Math.Min((double)maxSize / original.Width, (double)maxSize / original.Height);
            scale = Math.Min(scale, 1.0);

            using var thumb = new Mat();
            CvInvoke.Resize(original, thumb, new System.Drawing.Size(0, 0), scale, scale, Inter.Area);
            CvInvoke.Imwrite(thumbPath, thumb);

            return thumbPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Saves a cropped region screenshot as a preview image for a meter target.
    /// Returns the file path of the saved preview, or null on failure.
    /// </summary>
    public string? SaveRegionPreview(string moduleId, string targetId, Mat screenshot, ScreenRegion region)
    {
        try
        {
            var previewDir = Path.Combine(GetModulePath(moduleId), "previews");
            Directory.CreateDirectory(previewDir);

            var previewPath = Path.Combine(previewDir, $"{targetId}_region.png");

            int x = Math.Max(0, Math.Min(region.X, screenshot.Width - 1));
            int y = Math.Max(0, Math.Min(region.Y, screenshot.Height - 1));
            int width = Math.Min(region.Width, screenshot.Width - x);
            int height = Math.Min(region.Height, screenshot.Height - y);

            if (width <= 0 || height <= 0) return null;

            using var subMat = new Mat(screenshot, new System.Drawing.Rectangle(x, y, width, height));
            using var cropped = subMat.Clone();
            CvInvoke.Imwrite(previewPath, cropped);

            return previewPath;
        }
        catch
        {
            return null;
        }
    }
}
