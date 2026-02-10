using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

/// <summary>
/// Computes the fill percentage of a meter/healthbar from a screen region.
/// 
/// Strategy (in priority order):
/// 1. If a color hint is provided, use HSV masking to isolate the filled portion
///    and compute the ratio of filled pixels along the bar's fill axis.
/// 2. Otherwise, use an edge/contrast approach:
///    - Convert to grayscale, apply adaptive threshold to find the boundary
///      between the filled and empty portions of the bar.
///    - Project pixel intensities along the fill axis and find the transition point.
/// 
/// Both approaches are direction-aware (L-R, R-L, T-B, B-T).
/// </summary>
public static class MeterFillAlgorithm
{
    /// <summary>
    /// Computes fill percentage (0-100) from a cropped ROI of the meter bar.
    /// </summary>
    /// <param name="roi">The cropped region containing only the meter bar.</param>
    /// <param name="config">Meter configuration (direction, color hint, etc.).</param>
    /// <returns>Fill percentage 0-100, or -1 on failure.</returns>
    public static double ComputeFillPercent(Mat roi, MeterDetectionConfig config)
    {
        if (roi == null || roi.IsEmpty || roi.Width < 2 || roi.Height < 2)
            return -1;

        if (config.UseColorHint && config.ColorHint != null)
        {
            return ComputeWithColorHint(roi, config);
        }

        return ComputeWithEdgeDetection(roi, config);
    }

    /// <summary>
    /// Color-hint approach: mask for the filled bar color in HSV, then measure
    /// how far along the fill axis the color extends.
    /// </summary>
    private static double ComputeWithColorHint(Mat roi, MeterDetectionConfig config)
    {
        var hint = config.ColorHint!;

        using var hsv = new Mat();
        var roiBgr = EnsureBgr(roi);
        CvInvoke.CvtColor(roiBgr, hsv, ColorConversion.Bgr2Hsv);
        if (roiBgr != roi) roiBgr.Dispose();

        using var mask = new Mat();

        if (hint.HueMin > hint.HueMax)
        {
            // Handle hue wrap-around for red (hue near 0/180)
            using var maskLow = new Mat();
            using var maskHigh = new Mat();
            using var lowerLow = new ScalarArray(new MCvScalar(0, hint.SatMin, hint.ValMin));
            using var upperLow = new ScalarArray(new MCvScalar(hint.HueMax, hint.SatMax, hint.ValMax));
            using var lowerHigh = new ScalarArray(new MCvScalar(hint.HueMin, hint.SatMin, hint.ValMin));
            using var upperHigh = new ScalarArray(new MCvScalar(180, hint.SatMax, hint.ValMax));

            CvInvoke.InRange(hsv, lowerLow, upperLow, maskLow);
            CvInvoke.InRange(hsv, lowerHigh, upperHigh, maskHigh);
            CvInvoke.BitwiseOr(maskLow, maskHigh, mask);
        }
        else
        {
            using var lower = new ScalarArray(new MCvScalar(hint.HueMin, hint.SatMin, hint.ValMin));
            using var upper = new ScalarArray(new MCvScalar(hint.HueMax, hint.SatMax, hint.ValMax));
            CvInvoke.InRange(hsv, lower, upper, mask);
        }

        return MeasureFillFromMask(mask, config.Direction);
    }

    /// <summary>
    /// Edge-detection approach: use intensity projection along the fill axis
    /// to find where the bar transitions from filled to empty.
    /// Works for bars where the filled portion is significantly brighter or
    /// darker than the empty portion (which is most game UIs).
    /// </summary>
    private static double ComputeWithEdgeDetection(Mat roi, MeterDetectionConfig config)
    {
        using var gray = new Mat();
        var roiBgr = EnsureBgr(roi);
        CvInvoke.CvtColor(roiBgr, gray, ColorConversion.Bgr2Gray);
        if (roiBgr != roi) roiBgr.Dispose();

        using var binary = new Mat();
        CvInvoke.Threshold(gray, binary, 0, 255, ThresholdType.Binary | ThresholdType.Otsu);

        return MeasureFillFromMask(binary, config.Direction);
    }

