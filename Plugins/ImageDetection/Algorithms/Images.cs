using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

public static class Images
{
    public static bool ColorAgrees(Mat frame, Mat template, DetectionResult candidate, double minimum = 0.7)
    {
        if (candidate.MatchLocation is not { } location || candidate.MatchSize is not { } size) return false;
        using var source = Images.Bgra(template);
        using var crop = new Mat(frame, new System.Drawing.Rectangle(location.X, location.Y, size.Width, size.Height));
        using var color = Images.Bgra(crop);
        using var resized = new Mat();
        CvInvoke.Resize(source, resized, color.Size, 0, 0, color.Width < source.Width ? Inter.Area : Inter.Cubic);
        using var scores = new Mat();
        CvInvoke.MatchTemplate(color, resized, scores, TemplateMatchingType.CcoeffNormed);
        double min = 0, max = 0; System.Drawing.Point minPoint = default, maxPoint = default;
        CvInvoke.MinMaxLoc(scores, ref min, ref max, ref minPoint, ref maxPoint);
        return double.IsFinite(max) && max >= minimum;
    }


    public static string Fingerprint(Mat template)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var row = new byte[template.Width * template.NumberOfChannels];
        for (int y = 0; y < template.Height; y++)
        {
            Marshal.Copy(template.DataPointer + y * template.Step, row, 0, row.Length);
            hash.AppendData(row);
        }
        return $"{template.Width}:{template.Height}:{template.NumberOfChannels}:{Convert.ToHexString(hash.GetHashAndReset())}";
    }

    public static Mat Read(string path)
    {
        var image = CvInvoke.Imread(path, ImreadModes.Unchanged);
        if (image.IsEmpty || image.Depth != DepthType.Cv8U || image.NumberOfChannels is not (1 or 3 or 4))
        {
            image.Dispose();
            throw new InvalidDataException($"Expected an 8-bit grayscale, BGR or BGRA image: {path}");
        }
        return image;
    }

    public static Mat Gray(Mat image)
    {
        if (image.NumberOfChannels == 1) return image.Clone();
        var gray = new Mat();
        CvInvoke.CvtColor(image, gray, image.NumberOfChannels == 4 ? ColorConversion.Bgra2Gray : ColorConversion.Bgr2Gray);
        return gray;
    }

    public static Mat Bgra(Mat image)
    {
        if (image.NumberOfChannels == 4) return image.Clone();
        var bgra = new Mat();
        CvInvoke.CvtColor(image, bgra, image.NumberOfChannels == 1 ? ColorConversion.Gray2Bgra : ColorConversion.Bgr2Bgra);
        return bgra;
    }
}
