#if !WINDOWS
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;

namespace ImageDetection.Services;

internal static class CaptureRegionFilter
{
    public static Mat Apply(Mat screenshot, RegionConfig regionConfig)
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

    private static Mat CloneView(Mat source) =>
        new(source, new System.Drawing.Rectangle(0, 0, source.Width, source.Height));
}
#endif