    /// <summary>
    /// Given a binary mask where white = filled pixels, measures how far along
    /// the fill axis the bar is filled by computing a column/row projection.
    /// </summary>
    private static double MeasureFillFromMask(Mat mask, MeterFillDirection direction)
    {
        bool isHorizontal = direction is MeterFillDirection.LeftToRight or MeterFillDirection.RightToLeft;
        int axisLength = isHorizontal ? mask.Width : mask.Height;
        int crossLength = isHorizontal ? mask.Height : mask.Width;

        if (axisLength < 2 || crossLength < 2)
            return 0;

        using var reduced = new Mat();
        if (isHorizontal)
        {
            CvInvoke.Reduce(mask, reduced, ReduceDimension.SingleRow, ReduceType.ReduceAvg, DepthType.Cv64F);
        }
        else
        {
            CvInvoke.Reduce(mask, reduced, ReduceDimension.SingleCol, ReduceType.ReduceAvg, DepthType.Cv64F);
        }

        var reducedData = new double[axisLength];
        int step = reduced.Step;

        if (isHorizontal)
        {
            Marshal.Copy(reduced.DataPointer, reducedData, 0, axisLength);
        }
        else
        {
            for (int i = 0; i < axisLength; i++)
            {
                reducedData[i] = BitConverter.Int64BitsToDouble(
                    Marshal.ReadInt64(reduced.DataPointer + i * step));
            }
        }

        // A column/row is "filled" if >50% of its cross-axis pixels are on
        // (average > 127 on 0-255 scale).
        const double fillThreshold = 127.0 * 0.5;

        int filledCount = 0;
        for (int i = 0; i < axisLength; i++)
        {
            if (reducedData[i] > fillThreshold)
                filledCount++;
        }

        double simplePercent = (double)filledCount / axisLength * 100.0;

        double edgePercent = FindFillEdge(reducedData, direction, fillThreshold);

        double fragmentRatio = filledCount > 0
            ? Math.Abs(simplePercent - edgePercent) / simplePercent
            : 0;

        double result = fragmentRatio < 0.2 ? edgePercent : simplePercent;

        return Math.Clamp(result, 0, 100);
    }

    /// <summary>
    /// Finds the fill edge position by scanning from the fill start direction
    /// and finding where the filled region ends. Tolerates small gaps (e.g.
    /// border pixels, grid lines) up to <see cref="GapTolerance"/> positions.
    /// </summary>
    private static double FindFillEdge(double[] projection, MeterFillDirection direction, double threshold)
    {
        int length = projection.Length;
        if (length == 0) return 0;

        int gapTolerance = Math.Max(2, length / 50);

        switch (direction)
        {
            case MeterFillDirection.LeftToRight:
            case MeterFillDirection.TopToBottom:
            {
                int lastFilled = -1;
                int gapCount = 0;

                for (int i = 0; i < length; i++)
                {
                    if (projection[i] > threshold)
                    {
                        lastFilled = i;
                        gapCount = 0;
                    }
                    else
                    {
                        gapCount++;
                        if (lastFilled >= 0 && gapCount > gapTolerance)
                            break;
                    }
                }

                return lastFilled >= 0 ? (double)(lastFilled + 1) / length * 100.0 : 0;
            }

            case MeterFillDirection.RightToLeft:
            case MeterFillDirection.BottomToTop:
            {
                int lastFilled = -1;
                int gapCount = 0;

                for (int i = length - 1; i >= 0; i--)
                {
                    if (projection[i] > threshold)
                    {
                        lastFilled = i;
                        gapCount = 0;
                    }
                    else
                    {
                        gapCount++;
                        if (lastFilled >= 0 && gapCount > gapTolerance)
                            break;
                    }
                }

                return lastFilled >= 0 ? (double)(length - lastFilled) / length * 100.0 : 0;
            }

            default:
                return 0;
        }
    }

    /// <summary>
    /// Auto-calculates the dominant HSV color range from a region screenshot.
    /// Analyzes the most common hue/saturation/value in the image, ignoring
    /// very dark pixels (background) and very unsaturated pixels (borders/text).
    /// Returns null if no dominant color could be determined.
    /// </summary>
    public static HsvRange? ComputeDominantHsvRange(Mat roi)
    {
        if (roi == null || roi.IsEmpty || roi.Width < 2 || roi.Height < 2)
            return null;

        try
        {
            var roiBgr = EnsureBgr(roi);
            using var hsv = new Mat();
            CvInvoke.CvtColor(roiBgr, hsv, ColorConversion.Bgr2Hsv);
            if (roiBgr != roi) roiBgr.Dispose();

            int totalPixels = hsv.Width * hsv.Height;
            var hsvData = new byte[totalPixels * 3];
            Marshal.Copy(hsv.DataPointer, hsvData, 0, hsvData.Length);

            // Collect hue, sat, val for pixels that are colorful enough
            // (skip very dark or very desaturated pixels - likely background/borders)
            var hues = new List<int>(totalPixels);
            var sats = new List<int>(totalPixels);
            var vals = new List<int>(totalPixels);

            for (int i = 0; i < totalPixels; i++)
            {
                int h = hsvData[i * 3];
                int s = hsvData[i * 3 + 1];
                int v = hsvData[i * 3 + 2];

                if (s >= 30 && v >= 40)
                {
                    hues.Add(h);
                    sats.Add(s);
                    vals.Add(v);
                }
            }

            // If we have very few colorful pixels, the bar might be mostly white/gray.
            if (hues.Count < totalPixels * 0.05)
            {
                hues.Clear(); sats.Clear(); vals.Clear();
                for (int i = 0; i < totalPixels; i++)
                {
                    int v = hsvData[i * 3 + 2];
                    if (v >= 40)
                    {
                        hues.Add(hsvData[i * 3]);
                        sats.Add(hsvData[i * 3 + 1]);
                        vals.Add(hsvData[i * 3 + 2]);
                    }
                }

                if (hues.Count == 0) return null;

                // Likely a low-saturation bar (white/gray)
                hues.Sort(); sats.Sort(); vals.Sort();
                return new HsvRange
                {
                    HueMin = 0,
                    HueMax = 180,
                    SatMin = 0,
                    SatMax = Math.Min(255, Percentile(sats, 90) + 20),
                    ValMin = Math.Max(0, Percentile(vals, 10) - 20),
                    ValMax = 255
                };
            }

            hues.Sort();
            sats.Sort();
            vals.Sort();

            int hueP10 = Percentile(hues, 10);
            int hueP90 = Percentile(hues, 90);
            int satP10 = Percentile(sats, 10);
            int valP10 = Percentile(vals, 10);

            // Check for hue wrap-around (red hues near 0 and 180)
            bool hasWrap = false;
            int lowCount = hues.Count(h => h <= 15);
            int highCount = hues.Count(h => h >= 165);
            if (lowCount > hues.Count * 0.1 && highCount > hues.Count * 0.1)
            {
                hasWrap = true;
            }

            int hueMin, hueMax;
            if (hasWrap)
            {
                // For wrap-around hues, compute separate percentiles for low and high ranges
                hueMin = Math.Max(0, Percentile([.. hues.Where(h => h >= 165)], 10) - 5);
                hueMax = Math.Min(180, Percentile([.. hues.Where(h => h <= 15)], 90) + 5);
            }
            else
            {
                // Add margin around the detected range
                hueMin = Math.Max(0, hueP10 - 10);
                hueMax = Math.Min(180, hueP90 + 10);
            }

            return new HsvRange
            {
                HueMin = hueMin,
                HueMax = hueMax,
                SatMin = Math.Max(0, satP10 - 20),
                SatMax = 255,
                ValMin = Math.Max(0, valP10 - 20),
                ValMax = 255
            };
        }
        catch
        {
            return null;
        }
    }

    private static int Percentile(List<int> sorted, int p)
    {
        if (sorted.Count == 0) return 0;
        int index = (int)Math.Floor(sorted.Count * p / 100.0);
        return sorted[Math.Min(index, sorted.Count - 1)];
    }

    /// <summary>
    /// Ensures the Mat is in BGR format (3 channels).
    /// Returns the same Mat if already BGR, or a new converted Mat.
    /// Caller must dispose the returned Mat only if it differs from input.
    /// </summary>
    private static Mat EnsureBgr(Mat image)
    {
        if (image.NumberOfChannels == 3)
            return image;

        var converted = new Mat();
        if (image.NumberOfChannels == 4)
            CvInvoke.CvtColor(image, converted, ColorConversion.Bgra2Bgr);
        else if (image.NumberOfChannels == 1)
            CvInvoke.CvtColor(image, converted, ColorConversion.Gray2Bgr);
        else
            return image;

        return converted;
    }
}
